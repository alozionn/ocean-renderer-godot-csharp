using Godot;
using OceanDemo.Environment;
using OceanNode = Ocean.Ocean;

namespace OceanDemo;

/// <summary>Wires the demo: the only place that knows both the ocean and the demo's own systems.</summary>
public partial class DemoRoot : Node
{
    [Export] public NodePath OceanPath { get; set; }
    [Export] public NodePath EnvironmentPath { get; set; }
    [Export] public NodePath SunPath { get; set; }
    [Export] public string StartSky { get; set; } = "evening_road";

    public OceanNode Ocean { get; private set; }
    public SkyRig Sky { get; private set; }

    public override void _Ready()
    {
        Ocean = GetNodeOrNull<OceanNode>(OceanPath);
        Sky = new SkyRig(GetNode<WorldEnvironment>(EnvironmentPath), GetNode<DirectionalLight3D>(SunPath), "res://demo/presets/skies.json");
        Sky.Apply(StartSky);
        GetNodeOrNull("/root/DebugMenu")?.Set("style", 1); // compact FPS readout
    }
}
