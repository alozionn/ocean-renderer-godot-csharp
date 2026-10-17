using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ocean.Foam;

namespace OceanDev;

/// <summary>Foam field measurements over the near field's central 128 m x 128 m (section 9.6).</summary>
internal readonly record struct FoamMeasure(double Whitecaps, double Trails, double Anisotropy)
{
    public override string ToString() =>
        FormattableString.Invariant($"whitecaps (fresh > 0.3) {Whitecaps * 100:F2} %, trails (density > 0.15) {Trails * 100:F2} %, anisotropy {Anisotropy:F2}");
}

internal static class FoamStats
{
    const double Threshold = 0.5;

    /// <summary>Reads the near field back once: coverage fractions and the central density window.</summary>
    public static (double Caps, double Trails, float[,] Density) Read(OceanFoamField field)
    {
        var rd = RenderingServer.GetRenderingDevice();
        float[] rg = GpuChecks.Halves(rd.TextureGetData(field.CurrentRid, 0));
        int n = field.Size, half = (int)(64f / field.Texel), x0 = n / 2 - half;
        var density = new float[2 * half, 2 * half];
        long caps = 0, trails = 0;
        for (int y = 0; y < 2 * half; y++)
            for (int x = 0; x < 2 * half; x++)
            {
                int o = ((y + x0) * n + x + x0) * 2;
                density[y, x] = rg[o];
                if (rg[o + 1] > 0.3f) caps++;
                if (rg[o] > 0.15f) trails++;
            }
        return ((double)caps / density.Length, (double)trails / density.Length, density);
    }

    /// <summary>Ratio of the autocorrelation lengths along and across the streak direction, pooled over snapshots.</summary>
    public static double Anisotropy(IReadOnlyList<float[,]> snapshots, float streakAngleRad)
    {
        var dir = new Vector2(Mathf.Cos(streakAngleRad), Mathf.Sin(streakAngleRad));
        return CorrelationLength(snapshots, dir) / Math.Max(CorrelationLength(snapshots, new Vector2(-dir.Y, dir.X)), 1e-6);
    }

    /// <summary>Lag (texels) at which the autocorrelation along a direction falls to 0.5.</summary>
    static double CorrelationLength(IReadOnlyList<float[,]> snapshots, Vector2 dir)
    {
        int n = snapshots[0].GetLength(0);
        var means = snapshots.Select(v => v.Cast<float>().Average()).ToArray();
        double Corr(float lag)
        {
            double num = 0, den = 0;
            int m = (int)Math.Ceiling(lag) + 2;
            for (int s = 0; s < snapshots.Count; s++)
            {
                var v = snapshots[s];
                for (int y = m; y < n - m; y += 2)
                    for (int x = m; x < n - m; x += 2)
                    {
                        double a = v[y, x] - means[s];
                        num += a * (Sample(v, x + dir.X * lag, y + dir.Y * lag) - means[s]);
                        den += a * a;
                    }
            }
            return den > 0 ? num / den : 0;
        }
        float prevLag = 0;
        double prev = 1;
        for (float lag = 0.5f; lag < n / 4; lag += 0.5f)
        {
            double c = Corr(lag);
            if (c < Threshold) return prevLag + (lag - prevLag) * (prev - Threshold) / (prev - c);
            prev = c;
            prevLag = lag;
        }
        return n / 4;
    }

    static double Sample(float[,] v, float x, float y)
    {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        float fx = x - ix, fy = y - iy;
        return (v[iy, ix] * (1 - fx) + v[iy, ix + 1] * fx) * (1 - fy) + (v[iy + 1, ix] * (1 - fx) + v[iy + 1, ix + 1] * fx) * fy;
    }
}
