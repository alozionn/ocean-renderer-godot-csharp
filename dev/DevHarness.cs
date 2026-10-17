using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using OceanDemo;
using Ocean.Core.Parameters;
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
        if (_args.TryGetValue("set", out var sets))
            foreach (var s in sets.Split(';')) SetParameter(s);
        if (_args.ContainsKey("panels")) ShowPanels(GetTree().Root);
        _ = RunAsync();
    }

    static T Find<T>(Node n) where T : Node => n as T ?? n.GetChildren().Select(Find<T>).FirstOrDefault(c => c != null);

    /// <summary>--set=<id>=<value>: sets any parameter the demo shows, parsed by its kind.</summary>
    void SetParameter(string assignment)
    {
        var kv = assignment.Split('=', 2);
        var p = _demo.AllSets.SelectMany(s => s.Parameters).FirstOrDefault(x => x.Id == kv[0]);
        if (p == null || (kv.Length < 2 && p.Kind != ParameterKind.Action))
        {
            GD.PushError($"--set: unknown parameter {kv[0]}");
            return;
        }
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string v = kv.Length > 1 ? kv[1] : "";
        switch (p.Kind)
        {
            case ParameterKind.Float: p.Set(float.Parse(v, inv)); break;
            case ParameterKind.Int: p.Set(int.Parse(v, inv)); break;
            case ParameterKind.Bool: p.Set(bool.Parse(v)); break;
            case ParameterKind.Enum: p.Set(v); break;
            case ParameterKind.Color:
                var c = v.StartsWith('#') ? Color.FromHtml(v) : new Color(ParsePose(v)[0], ParsePose(v)[1], ParsePose(v)[2]);
                p.Set(new System.Numerics.Vector3(c.R, c.G, c.B));
                break;
            case ParameterKind.Action: p.Invoke(); break;
        }
        GD.Print($"set {p.Id} = {p.Value}");
    }

    /// <summary>--panels: shows every parameter panel with its sections unfolded, for UI captures.</summary>
    static void ShowPanels(Node n)
    {
        if (n is OceanDemo.UI.ParameterPanel panel) panel.Visible = true;
        if (n is FoldableContainer fold) fold.Folded = false;
        foreach (var c in n.GetChildren()) ShowPanels(c);
    }

    static float[] ParsePose(string s) => s.Split(',').Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();

    async Task RunAsync()
    {
        int exit = 0;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        try
        {
            if (_args.ContainsKey("selftest")) exit = GpuChecks.Run(_ocean.Pipelines) & await FoamTargets() ? 0 : 1;
            else if (_args.ContainsKey("foam-stats")) await FoamTargets();
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

    /// <summary>Foam field targets (section 9.6) after 8 s at the hero pose, at 60 Hz and at 120 Hz.</summary>
    async Task<bool> FoamTargets()
    {
        if (!_args.ContainsKey("cam")) Captures.Place(_camera, Captures.Poses["hero"]);
        float angle = Mathf.DegToRad(_ocean.Config.Cascades[0].WindDirection + _ocean.Config.Foam.StreakAngleOffset);
        async Task<FoamMeasure> At(float rate)
        {
            var c = _ocean.Snapshot();
            c.SimulationRate = rate;
            _ocean.Apply(c);
            _ocean.Restart();
            await Wait(8.0);
            var (caps, trails, window) = FoamStats.Read(_ocean.NearField);
            var snapshots = new List<float[,]> { window };
            for (int i = 0; i < 3; i++)
            {
                await Wait(1.0);
                snapshots.Add(FoamStats.Read(_ocean.NearField).Density);
            }
            var m = new FoamMeasure(caps, trails, FoamStats.Anisotropy(snapshots, angle));
            GD.Print($"foam at {rate} Hz: {m}");
            return m;
        }
        float rate0 = _ocean.Config.SimulationRate;
        var a = await At(60);
        var b = await At(120);
        await At(rate0);
        double rel = Math.Abs(b.Trails - a.Trails) / Math.Max(a.Trails, 1e-9);
        bool ok = GpuChecks.Report("foam whitecaps 0.3-3 %", (a.Whitecaps >= 0.003 && a.Whitecaps <= 0.03, $"{a.Whitecaps * 100:F2} %"));
        ok &= GpuChecks.Report("foam trails 3-15 %", (a.Trails >= 0.03 && a.Trails <= 0.15, $"{a.Trails * 100:F2} %"));
        ok &= GpuChecks.Report("foam 60 vs 120 Hz within 15 %", (rel <= 0.15, $"{rel * 100:F1} % relative"));
        ok &= GpuChecks.Report("foam streak anisotropy >= 1.25", (a.Anisotropy >= 1.25, $"{a.Anisotropy:F2}"));
        return ok;
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
