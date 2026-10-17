using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using OceanDemo;
using OceanNode = Ocean.Ocean;

namespace OceanDev;

/// <summary>
/// Entry point of dev/dev_main.tscn. Parses the user args after `--` and runs the requested tool:
/// --cam, --sky, --preset, --set, --capture, --tag, --burst, --bench, --selftest, --foam-stats, --prepare-assets.
/// </summary>
public partial class DevHarness : Node
{
    readonly Dictionary<string, string> _args = new();
    OceanNode _ocean;
    DemoRoot _demo;
    Camera3D _camera;

    public override void _Ready()
    {
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            var kv = a.TrimStart('-').Split('=', 2);
            _args[kv[0]] = kv.Length > 1 ? (_args.TryGetValue(kv[0], out var prev) ? prev + ";" + kv[1] : kv[1]) : "";
        }
        if (_args.ContainsKey("prepare-assets"))
        {
            AssetPrep.Run();
            GetTree().Quit();
            return;
        }
        _ocean = Find<OceanNode>(GetTree().Root);
        _camera = GetViewport().GetCamera3D();
        if (_camera != null) _camera.SetProcessInput(false);
        if (_args.TryGetValue("cam", out var cam)) Captures.Place(_camera, ParsePose(cam));
        _demo = Find<DemoRoot>(GetTree().Root);
        if (_args.TryGetValue("sky", out var sky)) _demo.Sky.Apply(sky);
        if (_args.TryGetValue("preset", out var preset))
            _ocean.LoadConfig(preset.Contains('/') ? preset : $"res://demo/presets/{preset.TrimSuffix(".json")}.json");
        _ = RunAsync();
    }

    static T Find<T>(Node n) where T : Node => n as T ?? n.GetChildren().Select(Find<T>).FirstOrDefault(c => c != null);

    static float[] ParsePose(string s) => s.Split(',').Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();

    async Task RunAsync()
    {
        int exit = 0;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        try
        {
            if (_args.ContainsKey("selftest")) exit = GpuChecks.Run(_ocean.Pipelines) ? 0 : 1;
            else if (_args.TryGetValue("capture", out var poses)) await Capture(poses.Split(','));
            else if (_args.TryGetValue("burst", out var n)) await Burst(int.Parse(n));
            else if (_args.TryGetValue("bench", out var secs)) await Bench(double.Parse(secs, System.Globalization.CultureInfo.InvariantCulture));
            else return;
        }
        catch (Exception e)
        {
            GD.PushError(e.ToString());
            exit = 1;
        }
        GetTree().Quit(exit);
    }

    string Tag => _args.TryGetValue("tag", out var t) && t.Length > 0 ? t : "cap";

    async Task Wait(double seconds)
    {
        double end = Time.GetTicksMsec() / 1000.0 + seconds;
        while (Time.GetTicksMsec() / 1000.0 < end) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>n consecutive frames, centre-cropped to 640x360, plus a strip: the flicker check.</summary>
    async Task Burst(int n)
    {
        await Wait(3.0);
        var frames = new List<Image>();
        for (int i = 0; i < n; i++)
        {
            var img = await Captures.Grab(this);
            frames.Add(img.GetRegion(new Rect2I((img.GetWidth() - 640) / 2, (img.GetHeight() - 360) / 2, 640, 360)));
        }
        for (int i = 0; i < n; i++) Captures.Save(frames[i], $"{Tag}_burst_{i:D2}");
        Captures.Save(Captures.Sheet(frames, 4, 640), $"{Tag}_burst_strip");
    }

    /// <summary>Average and 1 %-low frame time after a 3 s warm-up, at the current pose.</summary>
    async Task Bench(double seconds)
    {
        await Wait(3.0);
        var times = new List<double>();
        ulong last = Time.GetTicksUsec(), end = last + (ulong)(seconds * 1e6);
        while (last < end)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ulong now = Time.GetTicksUsec();
            times.Add((now - last) / 1000.0);
            last = now;
        }
        times.Sort();
        double avg = times.Average(), low = times.Skip((int)(times.Count * 0.99)).Average();
        var size = GetViewport().GetVisibleRect().Size;
        GD.Print(FormattableString.Invariant($"bench {Tag}: {size.X}x{size.Y} res {_ocean.Config.Resolution} frames {times.Count} avg {avg:F3} ms ({1000 / avg:F0} fps) 1%-low {low:F3} ms ({1000 / low:F0} fps)"));
    }

    async Task Capture(IEnumerable<string> poses)
    {
        var images = new List<Image>();
        foreach (var pose in poses)
        {
            if (!Captures.Poses.TryGetValue(pose, out var p)) { GD.PushError($"unknown pose {pose}"); continue; }
            Captures.Place(_camera, p);
            await Wait(3.0);
            var img = await Captures.Grab(this);
            Captures.Save(img, $"{Tag}_{pose}");
            images.Add(img);
        }
        if (images.Count > 1) Captures.Save(Captures.Sheet(images, 2, 960), $"{Tag}_sheet");
    }
}
