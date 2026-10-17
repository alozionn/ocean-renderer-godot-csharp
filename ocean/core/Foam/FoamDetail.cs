using System;
using System.Linq;

namespace Ocean.Core.Foam;

/// <summary>
/// Foam detail texture maths: the procedural fallback and the brightness matching every detail texture needs
/// (Crest's distribution: mean 0.41, p10 0.06, p90 0.73; the lace erosion thresholds assume it).
/// </summary>
public static class FoamDetail
{
    public const float TargetMean = 0.41f;

    /// <summary>Tileable domain-warped value-noise clouds with soft bubble spots, matched to Crest's distribution.</summary>
    public static float[] Generate(int size = 512)
    {
        var v = new float[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, w = (y + 0.5f) / size;
                float wx = (Fbm(u, w, 3, 3, 0.5f, 11) - 0.5f) * 2f * 0.09f;
                float wy = (Fbm(u, w, 3, 3, 0.5f, 23) - 0.5f) * 2f * 0.09f;
                float clouds = Fbm(u + wx, w + wy, 4, 5, 0.55f, 37);
                float bubbles = MathF.Max(Bubbles(u, w, 14, 0.45f, 51), 0.8f * Bubbles(u, w, 31, 0.4f, 67));
                v[y * size + x] = clouds + bubbles * clouds * 0.35f;
            }
        float min = v.Min(), max = v.Max();
        for (int i = 0; i < v.Length; i++) v[i] = (v[i] - min) / (max - min);
        return MatchCrest(v);
    }

    /// <summary>Stretches levels from the 1st..99th percentile to 0..1, then a gamma so the mean lands at 0.41.</summary>
    public static float[] MatchCrest(float[] values)
    {
        var sorted = (float[])values.Clone();
        Array.Sort(sorted);
        float lo = sorted[(int)(0.01 * (sorted.Length - 1))], hi = sorted[(int)(0.99 * (sorted.Length - 1))];
        var s = values.Select(x => Math.Clamp((x - lo) / MathF.Max(hi - lo, 1e-6f), 0f, 1f)).ToArray();
        double a = 0.05, b = 20;
        for (int it = 0; it < 50; it++)
        {
            double g = Math.Sqrt(a * b);
            if (s.Average(x => Math.Pow(x, g)) > TargetMean) a = g; else b = g;
        }
        float gamma = (float)Math.Sqrt(a * b);
        return s.Select(x => MathF.Pow(x, gamma)).ToArray();
    }

    public static (float Mean, float P10, float P90) Stats(float[] values)
    {
        var sorted = (float[])values.Clone();
        Array.Sort(sorted);
        return (values.Average(), sorted[(int)(0.1 * (sorted.Length - 1))], sorted[(int)(0.9 * (sorted.Length - 1))]);
    }

    static float Hash(int x, int y, int seed)
    {
        uint h = unchecked((uint)(x * 374761393 + y * 668265263 + seed * 2147483647));
        h = (h ^ (h >> 13)) * 1274126177u;
        return ((h ^ (h >> 16)) & 0xffffff) / 16777216f;
    }

    static int Wrap(int i, int n) => ((i % n) + n) % n;

    /// <summary>Value noise on a lattice with `cells` cells per tile, so it tiles.</summary>
    static float Noise(float u, float v, int cells, int seed)
    {
        float x = u * cells, y = v * cells;
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        float fx = x - ix, fy = y - iy;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        float H(int dx, int dy) => Hash(Wrap(ix + dx, cells), Wrap(iy + dy, cells), seed);
        return Lerp(Lerp(H(0, 0), H(1, 0), fx), Lerp(H(0, 1), H(1, 1), fx), fy);
    }

    static float Fbm(float u, float v, int cells, int octaves, float gain, int seed)
    {
        float sum = 0, amp = 1, norm = 0;
        for (int o = 0; o < octaves; o++)
        {
            sum += amp * Noise(u, v, cells << o, seed + o);
            norm += amp;
            amp *= gain;
        }
        return sum / norm;
    }

    /// <summary>Soft round spots, one per cell at a random position, radius up to maxRadius cells.</summary>
    static float Bubbles(float u, float v, int cells, float maxRadius, int seed)
    {
        float x = u * cells, y = v * cells, best = 0;
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int cx = Wrap(ix + dx, cells), cy = Wrap(iy + dy, cells);
                float px = ix + dx + Hash(cx, cy, seed), py = iy + dy + Hash(cx, cy, seed + 1);
                float r = maxRadius * (0.4f + 0.6f * Hash(cx, cy, seed + 2));
                float d = MathF.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
                float t = Math.Clamp(1 - d / r, 0, 1);
                best = MathF.Max(best, t * t * (3 - 2 * t));
            }
        return best;
    }

    static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
