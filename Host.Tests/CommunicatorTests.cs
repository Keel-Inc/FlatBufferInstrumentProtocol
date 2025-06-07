using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using InstrumentProtocolHost;

namespace Host.Tests;

public class CommunicatorTests
{
    private readonly ITestOutputHelper _output;

    public CommunicatorTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void ConnectionConfig_ShouldHaveCorrectDefaults()
    {
        // Arrange & Act
        var config = new ConnectionConfig();
        
        // Assert
        Assert.Equal(ConnectionType.NamedPipe, config.ConnectionType);
        Assert.Equal("localhost", config.TcpHost);
        Assert.Equal(1234, config.TcpPort);
        Assert.Equal(Program.PipeName, config.PipeName);
    }

    [Fact]
    public void Options_ShouldHaveCorrectDefaults()
    {
        // Arrange & Act
        var options = new Options();
        
        // Assert - Test that the C# property defaults are set
        Assert.Equal(ConnectionType.NamedPipe, options.ConnectionType);
        Assert.Equal("localhost", options.Host);
        Assert.Equal(0, options.Port); // Default int value, will be set to 1234 by CommandLineParser during parsing
        Assert.Equal(Program.PipeName, options.PipeName);
    }

    [Fact]
    public void CommandLineParser_WithNoArguments_ShouldUseDefaults()
    {
        // Arrange
        var args = Array.Empty<string>();
        
        // Act
        var result = CommandLine.Parser.Default.ParseArguments<Options>(args);
        
        // Assert
        Assert.IsType<CommandLine.Parsed<Options>>(result);
        var parsed = (CommandLine.Parsed<Options>)result;
        var options = parsed.Value;
        
        Assert.Equal(ConnectionType.NamedPipe, options.ConnectionType);
        Assert.Equal("localhost", options.Host);
        Assert.Equal(1234, options.Port);
        Assert.Equal(Program.PipeName, options.PipeName);
    }

    [Fact]
    public void CommandLineParser_WithTcpArguments_ShouldParseCorrectly()
    {
        // Arrange
        var args = new[] { "-c", "TcpSocket", "-h", "example.com", "-p", "9999" };
        
        // Act
        var result = CommandLine.Parser.Default.ParseArguments<Options>(args);
        
        // Assert
        Assert.IsType<CommandLine.Parsed<Options>>(result);
        var parsed = (CommandLine.Parsed<Options>)result;
        var options = parsed.Value;
        
        Assert.Equal(ConnectionType.TcpSocket, options.ConnectionType);
        Assert.Equal("example.com", options.Host);
        Assert.Equal(9999, options.Port);
        Assert.Equal(Program.PipeName, options.PipeName); // Should use default
    }

    [Fact]
    public void ConnectionType_EnumValues_ShouldBeValid()
    {
        // Test that the enum values are defined correctly
        Assert.True(Enum.IsDefined(typeof(ConnectionType), ConnectionType.NamedPipe));
        Assert.True(Enum.IsDefined(typeof(ConnectionType), ConnectionType.TcpSocket));
    }

    [Fact]
    public void Program_CreateCommunicator_WithNamedPipe_ShouldReturnNamedPipeCommunicator()
    {
        // Arrange
        var config = new ConnectionConfig
        {
            ConnectionType = ConnectionType.NamedPipe,
            PipeName = "TestPipe"
        };

        // Act
        var communicator = Program.CreateCommunicator(config);

        // Assert
        Assert.IsType<NamedPipeCommunicator>(communicator);
        Assert.Contains("TestPipe", communicator.ConnectionDescription);
        Assert.False(communicator.IsConnected);
        
        // Cleanup
        communicator.Dispose();
    }

    [Fact]
    public void Program_CreateCommunicator_WithTcpSocket_ShouldReturnTcpSocketCommunicator()
    {
        // Arrange
        var config = new ConnectionConfig
        {
            ConnectionType = ConnectionType.TcpSocket,
            TcpHost = "example.com",
            TcpPort = 9999
        };

        // Act
        var communicator = Program.CreateCommunicator(config);

        // Assert
        Assert.IsType<TcpSocketCommunicator>(communicator);
        Assert.Contains("example.com:9999", communicator.ConnectionDescription);
        Assert.False(communicator.IsConnected);
        
        // Cleanup
        communicator.Dispose();
    }

    [Fact]
    public void Program_CreateCommunicator_WithInvalidType_ShouldThrowException()
    {
        // Arrange
        var config = new ConnectionConfig
        {
            ConnectionType = (ConnectionType)999 // Invalid enum value
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => Program.CreateCommunicator(config));
        Assert.Contains("Unsupported connection type", exception.Message);
    }

    [Fact]
    public void NamedPipeCommunicator_Constructor_ShouldSetProperties()
    {
        // Arrange
        const string pipeName = "TestPipe";

        // Act
        using var communicator = new NamedPipeCommunicator(pipeName);

        // Assert
        Assert.Equal($"Named Pipe: {pipeName}", communicator.ConnectionDescription);
        Assert.False(communicator.IsConnected);
    }

    [Fact]
    public void TcpSocketCommunicator_Constructor_ShouldSetProperties()
    {
        // Arrange
        const string host = "testhost";
        const int port = 1337;

        // Act
        using var communicator = new TcpSocketCommunicator(host, port);

        // Assert
        Assert.Equal($"TCP Socket: {host}:{port}", communicator.ConnectionDescription);
        Assert.False(communicator.IsConnected);
    }

    [Fact]
    public async Task NamedPipeCommunicator_ConnectAsync_WithNonExistentPipe_ShouldThrowException()
    {
        // Arrange
        using var communicator = new NamedPipeCommunicator("NonExistentPipe");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<Exception>(() => 
            communicator.ConnectAsync(cts.Token));
        
        Assert.Contains("Failed to connect to named pipe", exception.Message);
        Assert.Contains("NonExistentPipe", exception.Message);
    }

    [Fact]
    public async Task TcpSocketCommunicator_ConnectAsync_WithUnreachableHost_ShouldThrowTimeoutException()
    {
        // Arrange
        using var communicator = new TcpSocketCommunicator("127.0.0.1", 65432); // Unlikely to be in use
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<TimeoutException>(() => 
            communicator.ConnectAsync(cts.Token));
        
        Assert.Contains("Failed to connect to TCP socket", exception.Message);
        Assert.Contains("127.0.0.1:65432", exception.Message);
    }

    [Fact]
    public async Task TcpSocketCommunicator_ConnectAsync_WithTimeout_ShouldThrowTimeoutException()
    {
        // Arrange - Use a black hole IP address that will timeout
        using var communicator = new TcpSocketCommunicator("192.0.2.1", 80); // RFC 5737 test address
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<TimeoutException>(() => 
            communicator.ConnectAsync(cts.Token));
        
        Assert.Contains("Failed to connect to TCP socket", exception.Message);
        Assert.Contains("192.0.2.1:80", exception.Message);
    }

    [Fact]
    public async Task NamedPipeCommunicator_Operations_WhenNotConnected_ShouldThrowInvalidOperation()
    {
        // Arrange
        using var communicator = new NamedPipeCommunicator("TestPipe");
        var buffer = new byte[10];

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            communicator.ReadAsync(buffer, 0, buffer.Length));
        
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            communicator.WriteAsync(buffer, 0, buffer.Length));
        
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            communicator.FlushAsync());
    }

    [Fact]
    public async Task TcpSocketCommunicator_Operations_WhenNotConnected_ShouldThrowInvalidOperation()
    {
        // Arrange
        using var communicator = new TcpSocketCommunicator("localhost", 1234);
        var buffer = new byte[10];

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            communicator.ReadAsync(buffer, 0, buffer.Length));
        
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            communicator.WriteAsync(buffer, 0, buffer.Length));
        
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            communicator.FlushAsync());
    }

    [Fact]
    public async Task TcpSocketCommunicator_FullCommunication_ShouldWork()
    {
        // Arrange
        const int port = 0; // Let the OS choose an available port
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        var actualPort = ((IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            using var communicator = new TcpSocketCommunicator("127.0.0.1", actualPort);
            var testData = System.Text.Encoding.UTF8.GetBytes("Hello, TCP!");
            var readBuffer = new byte[testData.Length];

            // Start a simple echo server
            var serverTask = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync();
                using var stream = client.GetStream();
                
                var buffer = new byte[1024];
                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                await stream.WriteAsync(buffer, 0, bytesRead);
                await stream.FlushAsync();
            });

            // Capture console output to avoid test output interference
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);

            try
            {
                // Act
                await communicator.ConnectAsync();
                Assert.True(communicator.IsConnected);

                await communicator.WriteAsync(testData, 0, testData.Length);
                await communicator.FlushAsync();

                var bytesRead = await communicator.ReadAsync(readBuffer, 0, readBuffer.Length);

                // Assert
                Assert.Equal(testData.Length, bytesRead);
                Assert.Equal(testData, readBuffer);

                await serverTask; // Ensure server completes
                
                // Log the console output to test output
                var consoleOutput = sw.ToString();
                if (!string.IsNullOrEmpty(consoleOutput))
                {
                    _output.WriteLine($"Console output: {consoleOutput.Trim()}");
                }
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void NamedPipeCommunicator_Dispose_ShouldCleanupResources()
    {
        // Arrange
        var communicator = new NamedPipeCommunicator("TestPipe");

        // Act & Assert - Should not throw
        communicator.Dispose();
        
        // Multiple dispose calls should not throw
        communicator.Dispose();
    }

    [Fact]
    public void TcpSocketCommunicator_Dispose_ShouldCleanupResources()
    {
        // Arrange
        var communicator = new TcpSocketCommunicator("localhost", 1234);

        // Act & Assert - Should not throw
        communicator.Dispose();
        
        // Multiple dispose calls should not throw
        communicator.Dispose();
    }

    [Fact]
    public async Task TcpSocketCommunicator_Dispose_AfterConnect_ShouldDisconnect()
    {
        // Arrange
        const int port = 0;
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        var actualPort = ((IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            var communicator = new TcpSocketCommunicator("127.0.0.1", actualPort);

            // Start a simple server that accepts connection
            var serverTask = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync();
                // Just wait a bit then close
                await Task.Delay(100);
            });

            // Capture console output to avoid test output interference
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);

            try
            {
                // Connect
                await communicator.ConnectAsync();
                Assert.True(communicator.IsConnected);

                // Act
                communicator.Dispose();

                // Assert
                Assert.False(communicator.IsConnected);

                await serverTask;
                
                // Log the console output to test output
                var consoleOutput = sw.ToString();
                if (!string.IsNullOrEmpty(consoleOutput))
                {
                    _output.WriteLine($"Console output: {consoleOutput.Trim()}");
                }
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void InstrumentHost_Constructor_WithNullCommunicator_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new InstrumentHost(null!));
    }

    [Fact]
    public void InstrumentHost_Constructor_WithValidCommunicator_ShouldSucceed()
    {
        // Arrange
        using var communicator = new TcpSocketCommunicator("localhost", 1234);

        // Act & Assert - Should not throw
        var host = new InstrumentHost(communicator);
        Assert.NotNull(host);
    }

    [Theory]
    [InlineData("localhost", 1234)]
    [InlineData("127.0.0.1", 9999)]
    [InlineData("example.com", 80)]
    public void TcpSocketCommunicator_ConnectionDescription_ShouldFormatCorrectly(string host, int port)
    {
        // Arrange & Act
        using var communicator = new TcpSocketCommunicator(host, port);

        // Assert
        Assert.Equal($"TCP Socket: {host}:{port}", communicator.ConnectionDescription);
    }

    [Theory]
    [InlineData("TestPipe")]
    [InlineData("MyCustomPipe")]
    [InlineData("InstrumentProtocol")]
    public void NamedPipeCommunicator_ConnectionDescription_ShouldFormatCorrectly(string pipeName)
    {
        // Arrange & Act
        using var communicator = new NamedPipeCommunicator(pipeName);

        // Assert
        Assert.Equal($"Named Pipe: {pipeName}", communicator.ConnectionDescription);
    }
} 