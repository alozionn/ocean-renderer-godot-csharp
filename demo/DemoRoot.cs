using System.Collections.Generic;
using System.Linq;
using Godot;
using Ocean.Core.Parameters;
using OceanDemo.Environment;
using OceanDemo.UI;
using OceanNode = Ocean.Ocean;

namespace OceanDemo;

/// <summary>
/// Wires the demo: the only place that knows both the ocean and the demo's own systems. Hands the ocean's parameter
/// sets plus the demo's Presets set to the ocean panel (F1), and the Environment set to the environment panel (F2).
/// </summary>
public partial class DemoRoot : Node
{
    const string PresetDir = "res://demo/presets/";
    const string CustomPreset = "user://ocean_custom.json";

    [Export] public NodePath OceanPath { get; set; }
    [Export] public NodePath EnvironmentPath { get; set; }
    [Export] public NodePath SunPath { get; set; }
    [Export] public NodePath OceanPanelPath { get; set; }
    [Export] public NodePath EnvironmentPanelPath { get; set; }
    [Export] public string StartSky { get; set; } = "evening_road";

    public OceanNode Ocean { get; private set; }
    public SkyRig Sky { get; private set; }
    /// <summary>Every parameter set shown by the demo (ocean and demo sets), for tools.</summary>
    public IReadOnlyList<ParameterSet> AllSets { get; private set; } = new List<ParameterSet>();

    public override void _Ready()
    {
        Ocean = GetNodeOrNull<OceanNode>(OceanPath ?? new NodePath());
        var env = GetNode<WorldEnvironment>(EnvironmentPath);
        Sky = new SkyRig(env, GetNode<DirectionalLight3D>(SunPath), PresetDir + "skies.json");
        Sky.Apply(StartSky);
        GetNodeOrNull("/root/DebugMenu")?.Set("style", 1); // compact FPS readout

        var oceanSets = Ocean == null ? new List<ParameterSet>() : new List<ParameterSet> { PresetsSet() }.Concat(Ocean.Parameters).ToList();
        var envSets = new List<ParameterSet> { EnvironmentSet(env.Environment) };
        AllSets = oceanSets.Concat(envSets).ToList();
        GetNodeOrNull<ParameterPanel>(OceanPanelPath ?? new NodePath())?.Show(oceanSets);
        GetNodeOrNull<ParameterPanel>(EnvironmentPanelPath ?? new NodePath())?.Show(envSets);
    }

    ParameterSet PresetsSet()
    {
        var names = DirAccess.GetFilesAt(PresetDir).Where(f => f.StartsWith("ocean_") && f.EndsWith(".json"))
            .Select(f => f["ocean_".Length..^".json".Length]).Append("custom").ToArray();
        string selected = names.Contains("calm") ? "calm" : names[0];
        string Path(string name) => name == "custom" ? CustomPreset : $"{PresetDir}ocean_{name}.json";
        return new ParameterSet("Presets", new[]
        {
            Parameter.Enum("presets.preset", "Preset", "", 0, names, () => selected, v => selected = v),
            Parameter.Action("presets.load", "Load preset", "", 1, () => Ocean.LoadConfig(Path(selected))),
            Parameter.Action("presets.save", $"Save to {CustomPreset}", "", 2, () => Ocean.SaveConfig(CustomPreset)),
        });
    }

    ParameterSet EnvironmentSet(Godot.Environment environment)
    {
        var list = new List<Parameter>();
        void Changed() => list.ForEach(p => p.Refresh());
        list.Add(Parameter.Enum("env.sky", "Sky", "", 0, Sky.Presets.Select(p => p.Id).ToArray(), () => Sky.Current.Id, v =>
        {
            Sky.Apply(v);
            Changed();
        }));
        list.Add(Parameter.Float("env.skyYaw", "Sky yaw", "", 1, () => Sky.Yaw, v => Sky.SetYaw(v), -180, 180, 0.5));
        list.Add(Parameter.Float("env.sunElevation", "Sun elevation", "", 2, () => Sky.SunElevation, v => Sky.SetSunElevation(v), 0, 90, 0.1));
        list.Add(Parameter.Float("env.skyEnergy", "Sky energy", "", 3, () => Sky.SkyEnergy, v => Sky.SetSkyEnergy(v), 0, 4, 0.01));
        list.Add(Parameter.Float("env.sunEnergy", "Sun energy", "", 4, () => Sky.SunEnergy, v => Sky.SetSunEnergy(v), 0, 8, 0.01));
        list.Add(Parameter.Bool("env.glow", "Glow", "", 5, () => environment.GlowEnabled, v => environment.GlowEnabled = v));
        return new ParameterSet("Environment", list);
    }
}
