using System;
using System.Linq;
using Godot;

namespace OceanDev;

/// <summary>Entry point of dev/dev_main.tscn: parses the user args after `--` and runs the requested tool.</summary>
public partial class DevHarness : Node
{
    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        if (args.Contains("--prepare-assets"))
        {
            AssetPrep.Run();
            GetTree().Quit();
        }
    }
}
