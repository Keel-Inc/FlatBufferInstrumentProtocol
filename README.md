# Medical Instrument Communication Protocol With FlatBuffers

This repository contains the FlatBuffers schema definition for a communication protocol between an embedded medical instrument and its host application.

## Overview

The protocol defines three message types:

### Message Types

1. Command Messages: Control instrument operation with Start/Stop commands
2. Configuration Messages: Set instrument parameters (sampling rate, samples per measurement)  
3. Measurement Messages: Transmit variable-length measurement data

### Supported Transports

The Host application supports two communication transports:

- Named Pipes
- TCP Sockets

## Build

```powershell
# Build flacc for MSCV
cd flatcc\build\MSCV
cmake -G "Visual Studio 17 2022" ..\..
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" -p:Configuration=Release FlatCC.sln
cd ..\..\..

# Generate the C bindings
flatcc\bin\Release\flatcc.exe -a -o MsvcInstrument\InstrumentProtocol instrument_protocol.fbs

# Build the MSCV instrument application
cd MsvcInstrument
cmake -G "Visual Studio 17 2022" -B build
cmake --build build --config Release
cd ..

# Build flatbuffers for C#
cd flatbuffers
cmake -G "Visual Studio 17 2022" -DCMAKE_BUILD_TYPE=Release
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" -p:Configuration=Release .\FlatBuffers.sln
cd ..

# Generate the C# bindings, then 
flatbuffers\Release\flatc.exe --csharp -o Host instrument_protocol.fbs

# Build the host application
cd Host
dotnet restore
dotnet build --configuration Release
dotnet test
cd ..
```

## Usage

### Using Named Pipes

Start the instrument application:
```powershell
cd MsvcInstrument
./build/bin/instrument.exe
```

Then start the host in a separate terminal:
```powershell
cd Host
dotnet run
# or explicitly:
dotnet run --connection NamedPipe
```

### Using TCP Sockets

Start the host with TCP connection:
```powershell
cd Host
dotnet run --connection TcpSocket --host localhost --port 1234
```
