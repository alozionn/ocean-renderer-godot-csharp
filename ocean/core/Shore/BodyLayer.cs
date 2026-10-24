#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Ocean.Core.Bodies;

namespace Ocean.Core.Shore;

/// <summary>
/// What the shore shader reads at one point: the sea floor texture's channels plus the lee of bodies. Depth is the
/// water over the ground or a body (breaking, contact foam); WaveDepth is the terrain's alone, and calms the
/// waves: a rock or a reef is too small to calm a swell, it breaks it.
/// </summary>
public readonly record struct ShorePoint(float Depth, float Distance, float Open, float Shelter)
{
    /// <summary>Deep open sea: what the shader reports without a sea floor or bodies.</summary>
    public static readonly ShorePoint Deep = new(1e4f, 1e4f, 1f, 0f);

    /// <summary>Water depth over the terrain alone (m), for the wave calm. Defaults to <see cref="Depth"/>.</summary>
    public float WaveDepth { get; init; } = Depth;
}

/// <summary>
/// Stationary <see cref="WaterBody"/>s folded into the shore data: a sparse set of fine pages (16 m, 0.25 m samples by
/// default) around the bodies, each holding the terrain's sea floor with the bodies merged in (depth, signed distance to
/// the nearest shore or waterline, open sea, lee shelter). The shader looks a page up first and falls back to the coarse
/// sea floor, so islands and props take the same shore path. Rebuilt on its own when bodies change; the terrain's
/// <see cref="SeaFloor"/> is only sampled, never rebuilt.
/// </summary>
public sealed class BodyLayer
{
    public const float DefaultPageSize = 16f;
    public const int DefaultCells = 64;

    /// <summary>Page side in metres.</summary>
    public float PageSize { get; }
    /// <summary>Cells per page side; a page holds <see cref="Samples"/>² samples, edges shared with its neighbours.</summary>
    public int Cells { get; }
    public int Samples => Cells + 1;
    /// <summary>World xz of index cell (0, 0)'s corner: a multiple of the page size.</summary>
    public Vector2 Origin { get; }
    public int IndexWidth { get; }
    public int IndexDepth { get; }
    /// <summary>Per index cell (row-major along z): page number + 1, or 0 where no body reaches.</summary>
    public int[] Index { get; }
    public int PageCount { get; }
    public int PagesPerRow { get; }
    public int AtlasWidth => PagesPerRow * Samples;
    public int AtlasHeight => (PageCount + PagesPerRow - 1) / PagesPerRow * Samples;
    /// <summary>RGBA floats per atlas texel: depth, distance, open, shelter.</summary>
    public float[] Atlas { get; }

    BodyLayer(float pageSize, int cells, Vector2 origin, int indexWidth, int indexDepth, int[] index, int pageCount, int pagesPerRow, float[] atlas)
    {
        PageSize = pageSize;
        Cells = cells;
        Origin = origin;
        IndexWidth = indexWidth;
        IndexDepth = indexDepth;
        Index = index;
        PageCount = pageCount;
        PagesPerRow = pagesPerRow;
        Atlas = atlas;
    }

    /// <summary>
    /// Folds the stationary bodies over the terrain (null: deep open sea). <paramref name="waveDirection"/> is the way the
    /// swell travels (world xz), for the lee. Null when no stationary body is given.
    /// </summary>
    public static BodyLayer? Build(SeaFloor? terrain, IEnumerable<WaterBody> bodies, Vector2 waveDirection,
        float pageSize = DefaultPageSize, int cells = DefaultCells, int maxIndexCells = 1 << 20)
    {
        if (pageSize <= 0f || cells < 1) throw new ArgumentException("Pages need a positive size and at least one cell.");
        waveDirection = waveDirection.LengthSquared() > 1e-12f ? Vector2.Normalize(waveDirection) : Vector2.UnitX;
        var list = new List<(WaterBody Body, Vector2 Min, Vector2 Max)>();
        foreach (var b in bodies)
            if (b.Stationary && b.Length > 0f && b.Beam > 0f)
            {
                var c = b.Clone();
                var (min, max) = Coverage(c, waveDirection);
                list.Add((c, min, max));
            }
        if (list.Count == 0) return null;

        var lo = new Vector2(float.MaxValue);
        var hi = new Vector2(float.MinValue);
        foreach (var (_, min, max) in list)
        {
            lo = Vector2.Min(lo, min);
            hi = Vector2.Max(hi, max);
        }
        int x0 = (int)MathF.Floor(lo.X / pageSize), z0 = (int)MathF.Floor(lo.Y / pageSize);
        int width = (int)MathF.Floor(hi.X / pageSize) - x0 + 1, depth = (int)MathF.Floor(hi.Y / pageSize) - z0 + 1;
        if ((long)width * depth > maxIndexCells)
            throw new ArgumentException($"Bodies spread over {width} x {depth} pages of {pageSize} m; more than {maxIndexCells} index cells.");

        // Pages every body touches, and which bodies each page needs.
        var index = new int[width * depth];
        var pageBodies = new List<List<int>>();
        var pageCells = new List<int>();
        for (int i = 0; i < list.Count; i++)
        {
            var (_, min, max) = list[i];
            int ax = (int)MathF.Floor(min.X / pageSize) - x0, bx = (int)MathF.Floor(max.X / pageSize) - x0;
            int az = (int)MathF.Floor(min.Y / pageSize) - z0, bz = (int)MathF.Floor(max.Y / pageSize) - z0;
            for (int z = az; z <= bz; z++)
            for (int x = ax; x <= bx; x++)
            {
                int cell = z * width + x;
                if (index[cell] == 0)
                {
                    pageBodies.Add(new List<int>());
                    pageCells.Add(cell);
                    index[cell] = pageBodies.Count;
                }
                pageBodies[index[cell] - 1].Add(i);
            }
        }

        int count = pageBodies.Count, samples = cells + 1;
        int perRow = (int)MathF.Ceiling(MathF.Sqrt(count));
        int atlasWidth = perRow * samples, atlasHeight = (count + perRow - 1) / perRow * samples;
        var atlas = new float[atlasWidth * atlasHeight * 4];
        var origin = new Vector2(x0 * pageSize, z0 * pageSize);
        float step = pageSize / cells;
        for (int p = 0; p < count; p++)
        {
            var corner = origin + new Vector2(pageCells[p] % width, pageCells[p] / width) * pageSize;
            int tx = p % perRow * samples, tz = p / perRow * samples;
            var near = pageBodies[p];
            for (int j = 0; j < samples; j++)
            for (int i = 0; i < samples; i++)
            {
                var xz = corner + new Vector2(i, j) * step;
                var point = terrain?.Sample(xz) ?? ShorePoint.Deep;
                point = Fold(point, xz, near, list, waveDirection);
                int o = ((tz + j) * atlasWidth + tx + i) * 4;
                atlas[o] = point.Depth;
                atlas[o + 1] = point.Distance;
                atlas[o + 2] = point.Open;
                atlas[o + 3] = point.Shelter;
            }
        }
        return new BodyLayer(pageSize, cells, origin, width, depth, index, count, perRow, atlas);
    }

    /// <summary>The page's sample there, bilinear like the shader; false where no page is (the coarse sea floor applies).</summary>
    public bool TrySample(Vector2 xz, out ShorePoint point)
    {
        point = default;
        var cell = (xz - Origin) / PageSize;
        int cx = (int)MathF.Floor(cell.X), cz = (int)MathF.Floor(cell.Y);
        if (cx < 0 || cz < 0 || cx >= IndexWidth || cz >= IndexDepth) return false;
        int page = Index[cz * IndexWidth + cx] - 1;
        if (page < 0) return false;
        float fx = (cell.X - cx) * Cells, fz = (cell.Y - cz) * Cells;
        int ix = Math.Min((int)fx, Cells - 1), iz = Math.Min((int)fz, Cells - 1);
        float tx = fx - ix, tz = fz - iz;
        int baseX = page % PagesPerRow * Samples + ix, baseZ = page / PagesPerRow * Samples + iz;
        float Channel(int c)
        {
            int a = (baseZ * AtlasWidth + baseX) * 4 + c, b = a + AtlasWidth * 4;
            float top = Atlas[a] + (Atlas[a + 4] - Atlas[a]) * tx, bottom = Atlas[b] + (Atlas[b + 4] - Atlas[b]) * tx;
            return top + (bottom - top) * tz;
        }
        point = new ShorePoint(Channel(0), Channel(1), Channel(2), Channel(3));
        return true;
    }

    /// <summary>
    /// What the shader reads at a world xz: a body page if one is there, else the sea floor, else deep sea. The wave
    /// depth is always the terrain's.
    /// </summary>
    public static ShorePoint Lookup(SeaFloor? floor, BodyLayer? bodies, Vector2 xz)
    {
        var ground = floor?.Sample(xz) ?? ShorePoint.Deep;
        if (bodies != null && bodies.TrySample(xz, out var p)) return p with { WaveDepth = ground.Depth };
        return ground;
    }

    /// <summary>Where a body changes anything: its footprint plus reach and skirt, and its lee downwind.</summary>
    static (Vector2 Min, Vector2 Max) Coverage(WaterBody b, Vector2 wave)
    {
        var (_, far) = Blend(b);
        float grow = MathF.Max(far, b.Skirt);
        var half = new Vector2(b.HalfExtent(Vector2.UnitX), b.HalfExtent(Vector2.UnitY)) + new Vector2(grow);
        var min = b.Position - half;
        var max = b.Position + half;
        if (b.Lee > 0f)
        {
            var (along, across, length) = LeeShape(b, wave);
            var side = new Vector2(-wave.Y, wave.X) * (across * 1.2f + 0.5f);
            var end = b.Position + wave * (along + length);
            foreach (var corner in new[] { b.Position + side, b.Position - side, end + side, end - side })
            {
                min = Vector2.Min(min, corner);
                max = Vector2.Max(max, corner);
            }
        }
        return (min, max);
    }

    /// <summary>The body's distance takes over within its reach and hands back to the terrain's by 1.5 × reach + 1 m.</summary>
    static (float Near, float Far) Blend(WaterBody b)
    {
        float reach = b.EffectiveReach;
        return (reach, 1.5f * reach + 1f);
    }

    /// <summary>Half depth along the waves, half width across them, and lee length.</summary>
    static (float Along, float Across, float Length) LeeShape(WaterBody b, Vector2 wave)
    {
        float across = b.HalfExtent(new Vector2(-wave.Y, wave.X));
        return (b.HalfExtent(wave), across, b.LeeLength * 2f * across);
    }

    /// <summary>Merges the listed bodies into a ground sample: shallowest depth, nearest waterline, strongest lee.</summary>
    static ShorePoint Fold(ShorePoint ground, Vector2 xz, List<int> near, List<(WaterBody Body, Vector2 Min, Vector2 Max)> all, Vector2 wave)
    {
        float depth = ground.Depth, distance = ground.Distance, open = ground.Open, shelter = ground.Shelter;
        foreach (int i in near)
        {
            var (b, min, max) = all[i];
            if (xz.X < min.X || xz.Y < min.Y || xz.X > max.X || xz.Y > max.Y) continue;
            float s = b.SignedDistance(xz);
            bool grounded = ground.Depth <= b.Draft;
            if (s <= 0f)
            {
                if (grounded)
                {
                    // Land for breaking and foam, but the waves keep running: the body hides the water inside it, and cutting
                    // them here tore the mesh into a wall around it.
                    depth = MathF.Min(depth, -b.Top);
                }
            }
            else if (grounded && b.Skirt > 0f)
                depth = MathF.Min(depth, ground.Depth * MathF.Min(s / b.Skirt, 1f));

            // Only a body that stands out of the water has a waterline; a submerged one only makes the water shallow.
            if (b.Top > 0f)
            {
                var (r0, r1) = Blend(b);
                float w = Smoothstep(r0, r1, s);
                distance = MathF.Min(distance, s + (ground.Distance - s) * w);
            }

            if (b.Lee > 0f)
            {
                var (along, across, length) = LeeShape(b, wave);
                var d = xz - b.Position;
                float u = Vector2.Dot(d, wave), v = MathF.Abs(d.X * -wave.Y + d.Y * wave.X);
                float behind = MathF.Max(u - along, 0f);
                // The shadow narrows downwind as the waves bend back into it, and fades in alongside the body.
                float width = across * (1f - 0.5f * behind / length);
                float lee = b.Lee * (1f - Smoothstep(0.6f * width, 1.2f * width + 0.5f, v))
                    * Math.Clamp((u + along) / (2f * along + 1e-3f), 0f, 1f) * (1f - Smoothstep(0f, length, behind));
                shelter = MathF.Max(shelter, lee);
            }
        }
        return new ShorePoint(depth, distance, open, shelter);
    }

    static float Smoothstep(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
