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
#include "cff.h"

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
    cff_frame_builder_t cff_builder;
    uint8_t cff_frame_buffer[BUFFER_SIZE];
    cff_ring_buffer_t cff_ring_buffer;
    uint8_t cff_receive_buffer[BUFFER_SIZE * 2];
} InstrumentState;

// Global instrument state
InstrumentState instrument_state = {0};

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
    bool success = WriteFile(state->pipe_handle, buffer, (DWORD)size, &bytes_written, NULL);
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
    
    // Create message as
    flatbuffers_buffer_ref_t buffer_ref = InstrumentProtocol_Message_create_as_root(
        &state->builder,
        message_type
    );
    
    if (!buffer_ref) {
        printf("Failed to create message buffer\n");
        return false;
    }
    
    // Get the finalized buffer
    void* flatbuffer_data = flatcc_builder_get_direct_buffer(&state->builder, NULL);
    size_t flatbuffer_size = flatcc_builder_get_buffer_size(&state->builder);
    
    // Build CFF frame around the FlatBuffer
    cff_error_en_t cff_result = cff_build_frame(&state->cff_builder, (const uint8_t*)flatbuffer_data, flatbuffer_size);
    
    if (cff_result != cff_error_none) {
        printf("Failed to build frame: %d\n", cff_result);
        return false;
    }
    
    // Calculate the frame size and send the frame
    size_t frame_size = cff_calculate_frame_size_bytes(flatbuffer_size);
    
    printf("Sending measurement with %u samples (frame, %zu bytes payload, %zu bytes total)\n", 
           sample_count, flatbuffer_size, frame_size);
    return send_message(state, state->cff_frame_buffer, frame_size);
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

// Callback function for processing CFF frames
void process_cff_frame(const cff_frame_t* frame) {
    printf("Received frame %u with %zu byte payload\n", 
           frame->header.frame_counter, frame->payload_size_bytes);
    
    // Parse the FlatBuffer message from the frame payload
    InstrumentProtocol_Message_table_t message = InstrumentProtocol_Message_as_root(frame->payload);
    if (!message) {
        printf("Error: Failed to parse FlatBuffer message from frame\n");
        return;
    }
    
    // Get message type
    InstrumentProtocol_MessageType_union_t message_type = InstrumentProtocol_Message_message_type_union(message);
    
    // Process based on message type
    switch (message_type.type) {
        case InstrumentProtocol_MessageType_Command:
            process_command(&instrument_state, (InstrumentProtocol_Command_table_t)message_type.value);
            break;
            
        case InstrumentProtocol_MessageType_Configuration:
            process_configuration(&instrument_state, (InstrumentProtocol_Configuration_table_t)message_type.value);
            break;
            
        case InstrumentProtocol_MessageType_Measurement:
            printf("Warning: Received unexpected measurement message\n");
            break;
            
        default:
            printf("Warning: Unknown message type: %u\n", message_type.type);
            break;
    }
}

// Process received data buffer containing frames
void process_received_data(void* buffer, size_t size) {
    printf("Received %zu bytes\n", size);
    
    // Append new data to ring buffer
    cff_error_en_t result = cff_ring_buffer_append(&instrument_state.cff_ring_buffer, (const uint8_t*)buffer, (uint32_t)size);
    if (result != cff_error_none) {
        printf("Warning: Failed to append to ring buffer: %d\n", result);
        return;
    }
    
    // Parse all complete frames
    size_t frames_parsed = cff_parse_frames(&instrument_state.cff_ring_buffer, process_cff_frame);
    
    if (frames_parsed > 0) {
        printf("Parsed %zu frames\n", frames_parsed);
    }
}

// Main measurement loop
void measurement_loop(void) {
    printf("Starting measurement loop...\n");
    
    // Calculate sleep interval in milliseconds
    DWORD sleep_ms = 1000 / instrument_state.measurements_per_second;
    if (sleep_ms == 0) sleep_ms = 1;
    
    while (instrument_state.running) {
        DWORD start_time = GetTickCount();
        
        // Send measurement
        if (!send_measurement(&instrument_state)) {
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
    
    // Initialize FlatBuffers builder
    flatcc_builder_init(&instrument_state.builder);
    
    // Initialize CFF frame builder
    cff_error_en_t cff_result = cff_frame_builder_init(&instrument_state.cff_builder, 
        instrument_state.cff_frame_buffer, sizeof(instrument_state.cff_frame_buffer));
    if (cff_result != cff_error_none) {
        printf("Error initializing CFF frame builder: %d\n", cff_result);
        return 1;
    }
    
    // Initialize CFF ring buffer
    cff_result = cff_ring_buffer_init(&instrument_state.cff_ring_buffer, 
        instrument_state.cff_receive_buffer, sizeof(instrument_state.cff_receive_buffer));
    if (cff_result != cff_error_none) {
        printf("Error initializing CFF ring buffer: %d\n", cff_result);
        return 1;
    }
    
    // Seed random number generator for simulated data
    srand((unsigned int)time(NULL));
    
    // Create named pipe
    printf("Creating named pipe: %s\n", PIPE_NAME);
    instrument_state.pipe_handle = CreateNamedPipeA(
        PIPE_NAME,
        PIPE_ACCESS_DUPLEX,
        PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
        1,                    // max instances
        BUFFER_SIZE,          // output buffer size
        BUFFER_SIZE,          // input buffer size
        0,                    // default timeout
        NULL                  // default security
    );
    
    if (instrument_state.pipe_handle == INVALID_HANDLE_VALUE) {
        printf("Error creating named pipe: %lu\n", GetLastError());
        return 1;
    }
    
    printf("Waiting for client connection...\n");
    
    // Wait for client to connect
    bool connected = ConnectNamedPipe(instrument_state.pipe_handle, NULL);
    if (!connected && GetLastError() != ERROR_PIPE_CONNECTED) {
        printf("Error connecting to client: %lu\n", GetLastError());
        CloseHandle(instrument_state.pipe_handle);
        return 1;
    }
    
    printf("Client connected! Ready to receive commands.\n");
    printf("Send a Configuration message followed by a Start command to begin measurements.\n");
    
    // Message processing loop
    char buffer[BUFFER_SIZE];
    while (true) {
        DWORD bytes_read;
        bool success = ReadFile(instrument_state.pipe_handle, buffer, BUFFER_SIZE, &bytes_read, NULL);
        
        if (!success || bytes_read == 0) {
            DWORD error = GetLastError();
            if (error == ERROR_BROKEN_PIPE) {
                printf("Client disconnected\n");
            } else {
                printf("Error reading from pipe: %lu\n", error);
            }
            break;
        }
        
        // Process the received data containing frames
        process_received_data(buffer, bytes_read);
        
        // If we received a start command and are configured, enter measurement loop
        if (instrument_state.running && instrument_state.configured) {
            measurement_loop();
        }
    }
    
    // Cleanup
    printf("Shutting down...\n");
    flatcc_builder_clear(&instrument_state.builder);
    CloseHandle(instrument_state.pipe_handle);
    
    return 0;
} 