using Godot;
using Ocean.Core.Mesh;

namespace Ocean.Surface;

/// <summary>Builds the camera-centred ring grid as an ArrayMesh.</summary>
internal static class OceanMesh
{
    public const float SnapStep = 0.25f;

    public static ArrayMesh Create()
    {
        var (positions, indices) = RingLayout.Build();
        var vertices = new Vector3[positions.Length];
        for (int i = 0; i < positions.Length; i++) vertices[i] = new Vector3(positions[i].X, 0, positions[i].Y);
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh { CustomAabb = new Aabb(new Vector3(-6000, -100, -6000), new Vector3(12000, 200, 12000)) };
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
