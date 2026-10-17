using System.Collections.Generic;
using System.Linq;
using Godot;

namespace OceanDemo.Environment;

/// <summary>Applies a <see cref="SkyPreset"/> to the WorldEnvironment and the sun, and keeps yaw/elevation/energies live.</summary>
public sealed class SkyRig
{
    const string SkyDir = "res://demo/skies/";
    readonly WorldEnvironment _env;
    readonly DirectionalLight3D _sun;

    public IReadOnlyList<SkyPreset> Presets { get; }
    public SkyPreset Current { get; private set; }
    public float Yaw { get; private set; }
    public float SunElevation { get; private set; }

    public SkyRig(WorldEnvironment env, DirectionalLight3D sun, string presetsPath)
    {
        _env = env;
        _sun = sun;
        Presets = SkyPreset.ParseList(FileAccess.GetFileAsString(presetsPath));
    }

    public void Apply(string id)
    {
        Current = Presets.FirstOrDefault(p => p.Id == id) ?? Presets[0];
        var sky = _env.Environment.Sky ??= new Sky();
        sky.RadianceSize = Sky.RadianceSizeEnum.Size256;
        if (string.IsNullOrEmpty(Current.File) || !ResourceLoader.Exists(SkyDir + Current.File))
            sky.SkyMaterial = new ProceduralSkyMaterial
            {
                SkyTopColor = new Color(0.22f, 0.29f, 0.48f),
                SkyHorizonColor = new Color(0.93f, 0.62f, 0.48f),
                SkyCurve = 0.12f,
                GroundBottomColor = new Color(0.36f, 0.3f, 0.36f),
                GroundHorizonColor = new Color(0.93f, 0.62f, 0.48f),
                SunAngleMax = 20,
                SunCurve = 0.08f,
            };
        else
            sky.SkyMaterial = new PanoramaSkyMaterial { Panorama = GD.Load<Texture2D>(SkyDir + Current.File), EnergyMultiplier = 1 };
        var c = Current.SunColor;
        _sun.LightColor = new Color(c[0], c[1], c[2]);
        _sun.LightEnergy = Current.SunEnergy;
        SunElevation = Current.SunElevation;
        SetYaw(Current.DefaultYaw);
    }

    /// <summary>Turns the sky by -yaw; the sun follows its painted position (procedural: the sun drawn from the light).</summary>
    public void SetYaw(float yaw)
    {
        Yaw = yaw;
        _env.Environment.SkyRotation = new Vector3(0, Mathf.DegToRad(yaw), 0);
        UpdateSun();
    }

    public void SetSunElevation(float elevation)
    {
        SunElevation = elevation;
        UpdateSun();
    }

    void UpdateSun()
    {
        bool procedural = string.IsNullOrEmpty(Current.File);
        float azimuth = Mathf.DegToRad(procedural ? -Yaw : Current.SunAzimuth - 180 - Yaw);
        float e = Mathf.DegToRad(SunElevation);
        var toSun = new Vector3(Mathf.Sin(azimuth) * Mathf.Cos(e), Mathf.Sin(e), -Mathf.Cos(azimuth) * Mathf.Cos(e));
        _sun.GlobalBasis = Basis.LookingAt(-toSun, Mathf.Abs(toSun.Y) > 0.99f ? Vector3.Forward : Vector3.Up);
    }
}
