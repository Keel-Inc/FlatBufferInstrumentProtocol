using System;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Google.FlatBuffers;
using InstrumentProtocol;
using System.Linq;

[assembly: InternalsVisibleTo("Host.Tests")]

namespace InstrumentProtocolHost;

public class Program
{
    public const string PipeName = "InstrumentProtocol";
    public const string DeviceId = "HostApplication_001";
    public const int TimeoutMs = 1000; // 1 seconds
    
    static async Task Main(string[] args)
    {
        Console.WriteLine("Medical Instrument Host v1.0");
        Console.WriteLine("=============================");
        
        var host = new InstrumentHost();
        await host.RunAsync();
    }
}

public class InstrumentHost
{
    private NamedPipeClientStream? _pipeClient;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    
    // Constructor for testing
    internal InstrumentHost() { }
    
    public async Task RunAsync()
    {
        try
        {
            await ConnectToInstrumentAsync();
            await RunProtocolDemoAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
        finally
        {
            await DisconnectAsync();
        }
    }
    
    private async Task ConnectToInstrumentAsync()
    {
        Console.WriteLine($"Connecting to named pipe: {Program.PipeName}");
        
        _pipeClient = new NamedPipeClientStream(".", Program.PipeName, PipeDirection.InOut);
        
        try
        {
            await _pipeClient.ConnectAsync(Program.TimeoutMs, _cancellationTokenSource.Token);
            Console.WriteLine("Connected to instrument!");
        }
        catch (TimeoutException)
        {
            throw new Exception($"Failed to connect to instrument within {Program.TimeoutMs}ms. Is the instrument running?");
        }
    }
    
    private async Task RunProtocolDemoAsync()
    {
        if (_pipeClient == null || !_pipeClient.IsConnected)
            throw new InvalidOperationException("Not connected to instrument");
            
        Console.WriteLine("\nStarting protocol demonstration...");
        
        // Send configuration
        await SendConfigurationAsync(10, 5); // 10 Hz, 5 samples per measurement
        
        // Send start command
        await SendCommandAsync(CommandCode.Start);
        
        // Receive measurements for 5 seconds
        var measurementTask1 = ReceiveMeasurementsAsync(_cancellationTokenSource.Token);
        await Task.Delay(500, _cancellationTokenSource.Token);
        
        // Send stop command
        await SendCommandAsync(CommandCode.Stop);
        
        Console.WriteLine("Protocol demonstration completed.");
    }
    
    private async Task SendConfigurationAsync(uint measurementsPerSecond, uint samplesPerMeasurement)
    {
        Console.WriteLine($"Sending configuration: {measurementsPerSecond} Hz, {samplesPerMeasurement} samples");
        
        var builder = new FlatBufferBuilder(1024);
        
        // Create configuration
        var configOffset = Configuration.CreateConfiguration(
            builder,
            measurementsPerSecond,
            samplesPerMeasurement
        );
        
        // Create message
        var messageOffset = Message.CreateMessage(
            builder,
            MessageType.Configuration,
            configOffset.Value
        );
        
        builder.FinishSizePrefixed(messageOffset.Value);
        
        await SendBufferAsync(builder.DataBuffer);
    }
    
    private async Task SendCommandAsync(CommandCode commandCode)
    {
        Console.WriteLine($"Sending command: {commandCode}");
        
        var builder = new FlatBufferBuilder(1024);
        
        // Create command
        var commandOffset = Command.CreateCommand(
            builder,
            commandCode
        );
        
        // Create message
        var messageOffset = Message.CreateMessage(
            builder,
            MessageType.Command,
            commandOffset.Value
        );
        
        builder.FinishSizePrefixed(messageOffset.Value);
        
        await SendBufferAsync(builder.DataBuffer);
    }
    
    private async Task SendBufferAsync(Google.FlatBuffers.ByteBuffer buffer)
    {
        if (_pipeClient == null || !_pipeClient.IsConnected)
            throw new InvalidOperationException("Not connected to instrument");
            
        var data = buffer.ToSizedArray();
        await _pipeClient.WriteAsync(data, 0, data.Length, _cancellationTokenSource.Token);
        await _pipeClient.FlushAsync(_cancellationTokenSource.Token);
    }
    
    private async Task ReceiveMeasurementsAsync(CancellationToken cancellationToken)
    {
        if (_pipeClient == null || !_pipeClient.IsConnected)
            return;
            
        var buffer = new byte[4096];
        int measurementCount = 0;
        
        try
        {
            while (!cancellationToken.IsCancellationRequested && _pipeClient.IsConnected)
            {
                var bytesRead = await _pipeClient.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                
                if (bytesRead > 0)
                {
                    ProcessReceivedData(buffer, bytesRead, ref measurementCount);
                }
                else
                {
                    // No data received, small delay to prevent busy waiting
                    await Task.Delay(10, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error receiving measurements: {ex.Message}");
        }
    }
    
    internal void ProcessReceivedData(byte[] buffer, int length, ref int measurementCount)
    {
        try
        {
            // Size-prefixed buffers need at least 4 bytes for the size prefix + minimum FlatBuffer data
            if (length < 8) // 4 bytes for size prefix + 4 bytes minimum FlatBuffer
            {
                Console.WriteLine($"Received incomplete data: {length} bytes (minimum 8 required)");
                _cancellationTokenSource.Cancel();
                return;
            }
            
            // Read the size prefix (first 4 bytes)
            var sizePrefix = BitConverter.ToUInt32(buffer, 0);
            
            // Validate size prefix is reasonable - if it's extremely large, it's likely corrupted data
            if (sizePrefix > 1024 * 1024) // 1MB seems like a reasonable maximum for our protocol
            {
                Console.WriteLine($"Received invalid FlatBuffer data");
                LogBufferDetails(buffer, length);
                _cancellationTokenSource.Cancel();
                return;
            }
            
            // Validate that we have enough data for the complete message
            var expectedTotalSize = 4 + (int)sizePrefix; // 4 bytes prefix + message size
            if (length < expectedTotalSize)
            {
                Console.WriteLine($"Received incomplete message: received {length} bytes, expected {expectedTotalSize} bytes");
                _cancellationTokenSource.Cancel();
                return;
            }
            
            // Create a ByteBuffer with the complete message data (including size prefix)
            var messageData = new byte[expectedTotalSize];
            Array.Copy(buffer, messageData, expectedTotalSize);
            var byteBuffer = new ByteBuffer(messageData);
            
            // Use FlatBuffers verification methods for size-prefixed buffers
            try
            {
                var verifier = new Google.FlatBuffers.Verifier(byteBuffer);
                bool isValidMessage = verifier.VerifyBuffer(null, true, MessageVerify.Verify);
                
                if (!isValidMessage)
                {
                    Console.WriteLine($"Received invalid FlatBuffer data");
                    LogBufferDetails(messageData, expectedTotalSize);
                    _cancellationTokenSource.Cancel();
                    return;
                }
            }
            catch (Exception)
            {
                Console.WriteLine($"Received invalid FlatBuffer data");
                LogBufferDetails(messageData, expectedTotalSize);
                _cancellationTokenSource.Cancel();
                return;
            }
            
            var bufferWithoutPrefix = Google.FlatBuffers.ByteBufferUtil.RemoveSizePrefix(byteBuffer);
            var message = Message.GetRootAsMessage(bufferWithoutPrefix);
            
            if (message.MessageTypeType == MessageType.Measurement)
            {
                var measurement = message.MessageType<Measurement>();
                if (measurement.HasValue)
                {
                    var measurementValue = measurement.Value;
                    
                    measurementCount++;
                    var dataLength = measurementValue.DataLength;
                    
                    // Display all values
                    var sampleValues = new float[dataLength];
                    for (int i = 0; i < dataLength; i++)
                    {
                        sampleValues[i] = measurementValue.Data(i);
                    }
                    
                    Console.WriteLine($"Measurement #{measurementCount} (samples: {dataLength})");
                    Console.WriteLine($"  Samples: [{string.Join(", ", sampleValues.Select(v => $"{v:F2}"))}]");
                }
            }
            else
            {
                Console.WriteLine($"Received unexpected message type: {message.MessageTypeType}");
            }
        }
        catch (Exception)
        {
            Console.WriteLine($"Received invalid FlatBuffer data");
            LogBufferDetails(buffer, length);
            _cancellationTokenSource.Cancel();
        }
    }
    
    private static void LogBufferDetails(byte[] data, int length)
    {
        Console.WriteLine($"Buffer details (length: {length}):");
        
        // Show first 64 bytes or full length if shorter
        var displayLength = Math.Min(64, length);
        var hexBytes = data.Take(displayLength).Select(b => $"{b:X2}").ToArray();
        Console.WriteLine($"  Hex: {string.Join(" ", hexBytes)}");
        
        // Show if it looks like text
        var printableChars = data.Take(displayLength)
            .Select(b => b >= 32 && b <= 126 ? (char)b : '.')
            .ToArray();
        Console.WriteLine($"  ASCII: {new string(printableChars)}");
        
        // Check if it looks like text data
        var textBytes = data.Take(Math.Min(32, length)).Count(b => b >= 32 && b <= 126);
        var textPercentage = (double)textBytes / Math.Min(32, length) * 100;
        
        if (textPercentage > 70)
        {
            Console.WriteLine($"  >> This appears to be TEXT data ({textPercentage:F0}% printable characters)");
            
            // Try to show as string
            try
            {
                var textData = System.Text.Encoding.UTF8.GetString(data, 0, length);
                Console.WriteLine($"  >> As text: '{textData}'");
            }
            catch
            {
                Console.WriteLine($"  >> Could not decode as UTF-8 text");
            }
        }
        else
        {
            Console.WriteLine($"  >> This appears to be BINARY data ({textPercentage:F0}% printable characters)");
        }
        
        // Check for common protocol patterns
        if (length >= 4)
        {
            var first4Bytes = BitConverter.ToUInt32(data, 0);
            Console.WriteLine($"  >> First 4 bytes as uint32: {first4Bytes} (0x{first4Bytes:X8})");
            
            // FlatBuffer messages typically start with a small offset (usually < 1000)
            if (first4Bytes > 0 && first4Bytes < 1000)
            {
                Console.WriteLine($"  >> Could be FlatBuffer offset: {first4Bytes}");
            }
        }
    }
    
    private static ulong GetCurrentTimestampMs()
    {
        return (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
    
    private async Task DisconnectAsync()
    {
        _cancellationTokenSource.Cancel();
        
        if (_pipeClient != null)
        {
            try
            {
                if (_pipeClient.IsConnected)
                {
                    Console.WriteLine("Disconnecting from instrument...");
                    _pipeClient.Close();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during disconnect: {ex.Message}");
            }
            finally
            {
                _pipeClient.Dispose();
                _pipeClient = null;
            }
        }
        
        _cancellationTokenSource.Dispose();
        await Task.CompletedTask;
    }
} 