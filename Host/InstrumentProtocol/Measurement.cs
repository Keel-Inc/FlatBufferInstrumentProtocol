// <auto-generated>
//  automatically generated by the FlatBuffers compiler, do not modify
// </auto-generated>

namespace InstrumentProtocol
{

using global::System;
using global::System.Collections.Generic;
using global::Google.FlatBuffers;

public struct Measurement : IFlatbufferObject
{
  private Table __p;
  public ByteBuffer ByteBuffer { get { return __p.bb; } }
  public static void ValidateVersion() { FlatBufferConstants.FLATBUFFERS_25_2_10(); }
  public static Measurement GetRootAsMeasurement(ByteBuffer _bb) { return GetRootAsMeasurement(_bb, new Measurement()); }
  public static Measurement GetRootAsMeasurement(ByteBuffer _bb, Measurement obj) { return (obj.__assign(_bb.GetInt(_bb.Position) + _bb.Position, _bb)); }
  public void __init(int _i, ByteBuffer _bb) { __p = new Table(_i, _bb); }
  public Measurement __assign(int _i, ByteBuffer _bb) { __init(_i, _bb); return this; }

  public float Data(int j) { int o = __p.__offset(4); return o != 0 ? __p.bb.GetFloat(__p.__vector(o) + j * 4) : (float)0; }
  public int DataLength { get { int o = __p.__offset(4); return o != 0 ? __p.__vector_len(o) : 0; } }
#if ENABLE_SPAN_T
  public Span<float> GetDataBytes() { return __p.__vector_as_span<float>(4, 4); }
#else
  public ArraySegment<byte>? GetDataBytes() { return __p.__vector_as_arraysegment(4); }
#endif
  public float[] GetDataArray() { return __p.__vector_as_array<float>(4); }

  public static Offset<InstrumentProtocol.Measurement> CreateMeasurement(FlatBufferBuilder builder,
      VectorOffset dataOffset = default(VectorOffset)) {
    builder.StartTable(1);
    Measurement.AddData(builder, dataOffset);
    return Measurement.EndMeasurement(builder);
  }

  public static void StartMeasurement(FlatBufferBuilder builder) { builder.StartTable(1); }
  public static void AddData(FlatBufferBuilder builder, VectorOffset dataOffset) { builder.AddOffset(0, dataOffset.Value, 0); }
  public static VectorOffset CreateDataVector(FlatBufferBuilder builder, float[] data) { builder.StartVector(4, data.Length, 4); for (int i = data.Length - 1; i >= 0; i--) builder.AddFloat(data[i]); return builder.EndVector(); }
  public static VectorOffset CreateDataVectorBlock(FlatBufferBuilder builder, float[] data) { builder.StartVector(4, data.Length, 4); builder.Add(data); return builder.EndVector(); }
  public static VectorOffset CreateDataVectorBlock(FlatBufferBuilder builder, ArraySegment<float> data) { builder.StartVector(4, data.Count, 4); builder.Add(data); return builder.EndVector(); }
  public static VectorOffset CreateDataVectorBlock(FlatBufferBuilder builder, IntPtr dataPtr, int sizeInBytes) { builder.StartVector(1, sizeInBytes, 1); builder.Add<float>(dataPtr, sizeInBytes); return builder.EndVector(); }
  public static void StartDataVector(FlatBufferBuilder builder, int numElems) { builder.StartVector(4, numElems, 4); }
  public static Offset<InstrumentProtocol.Measurement> EndMeasurement(FlatBufferBuilder builder) {
    int o = builder.EndTable();
    return new Offset<InstrumentProtocol.Measurement>(o);
  }
}


static public class MeasurementVerify
{
  static public bool Verify(Google.FlatBuffers.Verifier verifier, uint tablePos)
  {
    return verifier.VerifyTableStart(tablePos)
      && verifier.VerifyVectorOfData(tablePos, 4 /*Data*/, 4 /*float*/, false)
      && verifier.VerifyTableEnd(tablePos);
  }
}

}
