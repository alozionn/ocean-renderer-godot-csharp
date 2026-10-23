#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ocean.Core.Shore;

/// <summary>
/// What the ocean knows about the ground under it, on a regular grid: water depth, signed distance to the shoreline
/// (+ in the sea, − on land) and whether a wet cell is open sea or an enclosed lake (cut off from the grid border).
/// Built once from any terrain's height grid; the ocean uploads it as one texture.
/// </summary>
public sealed class SeaFloor
{
    public int Width { get; }
    public int Depth { get; }
    public float CellSize { get; }
    /// <summary>World xz of sample (0, 0).</summary>
    public Vector2 Origin { get; }
    /// <summary>Water depth in metres per sample, row-major (z rows): sea level − ground height; negative on land.</summary>
    public float[] WaterDepth { get; }
    /// <summary>Signed distance to the open sea's shoreline in metres: positive in open sea, negative on land and in lakes.</summary>
    public float[] ShoreDistance { get; }
    /// <summary>1 for open sea, 0 for land and enclosed lakes.</summary>
    public float[] OpenSea { get; }

    SeaFloor(int width, int depth, float cellSize, Vector2 origin, float[] waterDepth, float[] shoreDistance, float[] openSea)
    {
        Width = width;
        Depth = depth;
        CellSize = cellSize;
        Origin = origin;
        WaterDepth = waterDepth;
        ShoreDistance = shoreDistance;
        OpenSea = openSea;
    }

    /// <param name="heights">Ground heights in world metres, row-major: index = z * width + x.</param>
    /// <param name="origin">World xz of sample (0, 0).</param>
    public static SeaFloor FromHeights(float[] heights, int width, int depth, float cellSize, Vector2 origin, float seaLevel = 0f)
    {
        if (width < 2 || depth < 2) throw new ArgumentException("A sea floor needs at least 2 x 2 samples.");
        if (heights.Length != width * depth) throw new ArgumentException($"Expected {width * depth} heights, got {heights.Length}.");
        if (cellSize <= 0f) throw new ArgumentException("Cell size must be positive.");

        int n = width * depth;
        var water = new float[n];
        for (int i = 0; i < n; i++) water[i] = seaLevel - heights[i];
        var open = OpenWater(water, width, depth);

        // Distance between sample centres, then moved half a cell so the zero sits between a wet and a dry sample.
        var toDry = DistanceTransform(i => open[i] == 0f, width, depth);
        var toSea = DistanceTransform(i => open[i] != 0f, width, depth);
        var shore = new float[n];
        for (int i = 0; i < n; i++)
            shore[i] = open[i] != 0f ? (MathF.Sqrt(toDry[i]) - 0.5f) * cellSize : -(MathF.Sqrt(toSea[i]) - 0.5f) * cellSize;
        return new SeaFloor(width, depth, cellSize, origin, water, shore, open);
    }

    /// <summary>
    /// The sea floor at a world xz, filtered like the shader's linear, clamped texture (bilinear between sample centres);
    /// deep open sea beyond the texture, which spans half a cell past the outer sample centres.
    /// </summary>
    public ShorePoint Sample(Vector2 xz)
    {
        float fx = (xz.X - Origin.X) / CellSize, fz = (xz.Y - Origin.Y) / CellSize;
        if (fx < -0.5f || fz < -0.5f || fx > Width - 0.5f || fz > Depth - 0.5f) return ShorePoint.Deep;
        fx = Math.Clamp(fx, 0f, Width - 1);
        fz = Math.Clamp(fz, 0f, Depth - 1);
        int x0 = Math.Min((int)fx, Width - 2), z0 = Math.Min((int)fz, Depth - 2);
        float tx = fx - x0, tz = fz - z0;
        float Bilinear(float[] v)
        {
            int i = z0 * Width + x0;
            float top = v[i] + (v[i + 1] - v[i]) * tx, bottom = v[i + Width] + (v[i + Width + 1] - v[i + Width]) * tx;
            return top + (bottom - top) * tz;
        }
        return new ShorePoint(Bilinear(WaterDepth), Bilinear(ShoreDistance), Bilinear(OpenSea), 0f);
    }

    /// <summary>Wet samples connected to the grid border (4-neighbour flood fill). With no wet border, every wet sample is open sea.</summary>
    static float[] OpenWater(float[] water, int width, int depth)
    {
        var open = new float[water.Length];
        var queue = new Queue<int>();
        void Seed(int i)
        {
            if (water[i] <= 0f || open[i] != 0f) return;
            open[i] = 1f;
            queue.Enqueue(i);
        }
        for (int x = 0; x < width; x++)
        {
            Seed(x);
            Seed((depth - 1) * width + x);
        }
        for (int z = 0; z < depth; z++)
        {
            Seed(z * width);
            Seed(z * width + width - 1);
        }
        if (queue.Count == 0)
        {
            for (int i = 0; i < water.Length; i++) open[i] = water[i] > 0f ? 1f : 0f;
            return open;
        }
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int x = i % width, z = i / width;
            if (x > 0) Seed(i - 1);
            if (x < width - 1) Seed(i + 1);
            if (z > 0) Seed(i - width);
            if (z < depth - 1) Seed(i + width);
        }
        return open;
    }

    /// <summary>
    /// Exact squared Euclidean distance (in samples) from every sample to the nearest target sample
    /// (Felzenszwalb and Huttenlocher, separable). No targets: a large finite value everywhere.
    /// </summary>
    public static float[] DistanceTransform(Func<int, bool> isTarget, int width, int depth)
    {
        const float Far = 1e12f;
        var d = new float[width * depth];
        for (int i = 0; i < d.Length; i++) d[i] = isTarget(i) ? 0f : Far;
        int longest = Math.Max(width, depth);
        var f = new float[longest];
        var result = new float[longest];
        var v = new int[longest];
        var zBounds = new double[longest + 1];
        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++) f[x] = d[z * width + x];
            Transform1D(f, width, result, v, zBounds);
            for (int x = 0; x < width; x++) d[z * width + x] = result[x];
        }
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++) f[z] = d[z * width + x];
            Transform1D(f, depth, result, v, zBounds);
            for (int z = 0; z < depth; z++) d[z * width + x] = Math.Min(result[z], Far);
        }
        return d;
    }

    /// <summary>Lower envelope of parabolas rooted at each sample: d[q] = min_p (q − p)² + f[p].</summary>
    static void Transform1D(float[] f, int n, float[] d, int[] v, double[] z)
    {
        int k = 0;
        v[0] = 0;
        z[0] = double.NegativeInfinity;
        z[1] = double.PositiveInfinity;
        for (int q = 1; q < n; q++)
        {
            double s = Intersection(f, q, v[k]);
            while (s <= z[k])
            {
                k--;
                s = Intersection(f, q, v[k]);
            }
            k++;
            v[k] = q;
            z[k] = s;
            z[k + 1] = double.PositiveInfinity;
        }
        k = 0;
        for (int q = 0; q < n; q++)
        {
            while (z[k + 1] < q) k++;
            double dq = q - v[k];
            d[q] = (float)(dq * dq + f[v[k]]);
        }
    }

    static double Intersection(float[] f, int q, int p) => ((f[q] + (double)q * q) - (f[p] + (double)p * p)) / (2.0 * (q - p));
}
