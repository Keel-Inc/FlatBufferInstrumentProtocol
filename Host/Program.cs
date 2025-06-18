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
using System.Collections.Generic;
using CompactFrameFormat;

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
    private readonly List<byte> _receiveBuffer = new();
    private ushort _frameCounter = 0;

    internal InstrumentHost() 
    { 
        _communicator = null!;
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
        
        builder.Finish(messageOffset.Value);
        
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
        
        builder.Finish(messageOffset.Value);
        
        await SendBufferAsync(builder.DataBuffer);
    }
    
    private async Task SendBufferAsync(Google.FlatBuffers.ByteBuffer buffer)
    {
        if (!_communicator.IsConnected) {
            throw new InvalidOperationException("Not connected to instrument");
        }

        var payload = buffer.ToSizedArray();
        var frame = Cff.CreateFrame(payload, _frameCounter++);
        await _communicator.WriteAsync(frame, 0, frame.Length, _cancellationTokenSource.Token);
        await _communicator.FlushAsync(_cancellationTokenSource.Token);
    }
    
    private async Task ReceiveMeasurementsAsync(CancellationToken cancellationToken)
    {
        if (!_communicator.IsConnected) {
            return;
        }
            
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
        // Add new data to our internal buffer
        for (int i = 0; i < length; i++)
        {
            _receiveBuffer.Add(buffer[i]);
        }
        
        ProcessAllFramesInBuffer(ref measurementCount);
    }
    
    private void ProcessAllFramesInBuffer(ref int measurementCount)
    {
        try
        {
            var bufferArray = _receiveBuffer.ToArray();
            
            var foundFrames = Cff.FindFrames(bufferArray, out int consumedBytes);
            
            foreach (var frame in foundFrames)
            {
                try
                {
                    ProcessMessage(frame.Payload.ToArray(), ref measurementCount);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing frame: {ex.Message}");
                }
            }
            
            if (consumedBytes > 0)
            {
                _receiveBuffer.RemoveRange(0, consumedBytes);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing frames in buffer: {ex.Message}");
            _cancellationTokenSource.Cancel();
        }
    }

    private void ProcessMessage(byte[] messageData, ref int measurementCount)
    {
        try
        {
            // Create a ByteBuffer with the message data
            var byteBuffer = new ByteBuffer(messageData);
            
            // Verify FlatBuffer structure
            try
            {
                var verifier = new Google.FlatBuffers.Verifier(byteBuffer);
                bool isValidMessage = verifier.VerifyBuffer(null, false, MessageVerify.Verify);
                
                if (!isValidMessage)
                {
                    Console.WriteLine($"Received invalid FlatBuffer message data");
                    LogBufferDetails(messageData, messageData.Length);
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Received invalid FlatBuffer message data: {ex.Message}");
                LogBufferDetails(messageData, messageData.Length);
                return;
            }
            
            var message = Message.GetRootAsMessage(byteBuffer);
            
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
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing message: {ex.Message}");
            LogBufferDetails(messageData, messageData.Length);
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
    
        // Check for common protocol patterns
        if (length >= 4)
        {
            var first4Bytes = BitConverter.ToUInt32(data, 0);
            Console.WriteLine($"  >> First 4 bytes as uint32: {first4Bytes} (0x{first4Bytes:X8})");
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