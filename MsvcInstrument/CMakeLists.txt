cmake_minimum_required(VERSION 3.15)
project(MedicalInstrument VERSION 1.0.0 LANGUAGES C)

# Set C standard
set(CMAKE_C_STANDARD 99)
set(CMAKE_C_STANDARD_REQUIRED ON)

# Enable debug information for all configurations
set(CMAKE_C_FLAGS_DEBUG "${CMAKE_C_FLAGS_DEBUG} /Zi")
set(CMAKE_C_FLAGS_RELEASE "${CMAKE_C_FLAGS_RELEASE} /Zi")
set(CMAKE_C_FLAGS_RELWITHDEBINFO "${CMAKE_C_FLAGS_RELWITHDEBINFO} /Zi")

# Windows-specific settings
if(WIN32)
    add_definitions(-D_CRT_SECURE_NO_WARNINGS)
    add_definitions(-DWIN32_LEAN_AND_MEAN)
endif()

# FlatCC submodule paths
set(FLATCC_ROOT_DIR ${CMAKE_CURRENT_SOURCE_DIR}/../flatcc)
set(FLATCC_INCLUDE_DIR ${FLATCC_ROOT_DIR}/include)
set(FLATCC_RUNTIME_DIR ${FLATCC_ROOT_DIR}/src/runtime)

# Include directories
include_directories(${CMAKE_CURRENT_SOURCE_DIR})
include_directories(${CMAKE_CURRENT_SOURCE_DIR}/InstrumentProtocol)
include_directories(${CMAKE_CURRENT_SOURCE_DIR}/cff)
include_directories(${FLATCC_INCLUDE_DIR})

# FlatBuffers generated headers
set(FLATBUFFERS_HEADERS
    InstrumentProtocol/instrument_protocol_reader.h
    InstrumentProtocol/instrument_protocol_builder.h
    InstrumentProtocol/instrument_protocol_verifier.h
    InstrumentProtocol/flatbuffers_common_builder.h
    InstrumentProtocol/flatbuffers_common_reader.h
)

# FlatCC runtime source files (needed for building FlatBuffers)
set(FLATCC_RUNTIME_SOURCES
    ${FLATCC_RUNTIME_DIR}/builder.c
    ${FLATCC_RUNTIME_DIR}/emitter.c
    ${FLATCC_RUNTIME_DIR}/verifier.c
    ${FLATCC_RUNTIME_DIR}/refmap.c
)

# CFF source files
set(CFF_SOURCES
    cff/cff.c
)

# Source files
set(INSTRUMENT_SOURCES
    instrument.c
    ${FLATCC_RUNTIME_SOURCES}
    ${CFF_SOURCES}
)

# Create the instrument executable
add_executable(instrument ${INSTRUMENT_SOURCES})

# Add the FlatBuffers headers as a dependency (so they show up in Visual Studio)
target_sources(instrument PRIVATE ${FLATBUFFERS_HEADERS})

# Set output directory
set_target_properties(instrument PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin
    RUNTIME_OUTPUT_DIRECTORY_DEBUG ${CMAKE_BINARY_DIR}/bin
    RUNTIME_OUTPUT_DIRECTORY_RELEASE ${CMAKE_BINARY_DIR}/bin
)

# Group source files in Visual Studio
source_group("Source Files" FILES instrument.c)
source_group("FlatBuffers Headers" FILES ${FLATBUFFERS_HEADERS})
source_group("FlatCC Runtime" FILES ${FLATCC_RUNTIME_SOURCES})
source_group("CFF" FILES ${CFF_SOURCES} cff/cff.h)

# Set Visual Studio startup project
set_property(DIRECTORY ${CMAKE_CURRENT_SOURCE_DIR} PROPERTY VS_STARTUP_PROJECT instrument)

# Configure for different build types
if(CMAKE_BUILD_TYPE STREQUAL "Debug")
    target_compile_definitions(instrument PRIVATE DEBUG=1)
endif()

# Add compiler warnings
if(MSVC)
    target_compile_options(instrument PRIVATE /W4)
    # Disable specific warnings that are common with generated code
    target_compile_options(instrument PRIVATE /wd4100)  # unreferenced formal parameter
    target_compile_options(instrument PRIVATE /wd4996)  # deprecated functions
    target_compile_options(instrument PRIVATE /wd4244)  # conversion warnings
    target_compile_options(instrument PRIVATE /wd4267)  # size_t conversion warnings
else()
    target_compile_options(instrument PRIVATE -Wall -Wextra -Wpedantic)
endif()

# Print build information
message(STATUS "Building Medical Instrument Application")
message(STATUS "C Standard: ${CMAKE_C_STANDARD}")
message(STATUS "Build Type: ${CMAKE_BUILD_TYPE}")
message(STATUS "Compiler: ${CMAKE_C_COMPILER_ID}")
message(STATUS "FlatCC Include: ${FLATCC_INCLUDE_DIR}")
message(STATUS "FlatCC Runtime: ${FLATCC_RUNTIME_DIR}")

# Custom target to show help
add_custom_target(usage
    COMMAND ${CMAKE_COMMAND} -E echo "Available targets:"
    COMMAND ${CMAKE_COMMAND} -E echo "  instrument - Build the medical instrument application"
    COMMAND ${CMAKE_COMMAND} -E echo "  clean      - Clean build files"
    COMMAND ${CMAKE_COMMAND} -E echo "  usage      - Show this help message"
    COMMENT "Showing available build targets"
) 