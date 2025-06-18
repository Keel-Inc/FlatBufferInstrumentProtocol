//! @file cff.h
//! @brief Compact Frame Format (CFF) library header
//! @author Richard Keelan
//! @date 2025
//! @copyright MIT License
//!
//! This library provides functionality for building and parsing frames in the Compact Frame Format (CFF).
//! CFF is a simple binary protocol that provides reliable frame transmission with CRC validation.

// MIT License

// Copyright (c) 2025 Richard Keelan

// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:

// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.

// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

#ifndef _CFF_H_
#define _CFF_H_

#ifdef __cplusplus
extern "C" {
#endif

#include <stddef.h>
#include <stdint.h>

//! @defgroup cff_constants CFF Constants
//! @brief Constants used in the Compact Frame Format C implementation
//! @{

//! @brief First byte of the frame preamble
#define CFF_PREAMBLE_BYTE_0 0xFA

//! @brief Second byte of the frame preamble
#define CFF_PREAMBLE_BYTE_1 0xCE

//! @brief Size of the preamble in bytes
#define CFF_PREAMBLE_SIZE_BYTES 2

//! @brief Size of the frame counter field in bytes
#define CFF_FRAME_COUNTER_SIZE_BYTES 2

//! @brief Size of the payload size field in bytes
#define CFF_PAYLOAD_SIZE_BYTES 2

//! @brief Size of the header CRC field in bytes
#define CFF_HEADER_CRC_SIZE_BYTES 2

//! @brief Size of the payload CRC field in bytes
#define CFF_PAYLOAD_CRC_SIZE_BYTES 2

//! @brief Total size of the frame header in bytes
#define CFF_HEADER_SIZE_BYTES                                                                                          \
    (CFF_PREAMBLE_SIZE_BYTES + CFF_FRAME_COUNTER_SIZE_BYTES + CFF_PAYLOAD_SIZE_BYTES + CFF_HEADER_CRC_SIZE_BYTES)

//! @brief Minimum possible frame size in bytes (header + payload CRC, no payload)
#define CFF_MIN_FRAME_SIZE_BYTES (CFF_HEADER_SIZE_BYTES + CFF_PAYLOAD_CRC_SIZE_BYTES)

//! @brief Maximum allowed payload size in bytes
#define CFF_MAX_PAYLOAD_SIZE_BYTES 65535

//! @brief CRC16 polynomial used for checksum calculation
#define CFF_CRC_POLYNOMIAL 0x1021

//! @brief Initial value for CRC16 calculation
#define CFF_CRC_INIT 0xFFFF

//! @}

//! @defgroup cff_enums CFF Enumerations
//! @brief Enumerations used in the Compact Frame Format C implementation
//! @{

//! @brief Error codes returned by CFF functions
typedef enum cff_error_en_t {
    cff_error_none = 0,            //!< No error occurred
    cff_error_null_pointer,        //!< Null pointer passed to function
    cff_error_invalid_preamble,    //!< Frame preamble is invalid
    cff_error_invalid_header_crc,  //!< Header CRC validation failed
    cff_error_invalid_payload_crc, //!< Payload CRC validation failed
    cff_error_buffer_too_small,    //!< Provided buffer is too small
    cff_error_payload_too_large,   //!< Payload exceeds maximum allowed size
    cff_error_incomplete_frame,    //!< Frame data is incomplete
} cff_error_en_t;

//! @}

//! @defgroup cff_structs CFF Structures
//! @brief Structures used in the Compact Frame Format
//! @{

//! @brief CFF frame header structure
//!
//! Contains all header fields of a CFF frame including preamble,
//! frame counter, payload size, and header CRC.
typedef struct cff_header_t {
    uint8_t preamble[CFF_PREAMBLE_SIZE_BYTES]; //!< Frame preamble bytes
    uint16_t frame_counter;                    //!< Incremental frame counter
    uint16_t payload_size_bytes;               //!< Size of payload in bytes
    uint16_t header_crc;                       //!< CRC16 checksum of header fields (preamble, frame counter,
                                               //!< payload size)
} cff_header_t;

//! @brief Complete CFF frame structure
//!
//! Represents a complete CFF frame with header, payload, and payload CRC.
//! The payload field points to the actual payload data.
typedef struct cff_frame_t {
    cff_header_t header;             //!< Frame header
    const uint8_t *payload;          //!< Pointer to payload data
    uint16_t payload_crc;            //!< CRC16 checksum of payload
    size_t payload_size_bytes_bytes; //!< Size of payload in bytes
} cff_frame_t;

//! @brief Frame builder structure for constructing CFF frames
//!
//! Used to build CFF frames into a provided buffer. Maintains state in the form of the current frame counter.
typedef struct cff_frame_builder_t {
    uint8_t *buffer;          //!< Buffer for frame construction
    size_t buffer_size_bytes; //!< Size of the buffer in bytes
    uint16_t frame_counter;   //!< Current frame counter value
} cff_frame_builder_t;

//! @brief Callback function type for frame processing
//!
//! @param frame Pointer to the parsed frame structure
typedef void (*cff_callback_t)(const cff_frame_t *frame);

//! @}

//! @defgroup cff_api CFF API Functions
//! @brief Public API functions for the Compact Frame Format C implementation
//! @{

//! @brief Calculate CRC16 checksum for given data
//!
//! Calculates a CRC16 checksum using the CRC polynomial and initial value
//! defined by the CFF specification.
//!
//! @param data Pointer to data buffer
//! @param data_size_bytes Length of data in bytes
//! @param crc Pointer to store calculated CRC value
//! @return cff_error_none on success, error code on failure
cff_error_en_t cff_crc16(const uint8_t *data, size_t data_size_bytes, uint16_t *crc);

//! @brief Initialize a frame builder
//!
//! Initializes a frame builder with the provided buffer and sets the frame counter to zero.
//!
//! @param builder Pointer to frame builder structure
//! @param buffer Pointer to buffer for frame construction
//! @param buffer_size_bytes Size of the buffer in bytes
//! @return cff_error_none on success, error code on failure
cff_error_en_t cff_frame_builder_init(cff_frame_builder_t *builder, uint8_t *buffer, size_t buffer_size_bytes);

//! @brief Build a CFF frame with the given payload
//!
//! Constructs a complete CFF frame in the builder's buffer, including header, payload, and all required CRC checksums.
//! Automatically increments the frame counter.
//!
//! @param builder Pointer to initialized frame builder
//! @param payload Pointer to payload data
//! @param payload_size_bytes Size of the payload in bytes
//! @return cff_error_none on success, error code on failure
cff_error_en_t cff_build_frame(cff_frame_builder_t *builder, const uint8_t *payload, size_t payload_size_bytes);

//! @brief Parse a single CFF frame from buffer
//!
//! Attempts to parse a complete CFF frame from the provided buffer.
//! Validates preamble, header CRC, and payload CRC.
//! Returns the number of bytes consumed from the buffer.
//!
//! @param buffer Pointer to buffer containing frame data
//! @param buffer_size_bytes Size of buffer in bytes
//! @param frame Pointer to frame structure to fill
//! @param consumed_bytes Pointer to store number of bytes consumed
//! @return cff_error_none on success, error code on failure
cff_error_en_t cff_parse_frame(const uint8_t *buffer, size_t buffer_size_bytes, cff_frame_t *frame,
                               size_t *consumed_bytes);

//! @brief Parse multiple CFF frames from buffer
//!
//! Continuously parses CFF frames from the buffer, calling the provided callback function for each successfully parsed
//! frame.
//!
//! @param buffer Pointer to buffer containing frame data
//! @param buffer_size_bytes Size of buffer in bytes
//! @param callback Callback function to call for each parsed frame
//! @return Number of bytes consumed from buffer
size_t cff_parse_frames(const uint8_t *buffer, size_t buffer_size_bytes, cff_callback_t callback);

//! @}

//! @defgroup cff_inline CFF Inline Functions
//! @brief Inline utility functions for the Compact Frame Format library
//! @{

//! @brief Calculate total frame size for given payload size
//!
//! Calculates the total size of a CFF frame including header,
//! payload, and payload CRC.
//!
//! @param payload_size_bytes_bytes Size of payload in bytes
//! @return Total frame size in bytes
static inline size_t cff_calculate_frame_size_bytes(size_t payload_size_bytes_bytes)
{
    return CFF_HEADER_SIZE_BYTES + payload_size_bytes_bytes + CFF_PAYLOAD_CRC_SIZE_BYTES;
}

//! @brief Get the total size of a parsed frame
//!
//! Returns the total size of the given frame structure including
//! header, payload, and payload CRC.
//!
//! @param frame Pointer to frame structure
//! @return Total frame size in bytes, or 0 if frame is NULL
static inline size_t cff_get_frame_size_bytes_bytes(const cff_frame_t *frame)
{
    if (!frame) {
        return 0;
    }
    return cff_calculate_frame_size_bytes(frame->payload_size_bytes_bytes);
}

//! @}

#ifdef __cplusplus
}
#endif

#endif // _CFF_H_