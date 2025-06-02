#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <math.h>
#include <windows.h>
#include <stdint.h>
#include <stdbool.h>

#include "InstrumentProtocol/instrument_protocol_reader.h"
#include "InstrumentProtocol/instrument_protocol_builder.h"

#define PIPE_NAME "\\\\.\\pipe\\InstrumentProtocol"
#define BUFFER_SIZE 4096
#define DEVICE_ID "MedicalInstrument_001"
#define MAX_SAMPLES 100

typedef struct {
    uint32_t measurements_per_second;
    uint32_t samples_per_measurement;
    bool configured;
    bool running;
    uint32_t sequence_number;
    HANDLE pipe_handle;
    flatcc_builder_t builder;
} InstrumentState;

// Generate simulated measurement data
void generate_measurement_data(float* data, uint32_t sample_count) {
    static float counter = 0.0f; // Persistent counter across function calls
    
    for (uint32_t i = 0; i < sample_count; i++) {
        counter += 0.1f;
        data[i] = counter;
    }
}

// Get current timestamp in milliseconds
uint64_t get_timestamp_ms() {
    FILETIME ft;
    ULARGE_INTEGER ui;
    GetSystemTimeAsFileTime(&ft);
    ui.LowPart = ft.dwLowDateTime;
    ui.HighPart = ft.dwHighDateTime;
    // Convert from 100ns intervals since 1601 to milliseconds since 1970
    return (ui.QuadPart - 116444736000000000ULL) / 10000ULL;
}

// Send a message through the named pipe
bool send_message(InstrumentState* state, void* buffer, size_t size) {
    DWORD bytes_written;
    BOOL success = WriteFile(state->pipe_handle, buffer, (DWORD)size, &bytes_written, NULL);
    if (!success || bytes_written != size) {
        printf("Failed to send message: %lu\n", GetLastError());
        return false;
    }
    FlushFileBuffers(state->pipe_handle);
    return true;
}

// Create and send a measurement message
bool send_measurement(InstrumentState* state) {
    // Generate measurement data
    float data[MAX_SAMPLES];
    uint32_t sample_count = state->samples_per_measurement;
    if (sample_count > MAX_SAMPLES) sample_count = MAX_SAMPLES;
    
    generate_measurement_data(data, sample_count);
    
    // Reset builder
    flatcc_builder_reset(&state->builder);
    
    // Create data vector
    flatbuffers_float_vec_ref_t data_vec = flatbuffers_float_vec_create(&state->builder, data, sample_count);
    
    // Create measurement
    InstrumentProtocol_Measurement_ref_t measurement = InstrumentProtocol_Measurement_create(
        &state->builder,
        data_vec
    );
    
    // Create message type union
    InstrumentProtocol_MessageType_union_ref_t message_type = 
        InstrumentProtocol_MessageType_as_Measurement(measurement);
    
    // Create message as size-prefixed root
    flatbuffers_buffer_ref_t buffer_ref = InstrumentProtocol_Message_create_as_root_with_size(
        &state->builder,
        message_type
    );
    
    if (!buffer_ref) {
        printf("Failed to create size-prefixed message buffer\n");
        return false;
    }
    
    // Get the finalized buffer
    void* buffer = flatcc_builder_get_direct_buffer(&state->builder, NULL);
    size_t size = flatcc_builder_get_buffer_size(&state->builder);
    
    printf("Sending measurement with %u samples (size-prefixed, %zu bytes)\n", 
           sample_count, size);
    return send_message(state, buffer, size);
}

// Process received command message
void process_command(InstrumentState* state, InstrumentProtocol_Command_table_t command) {
    InstrumentProtocol_CommandCode_enum_t code = InstrumentProtocol_Command_code(command);
    
    printf("Received command: %s\n", InstrumentProtocol_CommandCode_name(code));
    
    switch (code) {
        case InstrumentProtocol_CommandCode_Start:
            if (state->configured) {
                state->running = true;
                state->sequence_number = 0;
                printf("Instrument started - sending measurements at %u Hz\n", 
                       state->measurements_per_second);
            } else {
                printf("Error: Cannot start - instrument not configured\n");
            }
            break;
            
        case InstrumentProtocol_CommandCode_Stop:
            state->running = false;
            printf("Instrument stopped\n");
            break;
            
        default:
            printf("Warning: Unknown command code: %d\n", code);
            break;
    }
}

// Process received configuration message
void process_configuration(InstrumentState* state, InstrumentProtocol_Configuration_table_t config) {
    uint32_t measurements_per_second = InstrumentProtocol_Configuration_measurements_per_second(config);
    uint32_t samples_per_measurement = InstrumentProtocol_Configuration_samples_per_measurement(config);
    
    printf("Received configuration:\n");
    printf("  Measurements per second: %u\n", measurements_per_second);
    printf("  Samples per measurement: %u\n", samples_per_measurement);
    
    // Validate configuration
    if (measurements_per_second == 0 || measurements_per_second > 1000) {
        printf("Error: Invalid measurement rate (must be 1-1000 Hz)\n");
        return;
    }
    
    if (samples_per_measurement == 0 || samples_per_measurement > MAX_SAMPLES) {
        printf("Error: Invalid sample count (must be 1-%d)\n", MAX_SAMPLES);
        return;
    }
    
    // Save configuration
    state->measurements_per_second = measurements_per_second;
    state->samples_per_measurement = samples_per_measurement;
    state->configured = true;
    
    printf("Configuration accepted\n");
}

// Process received message
void process_message(InstrumentState* state, void* buffer, size_t size) {
    // Check for minimum size-prefixed buffer size
    if (size < sizeof(uint32_t) + 4) { // 4 bytes for size prefix + minimum FlatBuffer
        printf("Error: Received buffer too small (%zu bytes) for size-prefixed message\n", size);
        return;
    }
    
    // Read the size prefix (first 4 bytes)
    uint32_t message_size = *(uint32_t*)buffer;
    printf("Received size-prefixed message: %u bytes payload, %zu bytes total\n", message_size, size);
    
    // Validate size prefix
    if (sizeof(uint32_t) + message_size > size) {
        printf("Error: Size prefix (%u) exceeds available data (%zu bytes total)\n", 
               message_size, size);
        return;
    }
    
    // Skip the size prefix to get to the actual FlatBuffer message
    void* message_buffer = (uint8_t*)buffer + sizeof(uint32_t);
    
    // Parse the message (now without the size prefix)
    InstrumentProtocol_Message_table_t message = InstrumentProtocol_Message_as_root(message_buffer);
    if (!message) {
        printf("Error: Failed to parse size-prefixed message\n");
        return;
    }
    
    // Get message type
    InstrumentProtocol_MessageType_union_t message_type = InstrumentProtocol_Message_message_type_union(message);
    
    // Process based on message type
    switch (message_type.type) {
        case InstrumentProtocol_MessageType_Command:
            process_command(state, (InstrumentProtocol_Command_table_t)message_type.value);
            break;
            
        case InstrumentProtocol_MessageType_Configuration:
            process_configuration(state, (InstrumentProtocol_Configuration_table_t)message_type.value);
            break;
            
        case InstrumentProtocol_MessageType_Measurement:
            printf("Warning: Received unexpected measurement message\n");
            break;
            
        default:
            printf("Warning: Unknown message type: %u\n", message_type.type);
            break;
    }
}

// Main measurement loop
void measurement_loop(InstrumentState* state) {
    printf("Starting measurement loop...\n");
    
    // Calculate sleep interval in milliseconds
    DWORD sleep_ms = 1000 / state->measurements_per_second;
    if (sleep_ms == 0) sleep_ms = 1;
    
    while (state->running) {
        DWORD start_time = GetTickCount();
        
        // Send measurement
        if (!send_measurement(state)) {
            printf("Failed to send measurement - client may have disconnected\n");
            break;
        }
        
        // Calculate actual sleep time to maintain frequency
        DWORD elapsed = GetTickCount() - start_time;
        if (elapsed < sleep_ms) {
            Sleep(sleep_ms - elapsed);
        }
    }
    
    printf("Measurement loop ended\n");
}

int main() {
    printf("Medical Instrument v1.0\n");
    printf("=============================\n");
    
    InstrumentState state = {0};
    
    // Initialize FlatBuffers builder
    flatcc_builder_init(&state.builder);
    
    // Seed random number generator for simulated data
    srand((unsigned int)time(NULL));
    
    // Create named pipe
    printf("Creating named pipe: %s\n", PIPE_NAME);
    state.pipe_handle = CreateNamedPipeA(
        PIPE_NAME,
        PIPE_ACCESS_DUPLEX,
        PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
        1,                    // max instances
        BUFFER_SIZE,          // output buffer size
        BUFFER_SIZE,          // input buffer size
        0,                    // default timeout
        NULL                  // default security
    );
    
    if (state.pipe_handle == INVALID_HANDLE_VALUE) {
        printf("Error creating named pipe: %lu\n", GetLastError());
        return 1;
    }
    
    printf("Waiting for client connection...\n");
    
    // Wait for client to connect
    BOOL connected = ConnectNamedPipe(state.pipe_handle, NULL);
    if (!connected && GetLastError() != ERROR_PIPE_CONNECTED) {
        printf("Error connecting to client: %lu\n", GetLastError());
        CloseHandle(state.pipe_handle);
        return 1;
    }
    
    printf("Client connected! Ready to receive commands.\n");
    printf("Send a Configuration message followed by a Start command to begin measurements.\n");
    
    // Message processing loop
    char buffer[BUFFER_SIZE];
    while (true) {
        DWORD bytes_read;
        BOOL success = ReadFile(state.pipe_handle, buffer, BUFFER_SIZE, &bytes_read, NULL);
        
        if (!success || bytes_read == 0) {
            DWORD error = GetLastError();
            if (error == ERROR_BROKEN_PIPE) {
                printf("Client disconnected\n");
            } else {
                printf("Error reading from pipe: %lu\n", error);
            }
            break;
        }
        
        // Process the received message
        process_message(&state, buffer, bytes_read);
        
        // If we received a start command and are configured, enter measurement loop
        if (state.running && state.configured) {
            measurement_loop(&state);
        }
    }
    
    // Cleanup
    printf("Shutting down...\n");
    flatcc_builder_clear(&state.builder);
    CloseHandle(state.pipe_handle);
    
    return 0;
} 