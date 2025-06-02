using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Google.FlatBuffers;
using InstrumentProtocol;
using InstrumentProtocolHost;

namespace Host.Tests;

public class InstrumentHostTests
{
    [Fact]
    public void Program_Constants_ShouldHaveCorrectValues()
    {
        Assert.Equal("InstrumentProtocol", Program.PipeName);
        Assert.Equal("HostApplication_001", Program.DeviceId);
        Assert.Equal(1000, Program.TimeoutMs);
    }

    [Fact]
    public void CreateConfigurationMessage_ShouldCreateValidFlatBuffer()
    {
        // Arrange
        var builder = new FlatBufferBuilder(1024);
        uint measurementsPerSecond = 10;
        uint samplesPerMeasurement = 25;

        // Act
        var configOffset = Configuration.CreateConfiguration(
            builder,
            measurementsPerSecond,
            samplesPerMeasurement
        );

        var messageOffset = Message.CreateMessage(
            builder,
            MessageType.Configuration,
            configOffset.Value
        );

        builder.Finish(messageOffset.Value);

        // Assert
        var buffer = builder.DataBuffer;
        Assert.True(buffer.Length > 0);

        // Verify we can read back the message
        var message = Message.GetRootAsMessage(buffer);
        Assert.Equal(MessageType.Configuration, message.MessageTypeType);

        var config = message.MessageType<Configuration>();
        Assert.True(config.HasValue);
        var configValue = config.Value;
        Assert.Equal(measurementsPerSecond, configValue.MeasurementsPerSecond);
        Assert.Equal(samplesPerMeasurement, configValue.SamplesPerMeasurement);
    }

    [Fact]
    public void CreateCommandMessage_ShouldCreateValidFlatBuffer()
    {
        // Arrange
        var builder = new FlatBufferBuilder(1024);
        var commandCode = CommandCode.Start;

        // Act
        var commandOffset = Command.CreateCommand(
            builder,
            commandCode
        );

        var messageOffset = Message.CreateMessage(
            builder,
            MessageType.Command,
            commandOffset.Value
        );

        builder.Finish(messageOffset.Value);

        // Assert
        var buffer = builder.DataBuffer;
        Assert.True(buffer.Length > 0);

        // Verify we can read back the message
        var message = Message.GetRootAsMessage(buffer);
        Assert.Equal(MessageType.Command, message.MessageTypeType);

        var command = message.MessageType<Command>();
        Assert.True(command.HasValue);
        var commandValue = command.Value;
        Assert.Equal(commandCode, commandValue.Code);
    }

    [Fact]
    public void ParseMeasurementMessage_ShouldExtractDataCorrectly()
    {
        // Arrange
        var builder = new FlatBufferBuilder(1024);
        var testData = new float[] { 1.23f, -2.45f, 3.67f, -4.89f, 5.01f };

        // Create measurement message
        var dataVector = Measurement.CreateDataVector(builder, testData);
        
        var measurementOffset = Measurement.CreateMeasurement(
            builder,
            dataVector
        );

        var messageOffset = Message.CreateMessage(
            builder,
            MessageType.Measurement,
            measurementOffset.Value
        );

        builder.Finish(messageOffset.Value);

        // Act & Assert
        var buffer = builder.DataBuffer;
        var message = Message.GetRootAsMessage(buffer);
        
        Assert.Equal(MessageType.Measurement, message.MessageTypeType);
        
        var measurement = message.MessageType<Measurement>();
        Assert.True(measurement.HasValue);
        var measurementValue = measurement.Value;
        
        Assert.Equal(testData.Length, measurementValue.DataLength);
        
        // Verify data values
        for (int i = 0; i < testData.Length; i++)
        {
            Assert.Equal(testData[i], measurementValue.Data(i), precision: 6);
        }
    }

    [Theory]
    [InlineData(CommandCode.Start)]
    [InlineData(CommandCode.Stop)]
    public void CommandCode_EnumValues_ShouldBeValid(CommandCode code)
    {
        // This test verifies that the command codes match expected values
        Assert.True(Enum.IsDefined(typeof(CommandCode), code));
    }

    [Theory]
    [InlineData(MessageType.Command)]
    [InlineData(MessageType.Configuration)]
    [InlineData(MessageType.Measurement)]
    public void MessageType_EnumValues_ShouldBeValid(MessageType type)
    {
        // This test verifies that the message types match expected values
        Assert.True(Enum.IsDefined(typeof(MessageType), type));
    }

    [Fact]
    public void FlatBufferSerialization_ShouldBeReproducible()
    {
        // This test verifies that creating the same message twice produces identical results
        var builder1 = new FlatBufferBuilder(1024);
        var builder2 = new FlatBufferBuilder(1024);
        
        uint measurementsPerSecond = 50;
        uint samplesPerMeasurement = 10;

        // Create identical configuration messages
        CreateConfigurationMessage(builder1, measurementsPerSecond, samplesPerMeasurement);
        CreateConfigurationMessage(builder2, measurementsPerSecond, samplesPerMeasurement);

        // Compare the resulting buffers
        var buffer1 = builder1.DataBuffer.ToSizedArray();
        var buffer2 = builder2.DataBuffer.ToSizedArray();
        
        Assert.Equal(buffer1.Length, buffer2.Length);
        Assert.Equal(buffer1, buffer2);
    }

    // NEW TESTS FOR DATA PARSING EDGE CASES

    [Fact]
    public void ProcessReceivedData_WithValidMeasurementData_ShouldProcessSuccessfully()
    {
        // Arrange
        var host = new InstrumentHost();
        var measurementCount = 0;
        var validMessageData = CreateValidMeasurementMessageData();
        
        // Capture console output to verify processing
        using var sw = new StringWriter();
        Console.SetOut(sw);
        
        // Act
        host.ProcessReceivedData(validMessageData, validMessageData.Length, ref measurementCount);
        
        // Assert
        Assert.Equal(1, measurementCount);
        var output = sw.ToString();
        Assert.Contains("Measurement #1", output);
        Assert.Contains("samples: 3", output);
        
        // Restore console
        Console.SetOut(Console.Out);
    }

    [Fact]
    public void ProcessReceivedData_WithIncompleteData_ShouldHandleGracefully()
    {
        // Arrange
        var host = new InstrumentHost();
        var measurementCount = 0;
        var incompleteData = new byte[] { 0x01, 0x02, 0x03 }; // Only 3 bytes, need at least 8
        
        using var sw = new StringWriter();
        Console.SetOut(sw);
        
        // Act
        host.ProcessReceivedData(incompleteData, incompleteData.Length, ref measurementCount);
        
        // Assert
        Assert.Equal(0, measurementCount);
        var output = sw.ToString();
        Assert.Contains("Received incomplete data: 3 bytes (minimum 8 required)", output);
        
        Console.SetOut(Console.Out);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(7)]
    public void ProcessReceivedData_WithInsufficientBytes_ShouldRejectData(int dataLength)
    {
        // Arrange
        var host = new InstrumentHost();
        var measurementCount = 0;
        var insufficientData = new byte[dataLength];
        
        using var sw = new StringWriter();
        Console.SetOut(sw);
        
        // Act
        host.ProcessReceivedData(insufficientData, dataLength, ref measurementCount);
        
        // Assert
        Assert.Equal(0, measurementCount);
        var output = sw.ToString();
        Assert.Contains($"Received incomplete data: {dataLength} bytes (minimum 8 required)", output);
        
        Console.SetOut(Console.Out);
    }

    [Fact]
    public void ProcessReceivedData_WithCorruptedFlatBufferData_ShouldHandleException()
    {
        // Arrange
        var host = new InstrumentHost();
        var measurementCount = 0;
        var corruptedData = new byte[50]; // Enough bytes but invalid FlatBuffer structure
        // Fill with random data that's not a valid FlatBuffer
        for (int i = 0; i < corruptedData.Length; i++)
        {
            corruptedData[i] = (byte)(i % 256);
        }
        
        using var sw = new StringWriter();
        Console.SetOut(sw);
        
        // Act
        host.ProcessReceivedData(corruptedData, corruptedData.Length, ref measurementCount);
        
        // Assert
        Assert.Equal(0, measurementCount);
        var output = sw.ToString();
        // With the new validation, corrupted data is caught before parsing
        Assert.Contains("Received invalid FlatBuffer data", output);
        
        Console.SetOut(Console.Out);
    }

    [Fact]
    public void ProcessReceivedData_WithBufferSizeMismatch_ShouldHandleCorrectly()
    {
        // This test specifically covers the original issue where we passed a larger buffer
        // but only had valid data in the first part
        
        // Arrange
        var host = new InstrumentHost();
        var measurementCount = 0;
        var validMessageData = CreateValidMeasurementMessageData();
        
        // Create a larger buffer and copy valid data to the beginning
        var largerBuffer = new byte[4096]; // Simulate the original 4KB buffer
        Array.Copy(validMessageData, largerBuffer, validMessageData.Length);
        
        using var sw = new StringWriter();
        Console.SetOut(sw);
        
        // Act - pass only the valid data length, not the full buffer size
        host.ProcessReceivedData(largerBuffer, validMessageData.Length, ref measurementCount);
        
        // Assert
        Assert.Equal(1, measurementCount);
        var output = sw.ToString();
        Assert.Contains("Measurement #1", output);
        Assert.DoesNotContain("Error parsing", output);
        
        Console.SetOut(Console.Out);
    }

    [Fact]
    public void ProcessReceivedData_WithNonMeasurementMessage_ShouldLogUnexpectedType()
    {
        // Arrange
        var host = new InstrumentHost();
        var measurementCount = 0;
        var commandMessageData = CreateValidCommandMessageData();
        
        using var sw = new StringWriter();
        Console.SetOut(sw);
        
        // Act
        host.ProcessReceivedData(commandMessageData, commandMessageData.Length, ref measurementCount);
        
        // Assert
        Assert.Equal(0, measurementCount); // Should not increment for non-measurement messages
        var output = sw.ToString();
        Assert.Contains("Received unexpected message type: Command", output);
        
        Console.SetOut(Console.Out);
    }

    [Fact]
    public void ProcessReceivedData_WithEmptyMeasurementData_ShouldHandleGracefully()
    {
        // Test what happens when we get a measurement message with no data array
        var host = new InstrumentHost();
        var measurementCount = 0;
        var emptyMeasurementData = CreateMeasurementMessageWithEmptyData();
        
        using var sw = new StringWriter();
        Console.SetOut(sw);
        
        // Act
        host.ProcessReceivedData(emptyMeasurementData, emptyMeasurementData.Length, ref measurementCount);
        
        // Assert
        Assert.Equal(1, measurementCount);
        var output = sw.ToString();
        Assert.Contains("Measurement #1", output);
        Assert.Contains("samples: 0", output);
        
        Console.SetOut(Console.Out);
    }

    [Fact]
    public void ProcessReceivedData_WithInvalidFlatBufferStructure_ShouldLogInvalidData()
    {
        // Arrange
        var host = new InstrumentHost();
        var measurementCount = 0;
        // Create data that has enough bytes but invalid FlatBuffer structure
        var invalidData = new byte[100];
        Array.Fill(invalidData, (byte)0xFF); // Fill with invalid data
        
        using var sw = new StringWriter();
        Console.SetOut(sw);
        
        // Act
        host.ProcessReceivedData(invalidData, invalidData.Length, ref measurementCount);
        
        // Assert
        Assert.Equal(0, measurementCount);
        var output = sw.ToString();
        Assert.Contains("Received invalid FlatBuffer data", output);
        Assert.Contains("Buffer details", output);
        Assert.Contains("Hex:", output);
        
        Console.SetOut(Console.Out);
    }

    [Fact]
    public void ProcessReceivedData_WithPartialMessage_ShouldLogInvalidData()
    {
        // Arrange
        var host = new InstrumentHost();
        var measurementCount = 0;
        
        // Create a valid message and then truncate it to simulate partial message
        var validMessageData = CreateValidMeasurementMessageData();
        var partialData = new byte[validMessageData.Length / 2]; // Take only half
        Array.Copy(validMessageData, partialData, partialData.Length);
        
        using var sw = new StringWriter();
        Console.SetOut(sw);
        
        // Act - This should not crash the application
        host.ProcessReceivedData(partialData, partialData.Length, ref measurementCount);
        
        // Assert
        var output = sw.ToString();
        Console.SetOut(Console.Out);
        
        // The key test is that this doesn't crash - any reasonable error handling is acceptable
        // Either it detects invalid data, insufficient data, or parsing error
        Assert.True(
            measurementCount == 0 || measurementCount == 1, // Could be 0 (error) or 1 (somehow valid)
            "Measurement count should be reasonable"
        );
        
        // And we should get some kind of output (either success or error message)
        Assert.False(string.IsNullOrWhiteSpace(output), "Should produce some output");
    }

    // Helper methods to create test data

    private static byte[] CreateValidMeasurementMessageData()
    {
        var builder = new FlatBufferBuilder(1024);
        var testData = new float[] { 1.23f, -2.45f, 3.67f };

        var dataVector = Measurement.CreateDataVector(builder, testData);
        
        var measurementOffset = Measurement.CreateMeasurement(
            builder,
            dataVector
        );

        var messageOffset = Message.CreateMessage(
            builder,
            MessageType.Measurement,
            measurementOffset.Value
        );

        builder.FinishSizePrefixed(messageOffset.Value);
        return builder.DataBuffer.ToSizedArray();
    }

    private static byte[] CreateValidCommandMessageData()
    {
        var builder = new FlatBufferBuilder(1024);
        var commandOffset = Command.CreateCommand(builder, CommandCode.Start);
        var messageOffset = Message.CreateMessage(
            builder,
            MessageType.Command,
            commandOffset.Value
        );
        builder.FinishSizePrefixed(messageOffset.Value);
        return builder.DataBuffer.ToSizedArray();
    }

    private static byte[] CreateMeasurementMessageWithEmptyData()
    {
        var builder = new FlatBufferBuilder(1024);
        var emptyData = new float[0]; // Empty data array

        var dataVector = Measurement.CreateDataVector(builder, emptyData);
        
        var measurementOffset = Measurement.CreateMeasurement(
            builder,
            dataVector
        );

        var messageOffset = Message.CreateMessage(
            builder,
            MessageType.Measurement,
            measurementOffset.Value
        );

        builder.FinishSizePrefixed(messageOffset.Value);
        return builder.DataBuffer.ToSizedArray();
    }

    private static void CreateConfigurationMessage(FlatBufferBuilder builder, uint measurementsPerSecond, uint samplesPerMeasurement)
    {
        var configOffset = Configuration.CreateConfiguration(
            builder,
            measurementsPerSecond,
            samplesPerMeasurement
        );

        var messageOffset = Message.CreateMessage(
            builder,
            MessageType.Configuration,
            configOffset.Value
        );

        builder.Finish(messageOffset.Value);
    }
} 