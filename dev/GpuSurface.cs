using System;
using Godot;
using Ocean.Core.Sampling;
using Ocean.Core.Shore;
using Ocean.Simulation;
using Vec2 = System.Numerics.Vector2;
using Vec3 = System.Numerics.Vector3;

namespace OceanDev;

/// <summary>
/// The surface the GPU draws, on the CPU from read-back displacement textures (mip 0), displaced the way ocean.gdshader
/// does it: per cascade a bilinear lookup at grid / patch size scaled by shore_calm at the grid point, summed, and the
/// displacement inverted to find the height over a world xz. Leaves out the distance fade (none within 750 m of the camera).
/// </summary>
internal sealed class GpuSurface
{
    readonly float[][] _disp;
    readonly float[] _patch;
    readonly int _n;

    public SeaFloor Floor { get; init; }
    public BodyLayer Bodies { get; init; }
    public ShoreCalm Calm { get; init; } = ShoreCalm.Default;

    public GpuSurface(RenderingDevice rd, ReadOnlySpan<OceanCascade> cascades, ReadOnlySpan<float> patchSizes)
    {
        _n = cascades[0].Size;
        _disp = new float[cascades.Length][];
        _patch = patchSizes.ToArray();
        for (int c = 0; c < cascades.Length; c++) _disp[c] = GpuChecks.Halves(rd.TextureGetData(cascades[c].Displacement, 0));
    }

    /// <summary>Texel (x, y) of a cascade: (Dx, Dy, Dz) for grid point (x, y) * L / N, shown at the texel's centre.</summary>
    public Vec3 Texel(int cascade, int x, int y)
    {
        int o = ((y & (_n - 1)) * _n + (x & (_n - 1))) * 4;
        var d = _disp[cascade];
        return new(d[o], d[o + 1], d[o + 2]);
    }

    public Vec3 Displacement(Vec2 grid)
    {
        var shore = BodyLayer.Lookup(Floor, Bodies, grid);
        var sum = Vec3.Zero;
        for (int c = 0; c < _disp.Length; c++)
        {
            var f = grid / _patch[c] * _n - new Vec2(0.5f);
            int x = (int)MathF.Floor(f.X), y = (int)MathF.Floor(f.Y);
            float fx = f.X - x, fy = f.Y - y;
            var d = Vec3.Lerp(Vec3.Lerp(Texel(c, x, y), Texel(c, x + 1, y), fx), Vec3.Lerp(Texel(c, x, y + 1), Texel(c, x + 1, y + 1), fx), fy);
            sum += d * Calm.Factor(shore, c);
        }
        return sum;
    }

    /// <summary>Height above sea level over a world xz (fixed-point inversion of the choppy displacement).</summary>
    public float Height(Vec2 xz)
    {
        var p = xz;
        for (int i = 0; i < 16; i++)
        {
            var d = Displacement(p);
            p = xz - new Vec2(d.X, d.Z);
        }
        return Displacement(p).Y;
    }
}
