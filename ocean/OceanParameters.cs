using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Godot;
using Ocean.Core.Config;
using Ocean.Core.Parameters;
using Vector3N = System.Numerics.Vector3;

namespace Ocean;

/// <summary>Builds the ocean's parameter sets: Ocean, Cascade 0..n, Foam field, Surface (from the shader's uniform groups).</summary>
internal static class OceanParameters
{
    public static List<ParameterSet> Build(Ocean ocean)
    {
        var sets = new List<ParameterSet>
        {
            new("Ocean", new[]
            {
                Parameter.Enum("ocean.resolution", "Resolution", "", 0, OceanConfig.Resolutions.Select(r => r.ToString(CultureInfo.InvariantCulture)).ToArray(),
                    () => ocean.Config.Resolution.ToString(CultureInfo.InvariantCulture), v => Edit(ocean, c => c.Resolution = int.Parse(v, CultureInfo.InvariantCulture))),
                Float(ocean, typeof(OceanConfig), nameof(OceanConfig.SimulationRate), "ocean.simulationRate", "", 1, ApplyMode.Live,
                    c => c.SimulationRate, (c, v) => c.SimulationRate = v),
                Parameter.Readout("ocean.tickRate", "Tick rate", "", 2, () => $"{ocean.Config.SimulationRate:F0} Hz"),
                Parameter.Readout("ocean.cascadeTicks", "Cascade ticks / s", "", 3, () => $"{ocean.CascadeTickRate:F0}"),
            }),
        };
        for (int i = 0; i < ocean.Config.Cascades.Length; i++) sets.Add(CascadeSet(ocean, i));
        sets.Add(FoamSet(ocean));
        sets.Add(SpraySet(ocean));
        sets.Add(WaterSamplingSet(ocean));
        sets.Add(SurfaceSet(ocean));
        return sets;
    }

    static void Edit(Ocean ocean, Action<OceanConfig> change)
    {
        var c = ocean.Snapshot();
        change(c);
        ocean.Apply(c);
    }

    static Parameter Float(Ocean ocean, Type type, string property, string id, string group, int order, ApplyMode mode,
        Func<OceanConfig, float> get, Action<OceanConfig, float> set)
    {
        var r = OceanConfigJson.RangeOf(type, property);
        return Parameter.Float(id, Label(property), group, order, () => get(ocean.Config), v => Edit(ocean, c => set(c, v)),
            r.Min, r.Max, r.Step, r.Exp, mode);
    }

    static ParameterSet CascadeSet(Ocean ocean, int index)
    {
        var list = new List<Parameter>();
        int order = 0;
        foreach (var p in typeof(CascadeConfig).GetProperties())
        {
            var mode = p.GetCustomAttribute<SpectrumFieldAttribute>() != null ? ApplyMode.OnRelease : ApplyMode.Live;
            list.Add(Float(ocean, typeof(CascadeConfig), p.Name, $"cascade{index}.{Camel(p.Name)}", "", order++, mode,
                c => (float)p.GetValue(c.Cascades[index]), (c, v) => p.SetValue(c.Cascades[index], v)));
        }
        return new ParameterSet($"Cascade {index}", list);
    }

    static ParameterSet FoamSet(Ocean ocean)
    {
        var list = new List<Parameter>
        {
            Parameter.Enum("foam.texture", "Detail texture", "", 0, OceanConfig.FoamTextures, () => ocean.Config.FoamTexture,
                v => Edit(ocean, c => c.FoamTexture = v)),
        };
        int order = 1;
        foreach (var p in typeof(FoamConfig).GetProperties())
            list.Add(Float(ocean, typeof(FoamConfig), p.Name, $"foam.{Camel(p.Name)}", "", order++, ApplyMode.Live,
                c => (float)p.GetValue(c.Foam), (c, v) => p.SetValue(c.Foam, v)));
        return new ParameterSet("Foam field", list);
    }

    static ParameterSet SpraySet(Ocean ocean)
    {
        var list = new List<Parameter>();
        int order = 0;
        foreach (var p in typeof(SprayConfig).GetProperties())
        {
            // The particle count and lifetime restart the particles: apply them when the slider is released.
            var mode = p.Name is nameof(SprayConfig.Amount) or nameof(SprayConfig.Lifetime) ? ApplyMode.OnRelease : ApplyMode.Live;
            list.Add(Float(ocean, typeof(SprayConfig), p.Name, $"spray.{Camel(p.Name)}", "", order++, mode,
                c => (float)p.GetValue(c.Spray), (c, v) => p.SetValue(c.Spray, v)));
        }
        return new ParameterSet("Spray", list);
    }

    /// <summary>
    /// The CPU water sampler (<see cref="Ocean.SampleWater(Vector3)"/>): how many swell and chop waves it sums, how much of
    /// each cascade's energy they capture, and what the last query cost. A quality/cost setting, not part of presets.
    /// </summary>
    static ParameterSet WaterSamplingSet(Ocean ocean)
    {
        string Energy(int cascade)
        {
            var s = ocean.WaterSampler;
            return cascade < s.CascadeCount ? $"{s.Count(cascade)} waves, {s.CapturedEnergy(cascade) * 100f:F0} % of the energy" : "-";
        }
        return new ParameterSet("Water sampling", new[]
        {
            Parameter.Int("water.swellWaves", "Swell waves", "", 0, () => ocean.SampledSwellWaves, v => ocean.SampledSwellWaves = v,
                0, 1024, ApplyMode.OnRelease),
            Parameter.Int("water.chopWaves", "Chop waves", "", 1, () => ocean.SampledChopWaves, v => ocean.SampledChopWaves = v,
                0, 256, ApplyMode.OnRelease),
            Parameter.Readout("water.swellEnergy", "Swell", "", 2, () => Energy(0)),
            Parameter.Readout("water.chopEnergy", "Chop", "", 3, () => Energy(1)),
            Parameter.Readout("water.lastQuery", "Last query", "", 4, () =>
                ocean.LastWaterQuery.Points == 0 ? "none yet" : $"{ocean.LastWaterQuery.Points} points, {ocean.LastWaterQuery.Milliseconds:F3} ms"),
        });
    }

    /// <summary>One parameter per hinted uniform of ocean.gdshader, grouped by its group_uniforms.</summary>
    static ParameterSet SurfaceSet(Ocean ocean)
    {
        var list = new List<Parameter>();
        string group = null;
        int order = 0;
        foreach (var entry in ocean.Material.Shader.GetShaderUniformList(true))
        {
            var u = entry.AsGodotDictionary();
            string name = (string)u["name"];
            var usage = (PropertyUsageFlags)(int)u["usage"];
            if (usage.HasFlag(PropertyUsageFlags.Group))
            {
                group = name.Length > 0 ? name : null;
                continue;
            }
            if (usage.HasFlag(PropertyUsageFlags.Subgroup) || group == null) continue;
            var type = (Variant.Type)(int)u["type"];
            var hint = (PropertyHint)(int)u["hint"];
            string hintString = (string)u["hint_string"];
            string id = "surface." + name, label = Label(name);
            if (type == Variant.Type.Float && hint == PropertyHint.Range)
            {
                var r = hintString.Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                list.Add(Parameter.Float(id, label, group, order++, () => SurfaceFloat(ocean, name),
                    v => Edit(ocean, c => c.Surface[name] = OceanConfigJson.Number(v)), r[0], r[1], r.Length > 2 ? r[2] : 0.01));
            }
            else if (type == Variant.Type.Color)
                list.Add(Parameter.Color(id, label, group, order++, () => SurfaceColor(ocean, name),
                    v => Edit(ocean, c => c.Surface[name] = OceanConfigJson.Color(v))));
            else if (type == Variant.Type.Int && hint == PropertyHint.Enum)
            {
                var options = hintString.Split(',');
                list.Add(Parameter.Enum(id, label, group, order++, options, () => options[Math.Clamp((int)SurfaceFloat(ocean, name), 0, options.Length - 1)],
                    v => Edit(ocean, c => c.Surface[name] = OceanConfigJson.Number(Array.IndexOf(options, v)))));
            }
        }
        return new ParameterSet("Surface", list);
    }

    static Variant SurfaceValue(Ocean ocean, string name)
    {
        var v = ocean.Material.GetShaderParameter(name);
        return v.VariantType != Variant.Type.Nil ? v : RenderingServer.ShaderGetParameterDefault(ocean.Material.Shader.GetRid(), name);
    }

    static float SurfaceFloat(Ocean ocean, string name) =>
        ocean.Config.Surface.TryGetValue(name, out var e) && OceanConfigJson.TryNumber(e, out float f) ? f : SurfaceValue(ocean, name).AsSingle();

    static Vector3N SurfaceColor(Ocean ocean, string name)
    {
        if (ocean.Config.Surface.TryGetValue(name, out var e) && OceanConfigJson.TryColor(e, out var c)) return c;
        var v = SurfaceValue(ocean, name);
        var col = v.VariantType == Variant.Type.Vector3 ? new Color(v.AsVector3().X, v.AsVector3().Y, v.AsVector3().Z) : v.AsColor();
        return new Vector3N(col.R, col.G, col.B);
    }

    static string Camel(string s) => char.ToLowerInvariant(s[0]) + s[1..];

    /// <summary>"WindSpeed" / "foam_trail_low" -> "Wind speed" / "Foam trail low".</summary>
    static string Label(string s)
    {
        var words = s.Contains('_') ? s.Split('_') : System.Text.RegularExpressions.Regex.Split(s, "(?<!^)(?=[A-Z])");
        var text = string.Join(" ", words).ToLowerInvariant();
        return char.ToUpperInvariant(text[0]) + text[1..];
    }
}
