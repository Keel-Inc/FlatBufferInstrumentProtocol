using System;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Google.FlatBuffers;
using InstrumentProtocol;
using System.Linq;
using CommandLine;

[assembly: InternalsVisibleTo("Host.Tests")]

namespace InstrumentProtocolHost;

public class Program
{
    public const string PipeName = "InstrumentProtocol";
    public const string DeviceId = "HostApplication_001";
    public const int TimeoutMs = 1000; // 1 seconds
    
    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("Medical Instrument Host v1.0");
        Console.WriteLine("=============================");

        var result = Parser.Default.ParseArguments<Options>(args);
        
        return await result.MapResult(
            async (Options opts) => await RunWithOptionsAsync(opts),
            async (IEnumerable<Error> errors) => await Task.FromResult(HandleParseErrors(errors)));
    }

    private static async Task<int> RunWithOptionsAsync(Options options)
    {
        try
        {
            var config = new ConnectionConfig
            {
                ConnectionType = options.ConnectionType,
                TcpHost = options.Host,
                TcpPort = options.Port,
                PipeName = options.PipeName
            };

            var communicator = CreateCommunicator(config);
            var instrumentHost = new InstrumentHost(communicator);
            await instrumentHost.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Application error: {ex.Message}");
            return 1;
        }
    }

    private static int HandleParseErrors(IEnumerable<Error> errors)
    {
        var errorList = errors.ToList();
        
        // Don't show errors for help or version requests
        if (errorList.Any(e => e.Tag == ErrorType.HelpRequestedError || e.Tag == ErrorType.VersionRequestedError))
        {
            return 0;
        }

        Console.WriteLine("Command line parsing failed:");
        foreach (var error in errorList)
        {
            Console.WriteLine($"  {error}");
        }
        return 1;
    }

    public static ICommunicator CreateCommunicator(ConnectionConfig config)
    {
        return config.ConnectionType switch
        {
            ConnectionType.NamedPipe => new NamedPipeCommunicator(config.PipeName),
            ConnectionType.TcpSocket => new TcpSocketCommunicator(config.TcpHost, config.TcpPort),
            _ => throw new ArgumentException($"Unsupported connection type: {config.ConnectionType}")
        };
    }
}

public class Options
{
    [Option('c', "connection", Required = false, Default = ConnectionType.NamedPipe,
        HelpText = "Connection type to use (NamedPipe or TcpSocket).")]
    public ConnectionType ConnectionType { get; set; }

    [Option('h', "host", Required = false, Default = "localhost",
        HelpText = "TCP host to connect to (when using TCP).")]
    public string Host { get; set; } = "localhost";

    [Option('p', "port", Required = false, Default = 1234,
        HelpText = "TCP port to connect to (when using TCP).")]
    public int Port { get; set; }

    [Option("pipe-name", Required = false, Default = Program.PipeName,
        HelpText = "Named pipe name (when using named pipes).")]
    public string PipeName { get; set; } = Program.PipeName;
}

public enum ConnectionType
{
    NamedPipe,
    TcpSocket
}

public class ConnectionConfig
{
    public ConnectionType ConnectionType { get; set; }
    public string TcpHost { get; set; } = "localhost";
    public int TcpPort { get; set; } = 1234;
    public string PipeName { get; set; } = Program.PipeName;
}

// Communication abstraction layer
public interface ICommunicator : IDisposable
{
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default);
    Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default);
    Task FlushAsync(CancellationToken cancellationToken = default);
    bool IsConnected { get; }
    string ConnectionDescription { get; }
}

public class NamedPipeCommunicator : ICommunicator
{
    private readonly string _pipeName;
    private NamedPipeClientStream? _pipeClient;

    public NamedPipeCommunicator(string pipeName)
    {
        _pipeName = pipeName;
    }

    public bool IsConnected => _pipeClient?.IsConnected ?? false;
    public string ConnectionDescription => $"Named Pipe: {_pipeName}";

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _pipeClient = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut);
        
        try
        {
            await _pipeClient.ConnectAsync(Program.TimeoutMs, cancellationToken);
            Console.WriteLine($"Connected via {ConnectionDescription}");
        }
        catch (TimeoutException)
        {
            throw new Exception($"Failed to connect to named pipe '{_pipeName}' within {Program.TimeoutMs}ms. Is the instrument running?");
        }
    }

    public async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
    {
        if (_pipeClient == null || !_pipeClient.IsConnected)
            throw new InvalidOperationException("Not connected");
            
        return await _pipeClient.ReadAsync(buffer, offset, count, cancellationToken);
    }

    public async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
    {
        if (_pipeClient == null || !_pipeClient.IsConnected)
            throw new InvalidOperationException("Not connected");
            
        await _pipeClient.WriteAsync(buffer, offset, count, cancellationToken);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_pipeClient == null || !_pipeClient.IsConnected)
            throw new InvalidOperationException("Not connected");
            
        await _pipeClient.FlushAsync(cancellationToken);
    }

    public void Dispose()
    {
        _pipeClient?.Close();
        _pipeClient?.Dispose();
        _pipeClient = null;
    }
}

public class TcpSocketCommunicator : ICommunicator
{
    private readonly string _host;
    private readonly int _port;
    private TcpClient? _tcpClient;
    private NetworkStream? _networkStream;

    public TcpSocketCommunicator(string host, int port)
    {
        _host = host;
        _port = port;
    }

    public bool IsConnected => _tcpClient?.Connected ?? false;
    public string ConnectionDescription => $"TCP Socket: {_host}:{_port}";

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _tcpClient = new TcpClient();
        
        try
        {
            // Connect with timeout
            var connectTask = _tcpClient.ConnectAsync(_host, _port);
            var timeoutTask = Task.Delay(Program.TimeoutMs, cancellationToken);
            
            var completedTask = await Task.WhenAny(connectTask, timeoutTask);
            
            if (completedTask == timeoutTask)
            {
                _tcpClient.Close();
                throw new TimeoutException($"Failed to connect to TCP socket {_host}:{_port} within {Program.TimeoutMs}ms");
            }
            
            await connectTask; // Re-await to propagate any exceptions
            
            _networkStream = _tcpClient.GetStream();
            Console.WriteLine($"Connected via {ConnectionDescription}");
        }
        catch (Exception ex) when (!(ex is TimeoutException))
        {
            _tcpClient?.Close();
            throw new Exception($"Failed to connect to TCP socket {_host}:{_port}: {ex.Message}", ex);
        }
    }

    public async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
    {
        if (_networkStream == null || !IsConnected)
            throw new InvalidOperationException("Not connected");
            
        return await _networkStream.ReadAsync(buffer, offset, count, cancellationToken);
    }

    public async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
    {
        if (_networkStream == null || !IsConnected)
            throw new InvalidOperationException("Not connected");
            
        await _networkStream.WriteAsync(buffer, offset, count, cancellationToken);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_networkStream == null || !IsConnected)
            throw new InvalidOperationException("Not connected");
            
        await _networkStream.FlushAsync(cancellationToken);
    }

    public void Dispose()
    {
        _networkStream?.Close();
        _networkStream?.Dispose();
        _networkStream = null;
        
        _tcpClient?.Close();
        _tcpClient?.Dispose();
        _tcpClient = null;
    }
}

public class InstrumentHost
{
    private readonly ICommunicator _communicator;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    
    // Constructor for testing
    internal InstrumentHost() 
    { 
        _communicator = null!; // For testing only
    }

    public InstrumentHost(ICommunicator communicator)
    {
        _communicator = communicator ?? throw new ArgumentNullException(nameof(communicator));
    }
    
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
        Console.WriteLine($"Connecting to instrument via {_communicator.ConnectionDescription}");
        await _communicator.ConnectAsync(_cancellationTokenSource.Token);
    }
    
    private async Task RunProtocolDemoAsync()
    {
        if (!_communicator.IsConnected)
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
        
        // Create command - fully qualify to avoid ambiguity with CommandLine.Command
        var commandOffset = InstrumentProtocol.Command.CreateCommand(
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
        if (!_communicator.IsConnected)
            throw new InvalidOperationException("Not connected to instrument");
            
        var data = buffer.ToSizedArray();
        await _communicator.WriteAsync(data, 0, data.Length, _cancellationTokenSource.Token);
        await _communicator.FlushAsync(_cancellationTokenSource.Token);
    }
    
    private async Task ReceiveMeasurementsAsync(CancellationToken cancellationToken)
    {
        if (!_communicator.IsConnected)
            return;
            
        var buffer = new byte[4096];
        int measurementCount = 0;
        
        try
        {
            while (!cancellationToken.IsCancellationRequested && _communicator.IsConnected)
            {
                var bytesRead = await _communicator.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                
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
        
        try
        {
            if (_communicator.IsConnected)
            {
                Console.WriteLine("Disconnecting from instrument...");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during disconnect: {ex.Message}");
        }
        finally
        {
            _communicator?.Dispose();
        }
        
        _cancellationTokenSource.Dispose();
        await Task.CompletedTask;
    }
} 