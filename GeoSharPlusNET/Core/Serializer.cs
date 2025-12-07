using Google.FlatBuffers;

namespace GSP.Core {
  /// <summary>
  /// Platform-independent serialization utilities using FlatBuffers.
  /// For Rhino-specific types, use GSP.Adapters.Rhino.RhinoAdapter.
  /// </summary>
  public static class Serializer {
    /// <summary>
    /// Serialize a 3D point (x, y, z) to a byte buffer.
    /// </summary>
    public static byte[] SerializePoint(double x, double y, double z) {
      var builder = new FlatBufferBuilder(64);

      FB.PointData.StartPointData(builder);
      var vecOffset = FB.Vec3.CreateVec3(builder, x, y, z);
      FB.PointData.AddPoint(builder, vecOffset);
      var ptOffset = FB.PointData.EndPointData(builder);

      builder.Finish(ptOffset.Value);
      return builder.SizedByteArray();
    }

    /// <summary>
    /// Deserialize a 3D point from a byte buffer.
    /// </summary>
    /// <returns>Tuple of (x, y, z) coordinates</returns>
    public static (double x, double y, double z) DeserializePoint(byte[] buffer) {
      var byteBuffer = new ByteBuffer(buffer);
      var pt = FB.PointData.GetRootAsPointData(byteBuffer).Point;

      return pt.HasValue
          ? (pt.Value.X, pt.Value.Y, pt.Value.Z)
          : (0.0, 0.0, 0.0);
    }

    /// <summary>
    /// Serialize an array of 3D points to a byte buffer.
    /// </summary>
    public static byte[] SerializePointArray(double[][] points) {
      var builder = new FlatBufferBuilder(1024);

      FB.PointArrayData.StartPointsVector(builder, points.Length);
      for (int i = points.Length - 1; i >= 0; i--) {
        FB.Vec3.CreateVec3(builder, points[i][0], points[i][1], points[i][2]);
      }
      var ptOffset = builder.EndVector();

      var arrayOffset = FB.PointArrayData.CreatePointArrayData(builder, ptOffset);
      builder.Finish(arrayOffset.Value);

      return builder.SizedByteArray();
    }

    /// <summary>
    /// Deserialize an array of 3D points from a byte buffer.
    /// </summary>
    /// <returns>Array of double[3] representing points</returns>
    public static double[][] DeserializePointArray(byte[] buffer) {
      var byteBuffer = new ByteBuffer(buffer);
      var pointArray = FB.PointArrayData.GetRootAsPointArrayData(byteBuffer);

      if (pointArray.PointsLength == 0)
        return Array.Empty<double[]>();

      var result = new double[pointArray.PointsLength][];
      for (int i = 0; i < pointArray.PointsLength; i++) {
        var pt = pointArray.Points(i);
        result[i] = pt.HasValue
            ? new double[] { pt.Value.X, pt.Value.Y, pt.Value.Z }
            : new double[] { 0.0, 0.0, 0.0 };
      }
      return result;
    }

    /// <summary>
    /// Serialize mesh data (vertices and triangle faces) to a byte buffer.
    /// </summary>
    /// <param name="vertices">Array of vertices as double[3] (x, y, z)</param>
    /// <param name="faces">Array of triangle faces as int[3] (v0, v1, v2)</param>
    public static byte[] SerializeMesh(double[][] vertices, int[][] faces) {
      var builder = new FlatBufferBuilder(1024);

      // Add vertices
      FB.MeshData.StartVerticesVector(builder, vertices.Length);
      for (int i = vertices.Length - 1; i >= 0; i--) {
        FB.Vec3.CreateVec3(builder, vertices[i][0], vertices[i][1], vertices[i][2]);
      }
      var verticesOffset = builder.EndVector();

      // Add faces
      FB.MeshData.StartFacesVector(builder, faces.Length);
      for (int i = faces.Length - 1; i >= 0; i--) {
        FB.Vec3i.CreateVec3i(builder, faces[i][0], faces[i][1], faces[i][2]);
      }
      var facesOffset = builder.EndVector();

      var meshOffset = FB.MeshData.CreateMeshData(builder, verticesOffset, facesOffset);
      builder.Finish(meshOffset.Value);

      return builder.SizedByteArray();
    }

    /// <summary>
    /// Deserialize mesh data from a byte buffer.
    /// </summary>
    /// <returns>Tuple of (vertices, faces) arrays</returns>
    public static (double[][] vertices, int[][] faces) DeserializeMesh(byte[] buffer) {
      var byteBuffer = new ByteBuffer(buffer);
      var meshData = FB.MeshData.GetRootAsMeshData(byteBuffer);

      var vertices = new double[meshData.VerticesLength][];
      for (int i = 0; i < meshData.VerticesLength; i++) {
        var v = meshData.Vertices(i);
        vertices[i] = v.HasValue
            ? new double[] { v.Value.X, v.Value.Y, v.Value.Z }
            : new double[] { 0.0, 0.0, 0.0 };
      }

      var faces = new int[meshData.FacesLength][];
      for (int i = 0; i < meshData.FacesLength; i++) {
        var f = meshData.Faces(i);
        faces[i] = f.HasValue
            ? new int[] { f.Value.X, f.Value.Y, f.Value.Z }
            : new int[] { 0, 0, 0 };
      }

      return (vertices, faces);
    }
  }
}
