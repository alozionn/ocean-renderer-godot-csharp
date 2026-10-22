using System.Collections.Generic;
using System.Linq;
using Godot;
using Ocean.Core.Parameters;
using OceanDemo.Environment;
using OceanDemo.Props;
using OceanDemo.UI;
using OceanNode = Ocean.Ocean;
using SeaFloor = Ocean.Core.Shore.SeaFloor;

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
    [Export] public NodePath IslandPath { get; set; }
    [Export] public NodePath OceanPanelPath { get; set; }
    [Export] public NodePath EnvironmentPanelPath { get; set; }
    [Export] public NodePath CameraPath { get; set; }
    [Export] public NodePath BoatPath { get; set; }
    [Export] public NodePath BoatCameraPath { get; set; }
    [Export] public string StartSky { get; set; } = "evening_road";
    [Export] public bool StartInBoat { get; set; }
    [Export] public Key BoatToggleKey { get; set; } = Key.C;

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

        // The island tells the ocean what ground is under it, on every (re)generation.
        if (Ocean != null && GetNodeOrNull<IslandTerrain>(IslandPath ?? new NodePath()) is { } island)
        {
            void PushSeaFloor() => Ocean.SetSeaFloor(SeaFloor.FromHeights(island.WorldHeights, island.Samples,
                island.Samples, island.CellSize, new System.Numerics.Vector2(island.SampleOrigin.X, island.SampleOrigin.Y), Ocean.GlobalPosition.Y));
            island.Generated += PushSeaFloor;
            if (island.WorldHeights.Length > 0) PushSeaFloor();
        }

        _godCamera = GetNodeOrNull<Camera3D>(CameraPath ?? new NodePath());
        _boat = GetNodeOrNull<Boat.Boat>(BoatPath ?? new NodePath());
        _boatCamera = GetNodeOrNull<Boat.BoatCamera>(BoatCameraPath ?? new NodePath());
        if (StartInBoat) SetBoatMode(true);
    }

    Camera3D _godCamera;
    Boat.Boat _boat;
    Boat.BoatCamera _boatCamera;

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false } key && key.Keycode == BoatToggleKey && _boatCamera != null)
            SetBoatMode(!_boatCamera.Current);
    }

    /// <summary>Boat mode: WASD steers the boat and the chase camera follows it. God view: the free-fly camera.</summary>
    public void SetBoatMode(bool on)
    {
        if (_boat == null || _boatCamera == null || _godCamera == null) return;
        _boat.Controlled = on;
        _godCamera.ProcessMode = on ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        if (on)
        {
            _boatCamera.MakeCurrent();
            _boatCamera.Snap();
        }
        else _godCamera.MakeCurrent();
    }

    ParameterSet PresetsSet()
    {
        var names = DirAccess.GetFilesAt(PresetDir).Where(f => f.StartsWith("ocean_") && f.EndsWith(".json"))
            .Select(f => f["ocean_".Length..^".json".Length]).Append("custom").ToArray();
        string selected = names.FirstOrDefault(n => Ocean.ConfigPath.EndsWith($"ocean_{n}.json")) ?? names[0];
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
        if (GetNodeOrNull<IslandTerrain>(IslandPath ?? new NodePath()) is { } island)
        {
            list.Add(Parameter.Int("env.islandSeed", "Island seed", "", 6, () => island.Seed, v => island.Generate(v), 0, 99999, ApplyMode.OnRelease));
            list.Add(Parameter.Action("env.islandReroll", "New island", "", 7, () =>
            {
                island.Reroll();
                Changed();
            }));
        }
        return new ParameterSet("Environment", list);
    }
}
