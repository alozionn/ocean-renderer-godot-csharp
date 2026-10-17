using System;
using System.Linq;
using System.Numerics;

namespace Ocean.Core.Mesh;

/// <summary>
/// A (2R+1)^2 grid of square Chebyshev rings around the camera: ring n has half-size n * spacing for the first
/// <c>linearRings</c>, then grows geometrically to <c>outerHalfSize</c> at ring R. Quads are sorted by ring
/// (near water first) and wound clockwise seen from above.
/// </summary>
public static class RingLayout
{
    public static float HalfSize(int ring, int rings = 256, int linearRings = 32, float spacing = 0.25f, float outerHalfSize = 4000f)
    {
        if (ring <= linearRings) return ring * spacing;
        float inner = linearRings * spacing;
        return inner * MathF.Pow(outerHalfSize / inner, (ring - linearRings) / (float)(rings - linearRings));
    }

    /// <summary>
    /// Vertex spacing grows by this fraction of the Chebyshev distance in the geometric rings:
    /// spacing(d) = max(spacing, d * growth). The surface shader uses it to pick the displacement mip.
    /// </summary>
    public static float Growth(int rings = 256, int linearRings = 32, float spacing = 0.25f, float outerHalfSize = 4000f) =>
        MathF.Pow(outerHalfSize / (linearRings * spacing), 1f / (rings - linearRings)) - 1f;

    public static (Vector2[] Positions, int[] Indices) Build(int rings = 256, int linearRings = 32, float spacing = 0.25f, float outerHalfSize = 4000f)
    {
        int side = 2 * rings + 1;
        var positions = new Vector2[side * side];
        for (int j = -rings; j <= rings; j++)
            for (int i = -rings; i <= rings; i++)
            {
                int n = Math.Max(Math.Abs(i), Math.Abs(j));
                positions[(j + rings) * side + i + rings] = n == 0 ? Vector2.Zero
                    : new Vector2(i, j) / n * HalfSize(n, rings, linearRings, spacing, outerHalfSize);
            }

        // Bucket quads by ring: quad (i, j)..(i+1, j+1) belongs to the outer ring of its corners.
        var counts = new int[rings + 1];
        int Ring(int i, int j) => Math.Max(Math.Max(Math.Abs(i), Math.Abs(i + 1)), Math.Max(Math.Abs(j), Math.Abs(j + 1)));
        for (int j = -rings; j < rings; j++)
            for (int i = -rings; i < rings; i++) counts[Ring(i, j)]++;
        var start = new int[rings + 1];
        for (int r = 1; r <= rings; r++) start[r] = start[r - 1] + counts[r - 1] * 6;
        var indices = new int[counts.Sum() * 6];
        for (int j = -rings; j < rings; j++)
            for (int i = -rings; i < rings; i++)
            {
                int a = (j + rings) * side + i + rings, b = a + 1, c = a + side, d = c + 1;
                ref int o = ref start[Ring(i, j)];
                indices[o++] = a; indices[o++] = b; indices[o++] = c;
                indices[o++] = b; indices[o++] = d; indices[o++] = c;
            }
        return (positions, indices);
    }
}
