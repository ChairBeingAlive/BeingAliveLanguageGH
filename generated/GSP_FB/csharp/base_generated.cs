// <auto-generated>
//  automatically generated by the FlatBuffers compiler, do not modify
// </auto-generated>

namespace GSP.FB
{

using global::System;
using global::System.Collections.Generic;
using global::Google.FlatBuffers;

public struct Vec3 : IFlatbufferObject
{
  private Struct __p;
  public ByteBuffer ByteBuffer { get { return __p.bb; } }
  public void __init(int _i, ByteBuffer _bb) { __p = new Struct(_i, _bb); }
  public Vec3 __assign(int _i, ByteBuffer _bb) { __init(_i, _bb); return this; }

  public double X { get { return __p.bb.GetDouble(__p.bb_pos + 0); } }
  public double Y { get { return __p.bb.GetDouble(__p.bb_pos + 8); } }
  public double Z { get { return __p.bb.GetDouble(__p.bb_pos + 16); } }

  public static Offset<GSP.FB.Vec3> CreateVec3(FlatBufferBuilder builder, double X, double Y, double Z) {
    builder.Prep(8, 24);
    builder.PutDouble(Z);
    builder.PutDouble(Y);
    builder.PutDouble(X);
    return new Offset<GSP.FB.Vec3>(builder.Offset);
  }
  public Vec3T UnPack() {
    var _o = new Vec3T();
    this.UnPackTo(_o);
    return _o;
  }
  public void UnPackTo(Vec3T _o) {
    _o.X = this.X;
    _o.Y = this.Y;
    _o.Z = this.Z;
  }
  public static Offset<GSP.FB.Vec3> Pack(FlatBufferBuilder builder, Vec3T _o) {
    if (_o == null) return default(Offset<GSP.FB.Vec3>);
    return CreateVec3(
      builder,
      _o.X,
      _o.Y,
      _o.Z);
  }
}

public class Vec3T
{
  public double X { get; set; }
  public double Y { get; set; }
  public double Z { get; set; }

  public Vec3T() {
    this.X = 0.0;
    this.Y = 0.0;
    this.Z = 0.0;
  }
}

public struct Vec3i : IFlatbufferObject
{
  private Struct __p;
  public ByteBuffer ByteBuffer { get { return __p.bb; } }
  public void __init(int _i, ByteBuffer _bb) { __p = new Struct(_i, _bb); }
  public Vec3i __assign(int _i, ByteBuffer _bb) { __init(_i, _bb); return this; }

  public int X { get { return __p.bb.GetInt(__p.bb_pos + 0); } }
  public int Y { get { return __p.bb.GetInt(__p.bb_pos + 4); } }
  public int Z { get { return __p.bb.GetInt(__p.bb_pos + 8); } }

  public static Offset<GSP.FB.Vec3i> CreateVec3i(FlatBufferBuilder builder, int X, int Y, int Z) {
    builder.Prep(4, 12);
    builder.PutInt(Z);
    builder.PutInt(Y);
    builder.PutInt(X);
    return new Offset<GSP.FB.Vec3i>(builder.Offset);
  }
  public Vec3iT UnPack() {
    var _o = new Vec3iT();
    this.UnPackTo(_o);
    return _o;
  }
  public void UnPackTo(Vec3iT _o) {
    _o.X = this.X;
    _o.Y = this.Y;
    _o.Z = this.Z;
  }
  public static Offset<GSP.FB.Vec3i> Pack(FlatBufferBuilder builder, Vec3iT _o) {
    if (_o == null) return default(Offset<GSP.FB.Vec3i>);
    return CreateVec3i(
      builder,
      _o.X,
      _o.Y,
      _o.Z);
  }
}

public class Vec3iT
{
  public int X { get; set; }
  public int Y { get; set; }
  public int Z { get; set; }

  public Vec3iT() {
    this.X = 0;
    this.Y = 0;
    this.Z = 0;
  }
}


}
