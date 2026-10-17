#nullable enable
using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Ocean.Core.Config;

/// <summary>JSON (de)serialisation of <see cref="OceanConfig"/>: camelCase, indented, unknown fields ignored, missing fields keep defaults.</summary>
public static class OceanConfigJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Serialize(OceanConfig config) => JsonSerializer.Serialize(config, Options);

    /// <summary>Overlays the JSON onto the default config, so every missing field (also inside cascades) keeps its default.</summary>
    public static OceanConfig Deserialize(string json)
    {
        var merged = JsonSerializer.SerializeToNode(new OceanConfig(), Options)!.AsObject();
        if (JsonNode.Parse(json, null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) is JsonObject src)
            Merge(merged, src);
        return merged.Deserialize<OceanConfig>(Options)!;
    }

    static void Merge(JsonObject dst, JsonObject src)
    {
        foreach (var (key, value) in src.ToArray())
        {
            string name = dst.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) ?? key;
            JsonNode? existing = dst[name];
            if (existing is JsonObject eo && value is JsonObject so)
                Merge(eo, so);
            else if (existing is JsonArray ea && value is JsonArray sa && ea.Count > 0 && ea[0] is JsonObject)
            {
                var result = new JsonArray();
                for (int i = 0; i < sa.Count; i++)
                {
                    var item = ea[Math.Min(i, ea.Count - 1)]!.DeepClone().AsObject();
                    if (sa[i] is JsonObject si) Merge(item, si);
                    result.Add(item);
                }
                dst[name] = result;
            }
            else dst[name] = value?.DeepClone();
        }
    }

    public static JsonElement Number(float v) => JsonSerializer.SerializeToElement(Math.Round((double)v, 5));

    public static JsonElement Color(Vector3 c) =>
        JsonSerializer.SerializeToElement(string.Create(CultureInfo.InvariantCulture,
            $"#{ToByte(c.X):x2}{ToByte(c.Y):x2}{ToByte(c.Z):x2}"));

    static int ToByte(float v) => (int)MathF.Round(Math.Clamp(v, 0f, 1f) * 255f);

    /// <summary>Reads a colour written as "#rrggbb" or [r, g, b] (0..1).</summary>
    public static bool TryColor(JsonElement e, out Vector3 c)
    {
        c = default;
        if (e.ValueKind == JsonValueKind.String && e.GetString() is { Length: 7 } s && s[0] == '#'
            && int.TryParse(s.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
        {
            c = new Vector3((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f);
            return true;
        }
        if (e.ValueKind == JsonValueKind.Array && e.GetArrayLength() >= 3)
        {
            var a = e.EnumerateArray().Take(3).Select(x => x.GetSingle()).ToArray();
            c = new Vector3(a[0], a[1], a[2]);
            return true;
        }
        return false;
    }

    public static bool TryNumber(JsonElement e, out float v)
    {
        v = 0;
        if (e.ValueKind != JsonValueKind.Number) return false;
        v = e.GetSingle();
        return true;
    }

    public static RangeAttribute RangeOf(Type type, string property) =>
        type.GetProperty(property)?.GetCustomAttribute<RangeAttribute>() ?? new RangeAttribute(0, 1, 0.01);
}
