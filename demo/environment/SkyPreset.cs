using System.Collections.Generic;
using System.Text.Json;

namespace OceanDemo.Environment;

/// <summary>One sky: a panorama file (empty = procedural sunset) and where its painted sun is.</summary>
public sealed class SkyPreset
{
    public string Id { get; set; } = "";
    public string File { get; set; } = "";
    /// <summary>Sun azimuth in the image, degrees: u = (atan2(x, -z) + pi) / 2pi.</summary>
    public float SunAzimuth { get; set; }
    public float SunElevation { get; set; }
    public float[] SunColor { get; set; } = { 1, 1, 1 };
    public float SunEnergy { get; set; } = 1;

    public static List<SkyPreset> ParseList(string json) =>
        JsonSerializer.Deserialize<List<SkyPreset>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

    /// <summary>Sky yaw (degrees) that puts the painted sun straight ahead (-Z).</summary>
    public float DefaultYaw => string.IsNullOrEmpty(File) ? 0 : Wrap(SunAzimuth + 180);

    public static float Wrap(float deg) => deg - 360 * Godot.Mathf.Floor((deg + 180) / 360);
}
