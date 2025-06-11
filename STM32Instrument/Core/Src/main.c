/* USER CODE BEGIN Header */
/**
  ******************************************************************************
  * @file           : main.c
  * @brief          : Main program body
  ******************************************************************************
  * @attention
  *
  * Copyright (c) 2025 STMicroelectronics.
  * All rights reserved.
  *
  * This software is licensed under terms that can be found in the LICENSE file
  * in the root directory of this software component.
  * If no LICENSE file comes with this software, it is provided AS-IS.
  *
  ******************************************************************************
  */
/* USER CODE END Header */
/* Includes ------------------------------------------------------------------*/
#include "main.h"

/* Private includes ----------------------------------------------------------*/
/* USER CODE BEGIN Includes */
#include <stdint.h>
#include <stdbool.h>
#include <string.h>
#include "instrument_protocol_reader.h"
#include "instrument_protocol_builder.h"
/* USER CODE END Includes */

/* Private typedef -----------------------------------------------------------*/
/* USER CODE BEGIN PTD */

/* USER CODE END PTD */

/* Private define ------------------------------------------------------------*/
/* USER CODE BEGIN PD */
#define DEVICE_ID "STM32Instrument_001"
#define MAX_SAMPLES 100
#define TX_BUFFER_SIZE 1024

// Custom allocator memory pool sizes - reduced for STM32F100 (8KB RAM)
#define VTABLE_POOL_SIZE 256
#define DATA_POOL_SIZE 1024
#define BUILDER_POOL_SIZE 128
#define HASH_POOL_SIZE 256
#define FRAME_POOL_SIZE 128
#define USER_POOL_SIZE 256
#define PATCH_POOL_SIZE 128
#define VECTOR_POOL_SIZE 256
/* USER CODE END PD */

/* Private macro -------------------------------------------------------------*/
/* USER CODE BEGIN PM */

/* USER CODE END PM */

/* Private variables ---------------------------------------------------------*/
UART_HandleTypeDef huart1;

/* USER CODE BEGIN PV */
#define RX_BUFFER_SIZE 1024
uint8_t rx_buffer[RX_BUFFER_SIZE];
uint16_t rx_index = 0;
bool message_size_received = false;
uint32_t expected_message_size = 0;

// Custom Memory Allocator - Static Memory Pools
static uint8_t vtable_pool[VTABLE_POOL_SIZE];
static uint8_t data_pool[DATA_POOL_SIZE]; 
static uint8_t builder_pool[BUILDER_POOL_SIZE];
static uint8_t hash_pool[HASH_POOL_SIZE];
static uint8_t frame_pool[FRAME_POOL_SIZE];
static uint8_t user_pool[USER_POOL_SIZE];
static uint8_t patch_pool[PATCH_POOL_SIZE];
static uint8_t vector_pool[VECTOR_POOL_SIZE];

// Pool management structure
typedef struct {
    uint8_t *pool;
    size_t size;
    size_t used;
    const char *name;
} memory_pool_t;

static memory_pool_t pools[] = {
    [flatcc_builder_alloc_vs] = {vtable_pool, VTABLE_POOL_SIZE, 0, "vtable_stack"},
    [flatcc_builder_alloc_ds] = {data_pool, DATA_POOL_SIZE, 0, "data_stack"},
    [flatcc_builder_alloc_vb] = {builder_pool, BUILDER_POOL_SIZE, 0, "vtable_buffer"},
    [flatcc_builder_alloc_pl] = {patch_pool, PATCH_POOL_SIZE, 0, "patch_log"},
    [flatcc_builder_alloc_fs] = {frame_pool, FRAME_POOL_SIZE, 0, "frame_stack"},
    [flatcc_builder_alloc_ht] = {hash_pool, HASH_POOL_SIZE, 0, "hash_table"},
    [flatcc_builder_alloc_vd] = {vector_pool, VECTOR_POOL_SIZE, 0, "vtable_desc"},
    [flatcc_builder_alloc_us] = {user_pool, USER_POOL_SIZE, 0, "user_stack"}
};

// Custom Emitter - Simple Linear Buffer
typedef struct {
    uint8_t *buffer;
    size_t capacity;
    size_t front_pos;  // Position for negative offsets (grows backwards)
    size_t back_pos;   // Position for positive offsets (grows forwards)
    size_t total_used;
} custom_emitter_t;

static custom_emitter_t custom_emit_context;

// Instrument state
typedef struct {
    uint32_t measurements_per_second;
    uint32_t samples_per_measurement;
    bool configured;
    bool running;
    uint32_t sequence_number;
    flatcc_builder_t builder;
    uint32_t last_measurement_time;
    uint32_t measurement_interval_ms;
} InstrumentState;

InstrumentState instrument_state = {0};
uint8_t tx_buffer[TX_BUFFER_SIZE];
/* USER CODE END PV */

/* Private function prototypes -----------------------------------------------*/
void SystemClock_Config(void);
static void MX_GPIO_Init(void);
static void MX_USART1_UART_Init(void);
/* USER CODE BEGIN PFP */
extern void initialise_monitor_handles(void);
int custom_allocator(void *alloc_context, flatcc_iovec_t *b, size_t request, int zero_fill, int alloc_type);
int custom_emitter(void *emit_context, const flatcc_iovec_t *iov, int iov_count, flatbuffers_soffset_t offset, size_t len);
void custom_emitter_init(custom_emitter_t *emitter, uint8_t *buffer, size_t capacity);
void custom_emitter_reset(custom_emitter_t *emitter);
void *custom_emitter_get_buffer(custom_emitter_t *emitter, size_t *size_out);
void print_memory_usage(void);
/* USER CODE END PFP */

/* Private user code ---------------------------------------------------------*/
/* USER CODE BEGIN 0 */

// Custom Memory Allocator Implementation
int custom_allocator(void *alloc_context, flatcc_iovec_t *b, size_t request, int zero_fill, int alloc_type) {
    (void)alloc_context; // Unused
    
    if (alloc_type >= FLATCC_BUILDER_ALLOC_BUFFER_COUNT) {
        printf("Error: Invalid alloc_type: %d\r\n", alloc_type);
        return -1;
    }
    
    memory_pool_t *pool = &pools[alloc_type];
    
    // Deallocate request
    if (request == 0) {
        if (b->iov_base) {
            pool->used = 0;
            b->iov_base = 0;
            b->iov_len = 0;
        }
        return 0;
    }
    
    // Ensure proper alignment (align to 8 bytes for safety)
    size_t aligned_request = (request + 7) & ~7;
    
    // Check if current allocation is sufficient
    if (b->iov_base && b->iov_len >= request) {
        // Current allocation is sufficient
        if (zero_fill && b->iov_len > request) {
            memset((uint8_t*)b->iov_base + request, 0, b->iov_len - request);
        }
        return 0;
    }
    
    // Need new allocation
    if (aligned_request > pool->size) {
        printf("Error: %s pool too small: need %u, have %u\r\n", 
               pool->name, aligned_request, pool->size);
        return -1;
    }
    
    // Simple allocation strategy: reset pool and allocate from start
    pool->used = aligned_request;
    b->iov_base = pool->pool;
    b->iov_len = aligned_request;
    
    if (zero_fill) {
        memset(b->iov_base, 0, b->iov_len);
    }
    
    return 0;
}

// Custom Emitter Implementation
void custom_emitter_init(custom_emitter_t *emitter, uint8_t *buffer, size_t capacity) {
    emitter->buffer = buffer;
    emitter->capacity = capacity;
    emitter->front_pos = capacity / 2;  // Start in middle for bidirectional growth
    emitter->back_pos = capacity / 2;
    emitter->total_used = 0;
}

void custom_emitter_reset(custom_emitter_t *emitter) {
    emitter->front_pos = emitter->capacity / 2;
    emitter->back_pos = emitter->capacity / 2;
    emitter->total_used = 0;
}

void *custom_emitter_get_buffer(custom_emitter_t *emitter, size_t *size_out) {
    if (emitter->total_used == 0) {
        if (size_out) *size_out = 0;
        return NULL;
    }
    
    size_t buffer_size = emitter->back_pos - emitter->front_pos;
    if (size_out) {
        *size_out = buffer_size;
    }
    
    return emitter->buffer + emitter->front_pos;
}

int custom_emitter(void *emit_context, const flatcc_iovec_t *iov, int iov_count, 
                   flatbuffers_soffset_t offset, size_t len) {
    custom_emitter_t *emitter = (custom_emitter_t*)emit_context;
    
    emitter->total_used += len;
    
    if (offset < 0) {
        // Negative offset: write towards front (backwards)
        if (emitter->front_pos < len) {
            printf("Error: Custom emitter front overflow\r\n");
            return -1;
        }
        
        emitter->front_pos -= len;
        uint8_t *write_pos = emitter->buffer + emitter->front_pos + len; // Start at end of allocated space
        
        // Copy data from iov vectors in reverse order, copying each vector's data backwards
        for (int i = iov_count - 1; i >= 0; i--) {
            write_pos -= iov[i].iov_len;
            memcpy(write_pos, iov[i].iov_base, iov[i].iov_len);
        }
    } else {
        // Positive offset: write towards back (forwards)  
        if (emitter->back_pos + len > emitter->capacity) {
            printf("Error: Custom emitter back overflow\r\n");
            return -1;
        }
        
        uint8_t *write_pos = emitter->buffer + emitter->back_pos;
        
        // Copy data from iov vectors
        for (int i = 0; i < iov_count; i++) {
            memcpy(write_pos, iov[i].iov_base, iov[i].iov_len);
            write_pos += iov[i].iov_len;
        }
        
        emitter->back_pos += len;
    }
    
    return 0;
}

// Generate simulated measurement data
void generate_measurement_data(float* data, uint32_t sample_count) {
    static float counter = 0.0f; // Persistent counter across function calls
    
    for (uint32_t i = 0; i < sample_count; i++) {
        counter += 0.1f;
        data[i] = counter;
    }
}

// Get current timestamp in milliseconds (STM32 version)
uint64_t get_timestamp_ms() {
    return HAL_GetTick();
}

// Send a message through UART
bool send_message(void* buffer, size_t size) {
    // Create size-prefixed buffer
    uint32_t total_size = sizeof(uint32_t) + size;
    if (total_size > TX_BUFFER_SIZE) {
        printf("Message with prefix too large (%lu bytes) for TX buffer\r\n", total_size);
        return false;
    }
    
    // Create a temporary buffer for size-prefixed message
    uint8_t prefixed_buffer[TX_BUFFER_SIZE];
    
    // Add size prefix (first 4 bytes)
    *(uint32_t*)prefixed_buffer = (uint32_t)size;
    
    // Copy the actual message data
    memcpy(prefixed_buffer + sizeof(uint32_t), buffer, size);
    
    // Send the complete size-prefixed message
    HAL_StatusTypeDef status = HAL_UART_Transmit(&huart1, prefixed_buffer, (uint16_t)total_size, 1000);
    if (status != HAL_OK) {
        printf("UART transmission failed\r\n");
        return false;
    }
    
    return true;
}

// Create and send a measurement message
bool send_measurement(InstrumentState* state) {
    // Generate measurement data
    float data[MAX_SAMPLES];
    uint32_t sample_count = state->samples_per_measurement;
    if (sample_count > MAX_SAMPLES) sample_count = MAX_SAMPLES;
    
    generate_measurement_data(data, sample_count);
    
    // Reset builder and custom emitter
    flatcc_builder_reset(&state->builder);
    custom_emitter_reset(&custom_emit_context);
    
    // Create data vector
    flatbuffers_float_vec_ref_t data_vec = flatbuffers_float_vec_create(&state->builder, data, sample_count);
    
    // Create measurement
    InstrumentProtocol_Measurement_ref_t measurement = InstrumentProtocol_Measurement_create(
        &state->builder,
        data_vec
    );
    
    // Create message type union
    InstrumentProtocol_MessageType_union_ref_t message_type = InstrumentProtocol_MessageType_as_Measurement(measurement);
    
    // Create message
    InstrumentProtocol_Message_create_as_root(
        &state->builder,
        message_type
    );
    
    // Get the buffer from custom emitter
    void* buffer;
    size_t size;
    buffer = custom_emitter_get_buffer(&custom_emit_context, &size);
    if (!buffer || size == 0) {
        printf("Failed to get buffer from custom emitter\r\n");
        return false;
    }
    
    // Copy to TX buffer
    if (size > TX_BUFFER_SIZE) {
        printf("Error: Message too large for TX buffer (%u > %d)\r\n", size, TX_BUFFER_SIZE);
        return false;
    }
    memcpy(tx_buffer, buffer, size);
    
    bool result = send_message(tx_buffer, size);
    
    static bool first_time = true;
    if (result && first_time) {
        first_time = false;
        print_memory_usage();
    }
    
    return result;
}

// Log received packet information (simplified for now)
void log_received_packet(void* buffer, size_t size) {
    // Check for minimum size-prefixed buffer size
    if (size < sizeof(uint32_t) + 4) { // 4 bytes for size prefix + minimum FlatBuffer
        printf("Error: Received buffer too small (%u bytes) for size-prefixed message\r\n", size);
        return;
    }
    
    // Read the size prefix (first 4 bytes)
    uint32_t message_size = *(uint32_t*)buffer;
    printf("Received size-prefixed message: %lu bytes payload, %u bytes total\r\n", message_size, size);
    
    // Validate size prefixf
    if (sizeof(uint32_t) + message_size > size) {
        printf("Error: Size prefix (%lu) exceeds available data (%u bytes total)\r\n", 
               message_size, size);
        return;
    }
    
    // Get the FlatBuffer data
    uint8_t* fb_data = (uint8_t*)buffer + sizeof(uint32_t);
    
    // Try to read as Message and determine type
    InstrumentProtocol_Message_table_t msg = InstrumentProtocol_Message_as_root(fb_data);
    if (msg) {
        InstrumentProtocol_MessageType_union_type_t msg_type = InstrumentProtocol_Message_message_type_type(msg);
        
        switch (msg_type) {
            case InstrumentProtocol_MessageType_Command:
                printf("Received Command message\r\n");
                break;
            case InstrumentProtocol_MessageType_Configuration:
                printf("Received Configuration message\r\n");
                break;
            case InstrumentProtocol_MessageType_Measurement:
                printf("Received Measurement message\r\n");
                break;
            default:
                printf("Received Unknown message type: %d\r\n", msg_type);
                break;
        }
    } else {
        printf("Failed to parse message as FlatBuffer\r\n");
    }
}

// Process configuration messages
void process_configuration(InstrumentState* state, InstrumentProtocol_Configuration_table_t config) {
    printf("Processing configuration...\r\n");
    
    state->measurements_per_second = InstrumentProtocol_Configuration_measurements_per_second(config);
    state->samples_per_measurement = InstrumentProtocol_Configuration_samples_per_measurement(config);
    
    // Validate configuration
    if (state->measurements_per_second == 0 || state->measurements_per_second > 1000) {
        printf("Invalid measurement rate: %lu (must be 1-1000)\r\n", state->measurements_per_second);
        return;
    }
    
    if (state->samples_per_measurement == 0 || state->samples_per_measurement > MAX_SAMPLES) {
        printf("Invalid sample count: %lu (must be 1-%d)\r\n", state->samples_per_measurement, MAX_SAMPLES);
        return;
    }
    
    state->configured = true;
    state->measurement_interval_ms = 1000 / state->measurements_per_second;
    
    printf("Configuration applied: %lu measurements/sec, %lu samples/measurement\r\n",
           state->measurements_per_second, state->samples_per_measurement);
}

// Process command messages
void process_command(InstrumentState* state, InstrumentProtocol_Command_table_t command) {
    InstrumentProtocol_CommandCode_enum_t command_type = InstrumentProtocol_Command_code(command);
    
    switch (command_type) {
        case InstrumentProtocol_CommandCode_Start:
            printf("Processing Start command\r\n");
            if (!state->configured) {
                printf("Error: Cannot start - instrument not configured\r\n");
                return;
            }
            state->running = true;
            state->last_measurement_time = get_timestamp_ms();
            printf("Instrument started\r\n");
            break;
            
        case InstrumentProtocol_CommandCode_Stop:
            printf("Processing Stop command\r\n");
            state->running = false;
            printf("Instrument stopped\r\n");
            break;
            
        default:
            printf("Unknown command type: %d\r\n", command_type);
            break;
    }
}

// Process a complete FlatBuffer message
void process_message(InstrumentState* state, void* buffer, size_t size) {
    (void)size; // Unused parameter
    
    // Skip size prefix and get FlatBuffer data
    uint8_t* fb_data = (uint8_t*)buffer + sizeof(uint32_t);
    
    InstrumentProtocol_Message_table_t msg = InstrumentProtocol_Message_as_root(fb_data);
    if (!msg) {
        printf("Failed to parse message\r\n");
        return;
    }
    
    InstrumentProtocol_MessageType_union_type_t msg_type = InstrumentProtocol_Message_message_type_type(msg);
    
    switch (msg_type) {
        case InstrumentProtocol_MessageType_Configuration: {
            InstrumentProtocol_Configuration_table_t config = 
                (InstrumentProtocol_Configuration_table_t)InstrumentProtocol_Message_message_type(msg);
            process_configuration(state, config);
            break;
        }
        
        case InstrumentProtocol_MessageType_Command: {
            InstrumentProtocol_Command_table_t command = 
                (InstrumentProtocol_Command_table_t)InstrumentProtocol_Message_message_type(msg);
            process_command(state, command);
            break;
        }
        
        case InstrumentProtocol_MessageType_Measurement:
            printf("Received measurement message (unexpected)\r\n");
            break;
            
        default:
            printf("Unknown message type: %d\r\n", msg_type);
            break;
    }
}

// Check if it's time to send a measurement
void check_measurement_timing(InstrumentState* state) {
    if (!state->running) {
        return;
    }
    
    uint64_t current_time = get_timestamp_ms();
    if (current_time - state->last_measurement_time >= state->measurement_interval_ms) {
        if (send_measurement(state)) {
            state->last_measurement_time = current_time;
        } else {
            printf("Failed to send measurement\r\n");
        }
    }
}

// Process received bytes to check for complete messages
void process_received_bytes(void) {
    // We need at least 4 bytes for the size prefix
    if (rx_index < sizeof(uint32_t)) {
        return;
    }
    
    if (!message_size_received) {
        // Read the expected message size from the first 4 bytes
        expected_message_size = *(uint32_t*)rx_buffer;
        message_size_received = true;
        
        // Sanity check on message size
        if (expected_message_size > (RX_BUFFER_SIZE - sizeof(uint32_t))) {
            printf("Error: Expected message size (%lu) too large for buffer\r\n", expected_message_size);
            rx_index = 0;
            message_size_received = false;
            expected_message_size = 0;
            return;
        }
    }
    
    // Check if we have received the complete message
    uint32_t total_expected_size = sizeof(uint32_t) + expected_message_size;
    if (rx_index >= total_expected_size) {
        // We have a complete message, process it
        log_received_packet(rx_buffer, total_expected_size);
        process_message(&instrument_state, rx_buffer, total_expected_size);
        
        // Reset for next message
        rx_index = 0;
        message_size_received = false;
        expected_message_size = 0;
    }
}

// Print memory usage statistics
void print_memory_usage(void) {
    size_t total_allocated = 0;
    printf("Memory pool usage:\r\n");
    for (int i = 0; i < FLATCC_BUILDER_ALLOC_BUFFER_COUNT; i++) {
        if (pools[i].used > 0) {
            printf("  %s: %u/%u bytes\r\n",
                   pools[i].name,
                   pools[i].used,
                   pools[i].size);
        }
        total_allocated += pools[i].used;
    }
    printf("Total pool usage: %u bytes\r\n", total_allocated);
}

/* USER CODE END 0 */

/**
  * @brief  The application entry point.
  * @retval int
  */
int main(void)
{

  /* USER CODE BEGIN 1 */

  /* USER CODE END 1 */

  /* MCU Configuration--------------------------------------------------------*/

  /* Reset of all peripherals, Initializes the Flash interface and the Systick. */
  HAL_Init();

  /* USER CODE BEGIN Init */

  /* USER CODE END Init */

  /* Configure the system clock */
  SystemClock_Config();

  /* USER CODE BEGIN SysInit */

  /* USER CODE END SysInit */

  /* Initialize all configured peripherals */
  MX_GPIO_Init();
  MX_USART1_UART_Init();
  /* USER CODE BEGIN 2 */
  initialise_monitor_handles();
  
  // Initialize custom emitter with TX buffer
  custom_emitter_init(&custom_emit_context, tx_buffer, TX_BUFFER_SIZE);
  
  // Initialize FlatBuffers builder with custom allocator and emitter
  int init_result = flatcc_builder_custom_init(&instrument_state.builder,
                                              custom_emitter, &custom_emit_context,
                                              custom_allocator, NULL);
  if (init_result != 0) {
    printf("Error: Failed to initialize FlatBuffers builder: %d\r\n", init_result);
    Error_Handler();
  }
  
  printf("STM32 Instrument v1.0 - FlatBuffer Protocol (Custom Allocator)\r\n");
  printf("Device ID: %s\r\n", DEVICE_ID);
  printf("Memory pools initialized:\r\n");
  printf("  - VTable Stack: %d bytes\r\n", VTABLE_POOL_SIZE);
  printf("  - Data Stack: %d bytes\r\n", DATA_POOL_SIZE);
  printf("  - Builder Pool: %d bytes\r\n", BUILDER_POOL_SIZE);
  printf("  - Hash Table: %d bytes\r\n", HASH_POOL_SIZE);
  printf("Ready to receive FlatBuffer messages...\r\n");
  printf("Send a Configuration message followed by a Start command to begin measurements.\r\n");
  /* USER CODE END 2 */

  /* Infinite loop */
  /* USER CODE BEGIN WHILE */
  while (1)
  {
    /* USER CODE END WHILE */

    /* USER CODE BEGIN 3 */
    // Check if it's time to send a measurement
    check_measurement_timing(&instrument_state);
    
    // Process incoming UART data
    uint8_t byte;
    if (HAL_UART_Receive(&huart1, &byte, 1, 1) == HAL_OK) {
      // Check for buffer overflow
      if (rx_index >= RX_BUFFER_SIZE) {
        printf("Error: RX buffer overflow, resetting\r\n");
        rx_index = 0;
        message_size_received = false;
        expected_message_size = 0;
        continue;
      }
      
      // Add byte to buffer
      rx_buffer[rx_index++] = byte;
      
      // Process received bytes to check for complete messages
      process_received_bytes();
    }
  }
  /* USER CODE END 3 */
}

/**
  * @brief System Clock Configuration
  * @retval None
  */
void SystemClock_Config(void)
{
  RCC_OscInitTypeDef RCC_OscInitStruct = {0};
  RCC_ClkInitTypeDef RCC_ClkInitStruct = {0};

  /** Initializes the RCC Oscillators according to the specified parameters
  * in the RCC_OscInitTypeDef structure.
  */
  RCC_OscInitStruct.OscillatorType = RCC_OSCILLATORTYPE_HSI;
  RCC_OscInitStruct.HSIState = RCC_HSI_ON;
  RCC_OscInitStruct.HSICalibrationValue = RCC_HSICALIBRATION_DEFAULT;
  RCC_OscInitStruct.PLL.PLLState = RCC_PLL_NONE;
  if (HAL_RCC_OscConfig(&RCC_OscInitStruct) != HAL_OK)
  {
    Error_Handler();
  }

  /** Initializes the CPU, AHB and APB buses clocks
  */
  RCC_ClkInitStruct.ClockType = RCC_CLOCKTYPE_HCLK|RCC_CLOCKTYPE_SYSCLK
                              |RCC_CLOCKTYPE_PCLK1|RCC_CLOCKTYPE_PCLK2;
  RCC_ClkInitStruct.SYSCLKSource = RCC_SYSCLKSOURCE_HSI;
  RCC_ClkInitStruct.AHBCLKDivider = RCC_SYSCLK_DIV1;
  RCC_ClkInitStruct.APB1CLKDivider = RCC_HCLK_DIV1;
  RCC_ClkInitStruct.APB2CLKDivider = RCC_HCLK_DIV1;

  if (HAL_RCC_ClockConfig(&RCC_ClkInitStruct, FLASH_LATENCY_0) != HAL_OK)
  {
    Error_Handler();
  }
}

/**
  * @brief USART1 Initialization Function
  * @param None
  * @retval None
  */
static void MX_USART1_UART_Init(void)
{

  /* USER CODE BEGIN USART1_Init 0 */

  /* USER CODE END USART1_Init 0 */

  /* USER CODE BEGIN USART1_Init 1 */

  /* USER CODE END USART1_Init 1 */
  huart1.Instance = USART1;
  huart1.Init.BaudRate = 115200;
  huart1.Init.WordLength = UART_WORDLENGTH_8B;
  huart1.Init.StopBits = UART_STOPBITS_1;
  huart1.Init.Parity = UART_PARITY_NONE;
  huart1.Init.Mode = UART_MODE_TX_RX;
  huart1.Init.HwFlowCtl = UART_HWCONTROL_NONE;
  huart1.Init.OverSampling = UART_OVERSAMPLING_16;
  if (HAL_UART_Init(&huart1) != HAL_OK)
  {
    Error_Handler();
  }
  /* USER CODE BEGIN USART1_Init 2 */

  /* USER CODE END USART1_Init 2 */

}

/**
  * @brief GPIO Initialization Function
  * @param None
  * @retval None
  */
static void MX_GPIO_Init(void)
{
  GPIO_InitTypeDef GPIO_InitStruct = {0};
  /* USER CODE BEGIN MX_GPIO_Init_1 */

  /* USER CODE END MX_GPIO_Init_1 */

  /* GPIO Ports Clock Enable */
  __HAL_RCC_GPIOC_CLK_ENABLE();
  __HAL_RCC_GPIOD_CLK_ENABLE();
  __HAL_RCC_GPIOA_CLK_ENABLE();

  /*Configure GPIO pin Output Level */
  HAL_GPIO_WritePin(GPIOC, LD4_Pin|LD3_Pin, GPIO_PIN_RESET);

  /*Configure GPIO pins : LD4_Pin LD3_Pin */
  GPIO_InitStruct.Pin = LD4_Pin|LD3_Pin;
  GPIO_InitStruct.Mode = GPIO_MODE_OUTPUT_PP;
  GPIO_InitStruct.Pull = GPIO_NOPULL;
  GPIO_InitStruct.Speed = GPIO_SPEED_FREQ_LOW;
  HAL_GPIO_Init(GPIOC, &GPIO_InitStruct);

  /* USER CODE BEGIN MX_GPIO_Init_2 */

  /* USER CODE END MX_GPIO_Init_2 */
}

/* USER CODE BEGIN 4 */

// Cleanup function for proper shutdown (though this may never be called in embedded systems)
void instrument_cleanup(void) {
    printf("Shutting down instrument...\r\n");
    instrument_state.running = false;
    flatcc_builder_clear(&instrument_state.builder);
}

/* USER CODE END 4 */

/**
  * @brief  This function is executed in case of error occurrence.
  * @retval None
  */
void Error_Handler(void)
{
  /* USER CODE BEGIN Error_Handler_Debug */
  return;
  /* User can add his own implementation to report the HAL error return state */
  __disable_irq();
  while (1)
  {
  }
  /* USER CODE END Error_Handler_Debug */
}

#ifdef  USE_FULL_ASSERT
/**
  * @brief  Reports the name of the source file and the source line number
  *         where the assert_param error has occurred.
  * @param  file: pointer to the source file name
  * @param  line: assert_param error line source number
  * @retval None
  */
void assert_failed(uint8_t *file, uint32_t line)
{
  /* USER CODE BEGIN 6 */
  /* User can add his own implementation to report the file name and line number,
     ex: printf("Wrong parameters value: file %s on line %d\r\n", file, line) */
  /* USER CODE END 6 */
}
#endif /* USE_FULL_ASSERT */
