using System;
using System.Numerics;
using Ocean.Core.Shore;

namespace Ocean.Core.Sampling;

/// <summary>
/// CPU copy of shore.gdshaderinc: shore_at (body pages, else the sea floor texture, bilinear, deep open sea outside
/// both) and shore_calm (each cascade fades with water depth; enclosed water keeps lake_waves of every cascade; the lee
/// of bodies calms swell and chop; near the waterline every cascade keeps at least ShoreWaves, fading out
/// ShoreWavesLand metres inland). Values are the shader's uniforms of the same names.
/// </summary>
public readonly record struct ShoreCalm(float SwellDepth, float ChopDepth, float RippleDepth, float LakeWaves,
    float ShoreWaves = 0f, float ShoreWavesLand = 6f)
{
    public static readonly ShoreCalm Default = new(6f, 5f, 0.6f, 0.15f, 0.5f, 6f);

    /// <summary>What shore_at reports without a sea floor: deep open sea.</summary>
    public static readonly (float Depth, float Open) Deep = (1e4f, 1f);

    /// <summary>How much a body's lee takes from each cascade (shore.gdshaderinc LEE_CALM).</summary>
    static readonly Vector3 LeeCalm = new(1f, 0.8f, 0.3f);

    /// <summary>Wave strength of a cascade (0 swell, 1 chop, 2 ripples) over water of this depth and openness.</summary>
    public float Factor((float Depth, float Open) shore, int cascade) => Factor(new ShorePoint(shore.Depth, 1e4f, shore.Open, 0f), cascade);

    /// <summary>Wave strength of a cascade at a shore sample: by the terrain's water depth, lee included.</summary>
    public float Factor(ShorePoint shore, int cascade)
    {
        float full = cascade switch { 0 => SwellDepth, 1 => ChopDepth, _ => RippleDepth };
        float t = Math.Clamp(shore.WaveDepth / full, 0f, 1f);
        float lee = cascade switch { 0 => LeeCalm.X, 1 => LeeCalm.Y, _ => LeeCalm.Z };
        float near = Math.Clamp((shore.Distance + ShoreWavesLand) / MathF.Max(ShoreWavesLand, 1e-6f), 0f, 1f);
        float strength = MathF.Max(t * t * (3f - 2f * t), ShoreWaves * near * near * (3f - 2f * near));
        // Only enclosed water (not open sea, and wet) keeps LakeWaves; land near the coast keeps the floor.
        float wet = Math.Clamp(shore.WaveDepth, 0f, 1f);
        float lake = (1f - shore.Open) * wet * wet * (3f - 2f * wet);
        return strength * (1f + (LakeWaves - 1f) * lake) * (1f - shore.Shelter * lee);
    }

    /// <summary>Depth and openness at world xz, filtered like the shader's linear, clamped texture.</summary>
    public static (float Depth, float Open) Lookup(SeaFloor floor, Vector2 xz)
    {
        var p = floor.Sample(xz);
        return (p.Depth, p.Open);
    }
}
