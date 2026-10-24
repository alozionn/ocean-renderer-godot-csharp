using System;
using Godot;
using Ocean.Core.Sampling;

namespace Ocean;

/// <summary>The water at one point (<see cref="Ocean.SampleWater(Vector3)"/>).</summary>
public struct WaterSample
{
    /// <summary>World y of the surface: sea level (the ocean's y) plus the waves.</summary>
    public float Height;
    public Vector3 Normal;
    /// <summary>Orbital velocity of the surface water (m/s); y is how fast it rises.</summary>
    public Vector3 Velocity;
}

// Water queries for gameplay: floating objects, buoys, splashes. The CPU re-evaluates the strongest swell and chop
// components of the GPU spectrum (Core.Sampling.WaveSampler); no GPU readback, nothing per frame unless asked.
public partial class Ocean
{
    readonly WaveSampler _waves = new();
    System.Numerics.Vector2[] _waterPoints = Array.Empty<System.Numerics.Vector2>();
    WaveSample[] _waterSamples = Array.Empty<WaveSample>();
    int _swellWaves = 256, _chopWaves = 32;

    /// <summary>Strongest swell (cascade 0) waves <see cref="SampleWater(Vector3)"/> sums; cost is linear in the count.</summary>
    public int SampledSwellWaves
    {
        get => _swellWaves;
        set { _swellWaves = Math.Max(0, value); ConfigureWater(); }
    }

    /// <summary>Strongest chop (cascade 1) waves summed too. The ripples and the rest of the chop are left out.</summary>
    public int SampledChopWaves
    {
        get => _chopWaves;
        set { _chopWaves = Math.Max(0, value); ConfigureWater(); }
    }

    /// <summary>The waves' clock: the swell cascade's simulation time (s, time scale included). Restarts with the ocean.</summary>
    public double WaterTime => _cascades.Length > 0 ? _cascades[0].Time : 0;

    /// <summary>The CPU sampler behind <see cref="SampleWater(Vector3)"/>: components and captured energy, for tools.</summary>
    internal WaveSampler WaterSampler => _waves;
    /// <summary>The last batch query: how many points and how long it took, for the readout.</summary>
    internal (int Points, double Milliseconds) LastWaterQuery { get; private set; }

    /// <summary>The water surface over a world position's xz, at the time the surface currently shows.</summary>
    public WaterSample SampleWater(Vector3 worldPosition)
    {
        WaterSample s = default;
        SampleWater(new ReadOnlySpan<Vector3>(in worldPosition), new Span<WaterSample>(ref s));
        return s;
    }

    /// <summary>Batch form of <see cref="SampleWater(Vector3)"/>: one result per position.</summary>
    public void SampleWater(ReadOnlySpan<Vector3> worldPositions, Span<WaterSample> results)
    {
        int n = worldPositions.Length;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_waterPoints.Length < n)
        {
            _waterPoints = new System.Numerics.Vector2[n];
            _waterSamples = new WaveSample[n];
        }
        for (int i = 0; i < Math.Min(_cascades.Length, _waves.CascadeCount); i++) _waves.SetTime(i, _cascades[i].Time);
        _waves.Floor = _seaFloor;
        for (int i = 0; i < n; i++) _waterPoints[i] = new System.Numerics.Vector2(worldPositions[i].X, worldPositions[i].Z);
        _waves.Sample(_waterPoints.AsSpan(0, n), _waterSamples.AsSpan(0, n));
        float level = GlobalPosition.Y;
        for (int i = 0; i < n; i++)
        {
            var s = _waterSamples[i];
            results[i] = new WaterSample
            {
                Height = level + s.Height,
                Normal = new Vector3(s.Normal.X, s.Normal.Y, s.Normal.Z),
                Velocity = new Vector3(s.Velocity.X, s.Velocity.Y, s.Velocity.Z),
            };
        }
        LastWaterQuery = (n, System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    /// <summary>Follows the config and the surface's shore uniforms; re-selects components only when a spectrum changed.</summary>
    void ConfigureWater()
    {
        _waves.Configure(_config, stackalloc int[] { _swellWaves, _chopWaves });
        if (_material == null) return;
        _waves.Calm = new ShoreCalm(ShaderFloat("shallow_swell_depth"), ShaderFloat("shallow_chop_depth"),
            ShaderFloat("shallow_ripple_depth"), ShaderFloat("lake_waves"), ShaderFloat("shore_waves"), ShaderFloat("shore_waves_land"));
        // The foam field calms its whitecaps and finds breaking with the same uniforms (foam_field.glsl, Shore block).
        _shore?.SetFoam(new Vector4(_waves.Calm.SwellDepth, _waves.Calm.ChopDepth, _waves.Calm.RippleDepth, _waves.Calm.LakeWaves),
            new Vector4(ShaderFloat("shore_waves"), ShaderFloat("shore_waves_land"), ShaderFloat("wash_depth"), ShaderFloat("wash_strength")),
            ShaderFloat("breaking_ratio"), ShaderFloat("breaking_strength"));
    }

    float ShaderFloat(string name)
    {
        var v = _material.GetShaderParameter(name);
        return v.VariantType != Variant.Type.Nil ? v.AsSingle() : RenderingServer.ShaderGetParameterDefault(_material.Shader.GetRid(), name).AsSingle();
    }
}
