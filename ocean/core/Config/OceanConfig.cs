using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Ocean.Core.Config;

/// <summary>UI range of a config field: min, max, step, and whether the slider is exponential.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class RangeAttribute : Attribute
{
    public RangeAttribute(double min, double max, double step) { Min = min; Max = max; Step = step; }
    public double Min { get; }
    public double Max { get; }
    public double Step { get; }
    public bool Exp { get; set; }
}

/// <summary>Marks fields whose change regenerates a cascade's initial spectrum.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SpectrumFieldAttribute : Attribute { }

public sealed class OceanConfig
{
    public static readonly int[] Resolutions = { 256, 512, 1024 };
    public static readonly string[] FoamTextures = { "crest", "ambientcg_001", "procedural" };

    public int Resolution { get; set; } = 512;
    [Range(15, 240, 1)] public float SimulationRate { get; set; } = 60f;
    public CascadeConfig[] Cascades { get; set; } =
    {
        new() { PatchSize = 400f, WindSpeed = 12f, WindDirection = 20f, Fetch = 200_000f, Suppression = 0.5f, HeightScale = 1.0f, Choppiness = 0.9f },
        new() { PatchSize = 73f, WindSpeed = 8f, WindDirection = 35f, Fetch = 60_000f, Suppression = 0.1f, HeightScale = 0.7f, Choppiness = 0.9f },
        new() { PatchSize = 13.7f, WindSpeed = 5f, WindDirection = 0f, Fetch = 30_000f, Suppression = 0.02f, HeightScale = 0.6f, Choppiness = 0.7f },
    };
    public FoamConfig Foam { get; set; } = new();
    public SprayConfig Spray { get; set; } = new();
    public string FoamTexture { get; set; } = "crest";
    /// <summary>Overrides of ocean.gdshader uniform defaults by name: numbers, "#rrggbb" or [r, g, b].</summary>
    public Dictionary<string, JsonElement> Surface { get; set; } = new();

    /// <summary>The calm preset.</summary>
    public static OceanConfig Default => new();

    public OceanConfig Clone() => OceanConfigJson.Deserialize(OceanConfigJson.Serialize(this));
}

public sealed class CascadeConfig
{
    [SpectrumField, Range(4, 2000, 0.1, Exp = true)] public float PatchSize { get; set; } = 100f;
    [SpectrumField, Range(0.5, 40, 0.1)] public float WindSpeed { get; set; } = 10f;
    [SpectrumField, Range(0, 360, 1)] public float WindDirection { get; set; }
    [SpectrumField, Range(1000, 1_000_000, 100, Exp = true)] public float Fetch { get; set; } = 100_000f;
    [SpectrumField, Range(0, 2, 0.01)] public float Suppression { get; set; }
    [Range(0, 3, 0.01)] public float TimeScale { get; set; } = 1f;
    [Range(0, 3, 0.01)] public float HeightScale { get; set; } = 1f;
    [Range(0, 2, 0.01)] public float Choppiness { get; set; } = 1f;
}

public sealed class FoamConfig
{
    [Range(-1, 2, 0.01)] public float Bias { get; set; } = 0.6f;
    [Range(0.1, 40, 0.1)] public float Gain { get; set; } = 5f;
    [Range(0.1, 60, 0.1)] public float InjectRate { get; set; } = 1.2f;
    [Range(0, 5, 0.01)] public float Decay { get; set; } = 0.35f;
    [Range(0, 20, 0.1)] public float FreshDecay { get; set; } = 2.5f;
    [Range(0, 20, 0.01)] public float DiffuseAlong { get; set; } = 1.5f;
    [Range(0, 4, 0.01)] public float DiffuseAcross { get; set; } = 0.3f;
    [Range(-90, 90, 1)] public float StreakAngleOffset { get; set; }
    [Range(0, 2, 0.01)] public float WeightSwell { get; set; } = 1f;
    [Range(0, 2, 0.01)] public float WeightChop { get; set; } = 1f;
    [Range(0, 2, 0.01)] public float WeightRipples { get; set; } = 0.3f;
}

/// <summary>Sea spray: GPU particles launched from fresh crest foam and from surf lines near the shore.</summary>
public sealed class SprayConfig
{
    /// <summary>Particle budget; each particle tries one spawn per lifetime, so this sets how many places get tested.</summary>
    [Range(0, 30000, 100)] public float Amount { get; set; } = 1200f;
    [Range(0.2, 6, 0.05)] public float Lifetime { get; set; } = 0.8f;
    /// <summary>Spawn area around the camera (m).</summary>
    [Range(10, 500, 1)] public float Radius { get; set; } = 160f;
    /// <summary>Crest freshness a crest needs before it throws spray.</summary>
    [Range(0, 2, 0.01)] public float Threshold { get; set; } = 0.3f;
    /// <summary>Upward launch speed (m/s).</summary>
    [Range(0, 20, 0.1)] public float Speed { get; set; } = 2.5f;
    /// <summary>Drift along the wind (m/s).</summary>
    [Range(0, 20, 0.1)] public float Wind { get; set; } = 5f;
    /// <summary>Fall acceleration as a fraction of g: mist is held up by air drag.</summary>
    [Range(0, 2, 0.01)] public float Gravity { get; set; } = 0.9f;
    /// <summary>Puff size at birth (m); puffs grow as they spread.</summary>
    [Range(0.1, 10, 0.05)] public float Size { get; set; } = 1.8f;
    [Range(0, 1, 0.01)] public float Opacity { get; set; } = 0.5f;
    /// <summary>Spray from surf lines and breaking shallows, relative to crest spray.</summary>
    [Range(0, 3, 0.01)] public float Shore { get; set; } = 1f;
}
