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

    // NEW TESTS FOR FRAGMENTED DATA HANDLING

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
    public void ProcessReceivedData_WithFragmentedData_ShouldAccumulateAndProcess()
    {
        // Arrange
        var host = new InstrumentHost();
        var measurementCount = 0;
        var validMessageData = CreateValidMeasurementMessageData();
        
        // Capture console output to verify processing
        using var sw = new StringWriter();
        Console.SetOut(sw);
        
        // Act - Send data in small fragments (like UART byte-by-byte)
        for (int i = 0; i < validMessageData.Length; i++)
        {
            var singleByte = new byte[] { validMessageData[i] };
            host.ProcessReceivedData(singleByte, 1, ref measurementCount);
        }
        
        // Assert
        Assert.Equal(1, measurementCount);
        var output = sw.ToString();
        Assert.Contains("Measurement #1", output);
        Assert.Contains("samples: 3", output);
        
        // Restore console
        Console.SetOut(Console.Out);
    }

    [Fact]
    public void ProcessReceivedData_WithMultipleFragmentedMessages_ShouldProcessAll()
    {
        // Arrange
        var host = new InstrumentHost();
        var measurementCount = 0;
        var message1 = CreateValidMeasurementMessageData();
        var message2 = CreateValidMeasurementMessageData();
        
        // Combine both messages into one buffer
        var combinedData = new byte[message1.Length + message2.Length];
        Array.Copy(message1, 0, combinedData, 0, message1.Length);
        Array.Copy(message2, 0, combinedData, message1.Length, message2.Length);
        
        // Capture console output to verify processing
        using var sw = new StringWriter();
        Console.SetOut(sw);
        
        // Act - Send combined data in random-sized fragments
        var random = new Random(42); // Fixed seed for reproducible tests
        var offset = 0;
        while (offset < combinedData.Length)
        {
            var fragmentSize = Math.Min(random.Next(1, 10), combinedData.Length - offset);
            var fragment = new byte[fragmentSize];
            Array.Copy(combinedData, offset, fragment, 0, fragmentSize);
            host.ProcessReceivedData(fragment, fragmentSize, ref measurementCount);
            offset += fragmentSize;
        }
        
        // Assert
        Assert.Equal(2, measurementCount);
        var output = sw.ToString();
        Assert.Contains("Measurement #1", output);
        Assert.Contains("Measurement #2", output);
        
        // Restore console
        Console.SetOut(Console.Out);
    }

    [Fact]
    public void ProcessReceivedData_WithPartialMessage_ShouldWaitForComplete()
    {
        // Arrange
        var host = new InstrumentHost();
        var measurementCount = 0;
        var validMessageData = CreateValidMeasurementMessageData();
        
        // Capture console output to verify processing
        using var sw = new StringWriter();
        Console.SetOut(sw);
        
        // Act - Send only part of the message
        var partialData = new byte[validMessageData.Length / 2];
        Array.Copy(validMessageData, partialData, partialData.Length);
        host.ProcessReceivedData(partialData, partialData.Length, ref measurementCount);
        
        // Assert - Should not process yet
        Assert.Equal(0, measurementCount);
        
        // Act - Send the rest of the message
        var remainingData = new byte[validMessageData.Length - partialData.Length];
        Array.Copy(validMessageData, partialData.Length, remainingData, 0, remainingData.Length);
        host.ProcessReceivedData(remainingData, remainingData.Length, ref measurementCount);
        
        // Assert - Should now process the complete message
        Assert.Equal(1, measurementCount);
        var output = sw.ToString();
        Assert.Contains("Measurement #1", output);
        
        // Restore console
        Console.SetOut(Console.Out);
    }

    [Fact]
    public void ProcessReceivedData_WithCorruptedSizePrefix_ShouldHandleGracefully()
    {
        // Arrange
        var host = new InstrumentHost();
        var measurementCount = 0;
        
        // Create data with an extremely large size prefix (corrupted)
        var corruptedData = new byte[8];
        BitConverter.GetBytes(0xFFFFFFFF).CopyTo(corruptedData, 0); // Very large size prefix
        BitConverter.GetBytes(0x12345678).CopyTo(corruptedData, 4); // Some additional data
        
        using var sw = new StringWriter();
        Console.SetOut(sw);
        
        // Act
        host.ProcessReceivedData(corruptedData, corruptedData.Length, ref measurementCount);
        
        // Assert
        Assert.Equal(0, measurementCount);
        var output = sw.ToString();
        Assert.Contains("size prefix too large", output);
        
        Console.SetOut(Console.Out);
    }

    [Fact]
    public void ProcessReceivedData_WithInsufficientDataForSizePrefix_ShouldWaitForMore()
    {
        // Arrange
        var host = new InstrumentHost();
        var measurementCount = 0;
        var incompleteData = new byte[] { 0x01, 0x02, 0x03 }; // Only 3 bytes, need 4 for size prefix
        
        using var sw = new StringWriter();
        Console.SetOut(sw);
        
        // Act
        host.ProcessReceivedData(incompleteData, incompleteData.Length, ref measurementCount);
        
        // Assert - Should not process or fail, just wait for more data
        Assert.Equal(0, measurementCount);
        var output = sw.ToString();
        // Should not contain error messages since we're just waiting for more data
        Assert.DoesNotContain("Error", output);
        Assert.DoesNotContain("Invalid", output);
        
        Console.SetOut(Console.Out);
    }

    [Fact]
    public void ProcessReceivedData_WithValidSizePrefixButInsufficientData_ShouldWaitForComplete()
    {
        // Arrange
        var host = new InstrumentHost();
        var measurementCount = 0;
        var validMessageData = CreateValidMeasurementMessageData();
        
        // Create data with correct size prefix but insufficient message data
        var sizePrefix = BitConverter.GetBytes((uint)(validMessageData.Length - 4)); // Correct size prefix
        var partialMessage = new byte[10]; // Only 10 bytes of message data
        var incompleteData = new byte[sizePrefix.Length + partialMessage.Length];
        sizePrefix.CopyTo(incompleteData, 0);
        partialMessage.CopyTo(incompleteData, sizePrefix.Length);
        
        using var sw = new StringWriter();
        Console.SetOut(sw);
        
        // Act
        host.ProcessReceivedData(incompleteData, incompleteData.Length, ref measurementCount);
        
        // Assert - Should not process yet, waiting for complete message
        Assert.Equal(0, measurementCount);
        var output = sw.ToString();
        // Should not contain error messages since we're just waiting for more data
        Assert.DoesNotContain("Error", output);
        Assert.DoesNotContain("Invalid", output);
        
        Console.SetOut(Console.Out);
    }

    [Fact]
    public void ProcessReceivedData_WithInvalidFlatBufferStructure_ShouldLogInvalidData()
    {
        // Arrange
        var host = new InstrumentHost();
        var measurementCount = 0;
        
        // Create data with valid size prefix but invalid FlatBuffer structure
        var invalidMessageSize = 50;
        var sizePrefix = BitConverter.GetBytes((uint)invalidMessageSize);
        var invalidMessageData = new byte[invalidMessageSize];
        Array.Fill(invalidMessageData, (byte)0xFF); // Fill with invalid data
        
        var completeInvalidData = new byte[4 + invalidMessageSize];
        sizePrefix.CopyTo(completeInvalidData, 0);
        invalidMessageData.CopyTo(completeInvalidData, 4);
        
        using var sw = new StringWriter();
        Console.SetOut(sw);
        
        // Act
        host.ProcessReceivedData(completeInvalidData, completeInvalidData.Length, ref measurementCount);
        
        // Assert
        Assert.Equal(0, measurementCount);
        var output = sw.ToString();
        Assert.Contains("Received invalid FlatBuffer data", output);
        
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
    public void ProcessReceivedData_WithBufferSizeMismatch_ShouldHandleCorrectly()
    {
        // This test specifically covers the scenario where we pass a larger buffer
        // but only have valid data in the first part
        
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
        Assert.DoesNotContain("Error", output);
        
        Console.SetOut(Console.Out);
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