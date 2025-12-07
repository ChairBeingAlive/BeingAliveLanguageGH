using Google.FlatBuffers;
using Rhino.Geometry;

namespace GSP.Adapters.Rhino {
  /// <summary>
  /// Adapter for converting between Rhino geometry types and FlatBuffer serialization.
  /// Uses GSP.Core.Serializer for the underlying serialization logic.
  /// </summary>
  public static class RhinoAdapter {
    #region Point3d Serialization

    /// <summary>
    /// Serialize a Rhino Point3d to a byte buffer.
    /// </summary>
    public static byte[] ToBuffer(Point3d pt) {
      var builder = new FlatBufferBuilder(64);

      FB.PointData.StartPointData(builder);
      var vecOffset = FB.Vec3.CreateVec3(builder, pt.X, pt.Y, pt.Z);
      FB.PointData.AddPoint(builder, vecOffset);
      var ptOffset = FB.PointData.EndPointData(builder);

      builder.Finish(ptOffset.Value);
      return builder.SizedByteArray();
    }

    /// <summary>
    /// Deserialize a Point3d from a byte buffer.
    /// </summary>
    public static Point3d PointFromBuffer(byte[] buffer) {
      var byteBuffer = new ByteBuffer(buffer);
      var pt = FB.PointData.GetRootAsPointData(byteBuffer).Point;

      return pt.HasValue
          ? new Point3d(pt.Value.X, pt.Value.Y, pt.Value.Z)
          : new Point3d(0, 0, 0);
    }

    #endregion

    #region Point3d Array Serialization

    /// <summary>
    /// Serialize an array of Rhino Point3d to a byte buffer.
    /// </summary>
    public static byte[] ToBuffer(Point3d[] points) {
      var builder = new FlatBufferBuilder(1024);

      FB.PointArrayData.StartPointsVector(builder, points.Length);
      for (int i = points.Length - 1; i >= 0; i--) {
        FB.Vec3.CreateVec3(builder, points[i].X, points[i].Y, points[i].Z);
      }
      var ptOffset = builder.EndVector();

      var arrayOffset = FB.PointArrayData.CreatePointArrayData(builder, ptOffset);
      builder.Finish(arrayOffset.Value);

      return builder.SizedByteArray();
    }

    /// <summary>
    /// Deserialize an array of Point3d from a byte buffer.
    /// </summary>
    public static Point3d[] PointArrayFromBuffer(byte[] buffer) {
      var byteBuffer = new ByteBuffer(buffer);
      var pointArray = FB.PointArrayData.GetRootAsPointArrayData(byteBuffer);

      if (pointArray.PointsLength == 0)
        return Array.Empty<Point3d>();

      var result = new Point3d[pointArray.PointsLength];
      for (int i = 0; i < pointArray.PointsLength; i++) {
        var pt = pointArray.Points(i);
        result[i] = pt.HasValue
            ? new Point3d(pt.Value.X, pt.Value.Y, pt.Value.Z)
            : new Point3d(0, 0, 0);
      }
      return result;
    }

    #endregion

    #region Mesh Serialization

    /// <summary>
    /// Serialize a Rhino Mesh to a byte buffer (triangulates if necessary).
    /// </summary>
    public static byte[] ToBuffer(Mesh mesh) {
      var builder = new FlatBufferBuilder(1024);

      // Triangulate the mesh if it's not already triangulated
      Mesh triangulatedMesh = mesh.DuplicateMesh();
      if (!triangulatedMesh.Faces.TriangleCount.Equals(triangulatedMesh.Faces.Count)) {
        triangulatedMesh.Faces.ConvertQuadsToTriangles();
      }

      // Add vertices
      FB.MeshData.StartVerticesVector(builder, triangulatedMesh.Vertices.Count);
      for (int i = triangulatedMesh.Vertices.Count - 1; i >= 0; i--) {
        var vertex = triangulatedMesh.Vertices[i];
        FB.Vec3.CreateVec3(builder, vertex.X, vertex.Y, vertex.Z);
      }
      var verticesOffset = builder.EndVector();

      // Add faces (triangles)
      FB.MeshData.StartFacesVector(builder, triangulatedMesh.Faces.Count);
      for (int i = triangulatedMesh.Faces.Count - 1; i >= 0; i--) {
        var face = triangulatedMesh.Faces[i];
        FB.Vec3i.CreateVec3i(builder, face.A, face.B, face.C);
      }
      var facesOffset = builder.EndVector();

      var meshOffset = FB.MeshData.CreateMeshData(builder, verticesOffset, facesOffset);
      builder.Finish(meshOffset.Value);

      return builder.SizedByteArray();
    }

    /// <summary>
    /// Deserialize a Rhino Mesh from a byte buffer.
    /// </summary>
    public static Mesh MeshFromBuffer(byte[] buffer) {
      var byteBuffer = new ByteBuffer(buffer);
      var meshData = FB.MeshData.GetRootAsMeshData(byteBuffer);

      var mesh = new Mesh();

      // Add vertices
      for (int i = 0; i < meshData.VerticesLength; i++) {
        var vertex = meshData.Vertices(i);
        if (vertex.HasValue) {
          mesh.Vertices.Add(vertex.Value.X, vertex.Value.Y, vertex.Value.Z);
        }
      }

      // Add faces
      for (int i = 0; i < meshData.FacesLength; i++) {
        var face = meshData.Faces(i);
        if (face.HasValue) {
          mesh.Faces.AddFace(face.Value.X, face.Value.Y, face.Value.Z);
        }
      }

      if (mesh.IsValid) {
        mesh.RebuildNormals();
        mesh.Compact();
      }

      return mesh;
    }

    #endregion
  }
}
