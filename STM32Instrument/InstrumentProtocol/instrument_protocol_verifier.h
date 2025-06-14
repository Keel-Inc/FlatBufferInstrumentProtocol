#ifndef INSTRUMENT_PROTOCOL_VERIFIER_H
#define INSTRUMENT_PROTOCOL_VERIFIER_H

/* Generated by flatcc 0.6.2 FlatBuffers schema compiler for C by dvide.com */

#ifndef INSTRUMENT_PROTOCOL_READER_H
#include "instrument_protocol_reader.h"
#endif
#include "flatcc/flatcc_verifier.h"
#include "flatcc/flatcc_prologue.h"

static int InstrumentProtocol_Command_verify_table(flatcc_table_verifier_descriptor_t *td);
static int InstrumentProtocol_Configuration_verify_table(flatcc_table_verifier_descriptor_t *td);
static int InstrumentProtocol_Measurement_verify_table(flatcc_table_verifier_descriptor_t *td);
static int InstrumentProtocol_Message_verify_table(flatcc_table_verifier_descriptor_t *td);

static int InstrumentProtocol_MessageType_union_verifier(flatcc_union_verifier_descriptor_t *ud)
{
    switch (ud->type) {
    case 1: return flatcc_verify_union_table(ud, InstrumentProtocol_Command_verify_table); /* Command */
    case 2: return flatcc_verify_union_table(ud, InstrumentProtocol_Configuration_verify_table); /* Configuration */
    case 3: return flatcc_verify_union_table(ud, InstrumentProtocol_Measurement_verify_table); /* Measurement */
    default: return flatcc_verify_ok;
    }
}

static int InstrumentProtocol_Command_verify_table(flatcc_table_verifier_descriptor_t *td)
{
    int ret;
    if ((ret = flatcc_verify_field(td, 0, 1, 1) /* code */)) return ret;
    return flatcc_verify_ok;
}

static inline int InstrumentProtocol_Command_verify_as_root(const void *buf, size_t bufsiz)
{
    return flatcc_verify_table_as_root(buf, bufsiz, InstrumentProtocol_Command_identifier, &InstrumentProtocol_Command_verify_table);
}

static inline int InstrumentProtocol_Command_verify_as_root_with_size(const void *buf, size_t bufsiz)
{
    return flatcc_verify_table_as_root_with_size(buf, bufsiz, InstrumentProtocol_Command_identifier, &InstrumentProtocol_Command_verify_table);
}

static inline int InstrumentProtocol_Command_verify_as_typed_root(const void *buf, size_t bufsiz)
{
    return flatcc_verify_table_as_root(buf, bufsiz, InstrumentProtocol_Command_type_identifier, &InstrumentProtocol_Command_verify_table);
}

static inline int InstrumentProtocol_Command_verify_as_typed_root_with_size(const void *buf, size_t bufsiz)
{
    return flatcc_verify_table_as_root_with_size(buf, bufsiz, InstrumentProtocol_Command_type_identifier, &InstrumentProtocol_Command_verify_table);
}

static inline int InstrumentProtocol_Command_verify_as_root_with_identifier(const void *buf, size_t bufsiz, const char *fid)
{
    return flatcc_verify_table_as_root(buf, bufsiz, fid, &InstrumentProtocol_Command_verify_table);
}

static inline int InstrumentProtocol_Command_verify_as_root_with_identifier_and_size(const void *buf, size_t bufsiz, const char *fid)
{
    return flatcc_verify_table_as_root_with_size(buf, bufsiz, fid, &InstrumentProtocol_Command_verify_table);
}

static inline int InstrumentProtocol_Command_verify_as_root_with_type_hash(const void *buf, size_t bufsiz, flatbuffers_thash_t thash)
{
    return flatcc_verify_table_as_typed_root(buf, bufsiz, thash, &InstrumentProtocol_Command_verify_table);
}

static inline int InstrumentProtocol_Command_verify_as_root_with_type_hash_and_size(const void *buf, size_t bufsiz, flatbuffers_thash_t thash)
{
    return flatcc_verify_table_as_typed_root_with_size(buf, bufsiz, thash, &InstrumentProtocol_Command_verify_table);
}

static int InstrumentProtocol_Configuration_verify_table(flatcc_table_verifier_descriptor_t *td)
{
    int ret;
    if ((ret = flatcc_verify_field(td, 0, 4, 4) /* measurements_per_second */)) return ret;
    if ((ret = flatcc_verify_field(td, 1, 4, 4) /* samples_per_measurement */)) return ret;
    return flatcc_verify_ok;
}

static inline int InstrumentProtocol_Configuration_verify_as_root(const void *buf, size_t bufsiz)
{
    return flatcc_verify_table_as_root(buf, bufsiz, InstrumentProtocol_Configuration_identifier, &InstrumentProtocol_Configuration_verify_table);
}

static inline int InstrumentProtocol_Configuration_verify_as_root_with_size(const void *buf, size_t bufsiz)
{
    return flatcc_verify_table_as_root_with_size(buf, bufsiz, InstrumentProtocol_Configuration_identifier, &InstrumentProtocol_Configuration_verify_table);
}

static inline int InstrumentProtocol_Configuration_verify_as_typed_root(const void *buf, size_t bufsiz)
{
    return flatcc_verify_table_as_root(buf, bufsiz, InstrumentProtocol_Configuration_type_identifier, &InstrumentProtocol_Configuration_verify_table);
}

static inline int InstrumentProtocol_Configuration_verify_as_typed_root_with_size(const void *buf, size_t bufsiz)
{
    return flatcc_verify_table_as_root_with_size(buf, bufsiz, InstrumentProtocol_Configuration_type_identifier, &InstrumentProtocol_Configuration_verify_table);
}

static inline int InstrumentProtocol_Configuration_verify_as_root_with_identifier(const void *buf, size_t bufsiz, const char *fid)
{
    return flatcc_verify_table_as_root(buf, bufsiz, fid, &InstrumentProtocol_Configuration_verify_table);
}

static inline int InstrumentProtocol_Configuration_verify_as_root_with_identifier_and_size(const void *buf, size_t bufsiz, const char *fid)
{
    return flatcc_verify_table_as_root_with_size(buf, bufsiz, fid, &InstrumentProtocol_Configuration_verify_table);
}

static inline int InstrumentProtocol_Configuration_verify_as_root_with_type_hash(const void *buf, size_t bufsiz, flatbuffers_thash_t thash)
{
    return flatcc_verify_table_as_typed_root(buf, bufsiz, thash, &InstrumentProtocol_Configuration_verify_table);
}

static inline int InstrumentProtocol_Configuration_verify_as_root_with_type_hash_and_size(const void *buf, size_t bufsiz, flatbuffers_thash_t thash)
{
    return flatcc_verify_table_as_typed_root_with_size(buf, bufsiz, thash, &InstrumentProtocol_Configuration_verify_table);
}

static int InstrumentProtocol_Measurement_verify_table(flatcc_table_verifier_descriptor_t *td)
{
    int ret;
    if ((ret = flatcc_verify_vector_field(td, 0, 0, 4, 4, INT64_C(1073741823)) /* data */)) return ret;
    return flatcc_verify_ok;
}

static inline int InstrumentProtocol_Measurement_verify_as_root(const void *buf, size_t bufsiz)
{
    return flatcc_verify_table_as_root(buf, bufsiz, InstrumentProtocol_Measurement_identifier, &InstrumentProtocol_Measurement_verify_table);
}

static inline int InstrumentProtocol_Measurement_verify_as_root_with_size(const void *buf, size_t bufsiz)
{
    return flatcc_verify_table_as_root_with_size(buf, bufsiz, InstrumentProtocol_Measurement_identifier, &InstrumentProtocol_Measurement_verify_table);
}

static inline int InstrumentProtocol_Measurement_verify_as_typed_root(const void *buf, size_t bufsiz)
{
    return flatcc_verify_table_as_root(buf, bufsiz, InstrumentProtocol_Measurement_type_identifier, &InstrumentProtocol_Measurement_verify_table);
}

static inline int InstrumentProtocol_Measurement_verify_as_typed_root_with_size(const void *buf, size_t bufsiz)
{
    return flatcc_verify_table_as_root_with_size(buf, bufsiz, InstrumentProtocol_Measurement_type_identifier, &InstrumentProtocol_Measurement_verify_table);
}

static inline int InstrumentProtocol_Measurement_verify_as_root_with_identifier(const void *buf, size_t bufsiz, const char *fid)
{
    return flatcc_verify_table_as_root(buf, bufsiz, fid, &InstrumentProtocol_Measurement_verify_table);
}

static inline int InstrumentProtocol_Measurement_verify_as_root_with_identifier_and_size(const void *buf, size_t bufsiz, const char *fid)
{
    return flatcc_verify_table_as_root_with_size(buf, bufsiz, fid, &InstrumentProtocol_Measurement_verify_table);
}

static inline int InstrumentProtocol_Measurement_verify_as_root_with_type_hash(const void *buf, size_t bufsiz, flatbuffers_thash_t thash)
{
    return flatcc_verify_table_as_typed_root(buf, bufsiz, thash, &InstrumentProtocol_Measurement_verify_table);
}

static inline int InstrumentProtocol_Measurement_verify_as_root_with_type_hash_and_size(const void *buf, size_t bufsiz, flatbuffers_thash_t thash)
{
    return flatcc_verify_table_as_typed_root_with_size(buf, bufsiz, thash, &InstrumentProtocol_Measurement_verify_table);
}

static int InstrumentProtocol_Message_verify_table(flatcc_table_verifier_descriptor_t *td)
{
    int ret;
    if ((ret = flatcc_verify_union_field(td, 1, 0, &InstrumentProtocol_MessageType_union_verifier) /* message_type */)) return ret;
    return flatcc_verify_ok;
}

static inline int InstrumentProtocol_Message_verify_as_root(const void *buf, size_t bufsiz)
{
    return flatcc_verify_table_as_root(buf, bufsiz, InstrumentProtocol_Message_identifier, &InstrumentProtocol_Message_verify_table);
}

static inline int InstrumentProtocol_Message_verify_as_root_with_size(const void *buf, size_t bufsiz)
{
    return flatcc_verify_table_as_root_with_size(buf, bufsiz, InstrumentProtocol_Message_identifier, &InstrumentProtocol_Message_verify_table);
}

static inline int InstrumentProtocol_Message_verify_as_typed_root(const void *buf, size_t bufsiz)
{
    return flatcc_verify_table_as_root(buf, bufsiz, InstrumentProtocol_Message_type_identifier, &InstrumentProtocol_Message_verify_table);
}

static inline int InstrumentProtocol_Message_verify_as_typed_root_with_size(const void *buf, size_t bufsiz)
{
    return flatcc_verify_table_as_root_with_size(buf, bufsiz, InstrumentProtocol_Message_type_identifier, &InstrumentProtocol_Message_verify_table);
}

static inline int InstrumentProtocol_Message_verify_as_root_with_identifier(const void *buf, size_t bufsiz, const char *fid)
{
    return flatcc_verify_table_as_root(buf, bufsiz, fid, &InstrumentProtocol_Message_verify_table);
}

static inline int InstrumentProtocol_Message_verify_as_root_with_identifier_and_size(const void *buf, size_t bufsiz, const char *fid)
{
    return flatcc_verify_table_as_root_with_size(buf, bufsiz, fid, &InstrumentProtocol_Message_verify_table);
}

static inline int InstrumentProtocol_Message_verify_as_root_with_type_hash(const void *buf, size_t bufsiz, flatbuffers_thash_t thash)
{
    return flatcc_verify_table_as_typed_root(buf, bufsiz, thash, &InstrumentProtocol_Message_verify_table);
}

static inline int InstrumentProtocol_Message_verify_as_root_with_type_hash_and_size(const void *buf, size_t bufsiz, flatbuffers_thash_t thash)
{
    return flatcc_verify_table_as_typed_root_with_size(buf, bufsiz, thash, &InstrumentProtocol_Message_verify_table);
}

#include "flatcc/flatcc_epilogue.h"
#endif /* INSTRUMENT_PROTOCOL_VERIFIER_H */
