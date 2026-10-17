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
    [Range(0, 20, 0.01)] public float DiffuseAlong { get; set; } = 0.35f;
    [Range(0, 4, 0.01)] public float DiffuseAcross { get; set; } = 0.06f;
    [Range(-90, 90, 1)] public float StreakAngleOffset { get; set; }
    [Range(0, 2, 0.01)] public float WeightSwell { get; set; } = 1f;
    [Range(0, 2, 0.01)] public float WeightChop { get; set; } = 1f;
    [Range(0, 2, 0.01)] public float WeightRipples { get; set; } = 0.3f;
}
