# Medical Instrument Protocol FlatBuffers

This repository contains the FlatBuffers schema definition for a communication protocol between embedded medical instruments and their host applications.

## Protocol Overview

The protocol defines a lightweight, efficient messaging system using Google's FlatBuffers serialization library. It supports three core message types for instrument control and data exchange:

### Message Types

1. **Command Messages** - Control instrument operation with Start/Stop commands
2. **Configuration Messages** - Set instrument parameters (sampling rate, samples per measurement)  
3. **Measurement Messages** - Transmit variable-length measurement data arrays

## Usage

### Generating Language Bindings

Use the FlatBuffers compiler to generate language-specific bindings:

```bash
# For C
flatcc --cpp instrument_protocol.fbs

# For C#
flatc --csharp instrument_protocol.fbs
```

## Protocol Versioning

The protocol includes a version field (currently v1) to ensure compatibility between instrument firmware and host applications. Future versions should maintain backward compatibility or provide appropriate migration paths. 