using System;
using System.Collections.Generic;
using Godot;
using Ocean.Core.Config;
using Ocean.Core.Scheduling;
using Ocean.Simulation;
using Ocean.Surface;

namespace Ocean;

/// <summary>
/// The drop-in FFT ocean. Simulates three cascades in GLSL compute on the main RenderingDevice at a fixed tick rate
/// on staggered phases, and draws a camera-centred surface. Uses the active camera, the scene's light and environment.
/// </summary>
public partial class Ocean : Node3D
{
    [Export(PropertyHint.File, "*.json")] public string ConfigPath { get; set; } = "";

    /// <summary>The live config. Change it through <see cref="Apply"/> or <see cref="Parameters"/>.</summary>
    public OceanConfig Config => _config;
    public event Action ConfigChanged;

    OceanConfig _config = OceanConfig.Default;
    RenderingDevice _rd;
    OceanPipelines _pipelines;
    OceanCascade[] _cascades = Array.Empty<OceanCascade>();
    bool[] _spectrumDirty = Array.Empty<bool>();
    TickScheduler _scheduler;
    double _clock;
    MeshInstance3D _surface;
    ShaderMaterial _material;
    string _root;

    internal OceanPipelines Pipelines => _pipelines;
    internal IReadOnlyList<OceanCascade> Cascades => _cascades;
    internal ShaderMaterial Material => _material;
    /// <summary>Cascade ticks recorded since start, for readouts.</summary>
    internal long CascadeTicks { get; private set; }

    public OceanConfig Snapshot() => _config.Clone();

    public override void _Ready()
    {
        _root = ((Resource)GetScript()).ResourcePath.GetBaseDir() + "/";
        _surface = GetNode<MeshInstance3D>("Surface");
        _surface.Mesh = OceanMesh.Create();
        _material = (ShaderMaterial)_surface.MaterialOverride;
        _rd = RenderingServer.GetRenderingDevice();
        if (_rd == null)
        {
            GD.PushError("Ocean: needs a RenderingDevice renderer (Forward+ or Mobile).");
            SetProcess(false);
            return;
        }
        _pipelines = new OceanPipelines(_rd, _root);
        if (!_pipelines.Valid)
        {
            SetProcess(false);
            return;
        }
        if (!string.IsNullOrEmpty(ConfigPath)) _config = ReadConfig(ConfigPath) ?? _config;
        _scheduler = new TickScheduler(_config.Cascades.Length + 1, _config.SimulationRate);
        BuildCascades();
        ApplySurfaceOverrides(_config.Surface.Keys);
    }

    public override void _ExitTree()
    {
        FreeCascades();
        _pipelines?.Free();
        _pipelines = null;
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        Simulate();
        var camera = GetViewport().GetCamera3D();
        if (camera != null)
        {
            var p = camera.GlobalPosition;
            _surface.GlobalPosition = new Vector3(Mathf.Snapped(p.X, OceanMesh.SnapStep), GlobalPosition.Y, Mathf.Snapped(p.Z, OceanMesh.SnapStep));
        }
    }

    /// <summary>Records all work due this frame into one compute list, opened only if something is due.</summary>
    void Simulate()
    {
        long list = -1;
        for (int i = 0; i < _cascades.Length; i++)
        {
            if (!_scheduler.TryTick(i, _clock, _spectrumDirty[i], out double dt)) continue;
            if (list < 0) list = _rd.ComputeListBegin();
            var c = _config.Cascades[i];
            if (_spectrumDirty[i])
            {
                _cascades[i].RecordSpectrum(list, c);
                _spectrumDirty[i] = false;
            }
            _cascades[i].Time += dt * c.TimeScale;
            _cascades[i].RecordTick(list, c);
            CascadeTicks++;
        }
        if (list >= 0) _rd.ComputeListEnd();
    }

    public void Apply(OceanConfig config)
    {
        var next = config.Clone();
        var diff = OceanConfigDiff.Compare(_config, next);
        _config = next;
        if (_rd == null || _pipelines == null) return;
        if (diff.Resolution || diff.CascadeCount)
        {
            FreeCascades();
            _scheduler = new TickScheduler(_config.Cascades.Length + 1, _config.SimulationRate, _clock);
            BuildCascades();
        }
        else
        {
            foreach (int i in diff.Spectra) _spectrumDirty[i] = true;
            _scheduler.SetRate(_config.SimulationRate, _clock);
        }
        ApplySurfaceOverrides(diff.Surface);
        SetCascadeUniforms();
        ConfigChanged?.Invoke();
    }

    public void LoadConfig(string path)
    {
        var c = ReadConfig(path);
        if (c != null) Apply(c);
    }

    public void SaveConfig(string path)
    {
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (f == null) GD.PushError($"Ocean: cannot write {path}: {FileAccess.GetOpenError()}");
        else f.StoreString(OceanConfigJson.Serialize(_config));
    }

    static OceanConfig ReadConfig(string path)
    {
        if (!FileAccess.FileExists(path))
        {
            GD.PushError($"Ocean: config {path} not found.");
            return null;
        }
        try { return OceanConfigJson.Deserialize(FileAccess.GetFileAsString(path)); }
        catch (Exception e)
        {
            GD.PushError($"Ocean: config {path} is invalid: {e.Message}");
            return null;
        }
    }

    void BuildCascades()
    {
        _cascades = new OceanCascade[_config.Cascades.Length];
        _spectrumDirty = new bool[_cascades.Length];
        for (int i = 0; i < _cascades.Length; i++)
        {
            _cascades[i] = new OceanCascade(_pipelines, i, _config.Resolution);
            _spectrumDirty[i] = true;
        }
        for (int i = 0; i < Math.Min(_cascades.Length, SurfaceUniforms.Displacement.Length); i++)
        {
            _material.SetShaderParameter(SurfaceUniforms.Displacement[i], _cascades[i].DisplacementTexture);
            _material.SetShaderParameter(SurfaceUniforms.Slope[i], _cascades[i].SlopeTexture);
        }
        SetCascadeUniforms();
    }

    void FreeCascades()
    {
        foreach (var c in _cascades) c.Free();
        _cascades = Array.Empty<OceanCascade>();
    }

    void SetCascadeUniforms()
    {
        var size = new Vector3();
        for (int i = 0; i < Math.Min(3, _config.Cascades.Length); i++) size[i] = _config.Cascades[i].PatchSize;
        _material.SetShaderParameter(SurfaceUniforms.PatchSizes, size);
        _material.SetShaderParameter(SurfaceUniforms.TexelSizes, size / _config.Resolution);
    }

    /// <summary>Writes the named surface overrides to the material; names without an override go back to the shader default.</summary>
    void ApplySurfaceOverrides(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (!_config.Surface.TryGetValue(name, out var value))
                _material.SetShaderParameter(name, RenderingServer.ShaderGetParameterDefault(_material.Shader.GetRid(), name));
            else if (OceanConfigJson.TryNumber(value, out float f))
                _material.SetShaderParameter(name, f);
            else if (OceanConfigJson.TryColor(value, out var c))
                _material.SetShaderParameter(name, new Color(c.X, c.Y, c.Z));
        }
    }
}
