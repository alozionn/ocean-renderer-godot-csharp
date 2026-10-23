using System;
using System.Numerics;
using Ocean.Core.Shore;

namespace Ocean.Core.Sampling;

/// <summary>
/// CPU copy of shore.gdshaderinc: shore_at (the sea floor texture, bilinear, deep open sea outside it) and shore_calm
/// (each cascade fades with water depth; enclosed water keeps lake_waves of every cascade). Values are the shader's
/// uniforms of the same names.
/// </summary>
public readonly record struct ShoreCalm(float SwellDepth, float ChopDepth, float RippleDepth, float LakeWaves)
{
    public static readonly ShoreCalm Default = new(16f, 5f, 0.6f, 0.15f);

    /// <summary>What shore_at reports without a sea floor: deep open sea.</summary>
    public static readonly (float Depth, float Open) Deep = (1e4f, 1f);

    /// <summary>Wave strength of a cascade (0 swell, 1 chop, 2 ripples) over water of this depth and openness.</summary>
    public float Factor((float Depth, float Open) shore, int cascade)
    {
        float full = cascade switch { 0 => SwellDepth, 1 => ChopDepth, _ => RippleDepth };
        float t = Math.Clamp(shore.Depth / full, 0f, 1f);
        return t * t * (3f - 2f * t) * (LakeWaves + (1f - LakeWaves) * shore.Open);
    }

    /// <summary>Depth and openness at world xz, filtered like the shader's linear, clamped texture.</summary>
    public static (float Depth, float Open) Lookup(SeaFloor floor, Vector2 xz)
    {
        // The texture spans half a cell beyond the outer sample centres; beyond that the sea is deep.
        float fx = (xz.X - floor.Origin.X) / floor.CellSize, fz = (xz.Y - floor.Origin.Y) / floor.CellSize;
        if (fx < -0.5f || fz < -0.5f || fx > floor.Width - 0.5f || fz > floor.Depth - 0.5f) return Deep;
        fx = Math.Clamp(fx, 0f, floor.Width - 1);
        fz = Math.Clamp(fz, 0f, floor.Depth - 1);
        int x0 = Math.Min((int)fx, floor.Width - 2), z0 = Math.Min((int)fz, floor.Depth - 2);
        float tx = fx - x0, tz = fz - z0;
        float Bilinear(float[] v)
        {
            int i = z0 * floor.Width + x0;
            float top = v[i] + (v[i + 1] - v[i]) * tx, bottom = v[i + floor.Width] + (v[i + floor.Width + 1] - v[i + floor.Width]) * tx;
            return top + (bottom - top) * tz;
        }
        return (Bilinear(floor.WaterDepth), Bilinear(floor.OpenSea));
    }
}
