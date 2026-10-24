using System;
using System.Collections.Generic;
using Godot;
using Ocean.Core.Config;
using Ocean.Core.Mesh;
using Ocean.Core.Parameters;
using Ocean.Core.Scheduling;
using Ocean.Core.Shore;
using Ocean.Foam;
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
    /// <summary>Live knobs for UIs and tools; built in _Ready.</summary>
    public IReadOnlyList<ParameterSet> Parameters => _parameters;
    public event Action ConfigChanged;

    OceanConfig _config = OceanConfig.Default;
    List<ParameterSet> _parameters = new();
    double _rateClock;
    long _rateTicks;
    RenderingDevice _rd;
    OceanPipelines _pipelines;
    OceanCascade[] _cascades = Array.Empty<OceanCascade>();
    bool[] _spectrumDirty = Array.Empty<bool>();
    OceanFoamField _near, _far;
    TickScheduler _scheduler;
    double _clock;
    MeshInstance3D _surface;
    ShaderMaterial _material;
    GpuParticles3D _spray;
    ShaderMaterial _sprayMaterial;
    SeaFloor _seaFloor;
    ShoreTextures _shore;
    string _root;

    /// <summary>The ground under the water, if a terrain has set one (<see cref="SetSeaFloor"/>).</summary>
    public SeaFloor SeaFloor => _seaFloor;

    /// <summary>
    /// Tells the ocean what ground is under it, so waves calm in shallow water, enclosed lakes stay quiet, shore foam and
    /// breaking follow the coast and shallows tint. Build it from any terrain's height grid with
    /// <see cref="SeaFloor.FromHeights"/> (sea level = this node's world y). Outside its rectangle the sea is deep.
    /// </summary>
    public void SetSeaFloor(SeaFloor floor)
    {
        if (floor == null)
        {
            ClearSeaFloor();
            return;
        }
        _seaFloor = floor;
        UploadSeaFloor();
    }

    /// <summary>Back to open deep sea everywhere.</summary>
    public void ClearSeaFloor()
    {
        _seaFloor = null;
        UploadSeaFloor();
    }

    /// <summary>Hands the sea floor to the GPU (once the ocean is ready; until then it is only kept).</summary>
    void UploadSeaFloor()
    {
        _bodiesDirty = true;
        if (_shore == null) return;
        _shore.SetFloor(_seaFloor);
        SetShared(SurfaceUniforms.SeaFloorRect, _shore.FloorRect);
    }

    /// <summary>Sets a uniform both the surface and the spray read.</summary>
    void SetShared(string name, Variant value)
    {
        _material?.SetShaderParameter(name, value);
        _sprayMaterial?.SetShaderParameter(name, value);
    }

    internal OceanPipelines Pipelines => _pipelines;
    internal IReadOnlyList<OceanCascade> Cascades => _cascades;
    internal OceanFoamField NearField => _near;
    internal double Clock => _clock;
    internal ShaderMaterial Material => _material;
    /// <summary>Cascade ticks recorded since start, for readouts.</summary>
    internal long CascadeTicks { get; private set; }
    internal float CascadeTickRate { get; private set; }

    public OceanConfig Snapshot() => _config.Clone();

    /// <summary>Restarts the simulation: cascade time 0, foam fields emptied on their next tick.</summary>
    internal void Restart()
    {
        foreach (var c in _cascades) c.Time = 0;
        _near?.Reset();
        _far?.Reset();
        _spray?.Restart();
    }

    public override void _Ready()
    {
        _root = ((Resource)GetScript()).ResourcePath.GetBaseDir() + "/";
        _surface = GetNode<MeshInstance3D>("Surface");
        _surface.Mesh = OceanMesh.Create();
        _material = (ShaderMaterial)_surface.MaterialOverride;
        _material.SetShaderParameter(SurfaceUniforms.MeshSpacing, OceanMesh.SnapStep);
        _material.SetShaderParameter(SurfaceUniforms.MeshGrowth, RingLayout.Growth());
        _spray = GetNodeOrNull<GpuParticles3D>("Spray");
        _sprayMaterial = _spray?.ProcessMaterial as ShaderMaterial;
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
        _shore = new ShoreTextures(_rd, _pipelines.FoamShader);
        SetShared(SurfaceUniforms.SeaFloor, _shore.Floor);
        SetShared(SurfaceUniforms.BodyPages, _shore.Pages);
        SetShared(SurfaceUniforms.BodyIndex, _shore.Index);
        UploadSeaFloor();
        _near = new OceanFoamField(_pipelines, 1024, 256f);
        _far = new OceanFoamField(_pipelines, 1024, 2048f);
        _scheduler = new TickScheduler(_config.Cascades.Length + 1, _config.SimulationRate);
        BuildCascades();
        SetFoamDetail();
        ApplySurfaceOverrides(_config.Surface.Keys);
        ApplySpray();
        ConfigureWater();
        _parameters = OceanParameters.Build(this);
    }

    public override void _ExitTree()
    {
        _near?.Free();
        _shore?.Free();
        _far?.Free();
        FreeCascades();
        _pipelines?.Free();
        _pipelines = null;
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        Simulate();
        if (_clock - _rateClock >= 1.0)
        {
            CascadeTickRate = (float)((CascadeTicks - _rateTicks) / (_clock - _rateClock));
            _rateClock = _clock;
            _rateTicks = CascadeTicks;
        }
        var camera = GetViewport().GetCamera3D();
        if (camera != null)
        {
            var p = camera.GlobalPosition;
            _surface.GlobalPosition = new Vector3(Mathf.Snapped(p.X, OceanMesh.SnapStep), GlobalPosition.Y, Mathf.Snapped(p.Z, OceanMesh.SnapStep));
            if (_spray != null)
            {
                // Particles live in world space; the emitter only follows the camera so its bounds stay around it.
                _spray.GlobalPosition = new Vector3(p.X, GlobalPosition.Y, p.Z);
                _sprayMaterial?.SetShaderParameter(SurfaceUniforms.CameraXz, new Vector2(p.X, p.Z));
            }
        }
        FoldBodies();
        _sprayMaterial?.SetShaderParameter(SurfaceUniforms.SeaLevel, GlobalPosition.Y);
    }

    /// <summary>Records all work due this frame into one compute list, opened only if something is due.</summary>
    void Simulate()
    {
        long list = -1;
        var shore = _shore.Flush(); // buffer updates must happen outside the compute list
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
        if (_near != null && _scheduler.TryTick(_cascades.Length, _clock, false, out double foamDt))
        {
            if (list < 0) list = _rd.ComputeListBegin();
            var camera = GetViewport().GetCamera3D();
            var p = camera?.GlobalPosition ?? GlobalPosition;
            var xz = new Vector2(p.X, p.Z);
            _near.Record(list, xz, (float)foamDt, _config, shore);
            _far.Record(list, xz, (float)foamDt, _config, shore);
            SetShared(SurfaceUniforms.FoamNear, _near.Texture);
            _material.SetShaderParameter(SurfaceUniforms.FoamFar, _far.Texture);
            SetShared(SurfaceUniforms.FoamNearRect, _near.Rect);
            _material.SetShaderParameter(SurfaceUniforms.FoamFarRect, _far.Rect);
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
        if (diff.FoamTexture) SetFoamDetail();
        ApplySurfaceOverrides(diff.Surface);
        ApplySpray();
        ConfigureWater();
        SetCascadeUniforms();
        foreach (var set in _parameters)
            foreach (var p in set.Parameters) p.Refresh();
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
            SetShared(SurfaceUniforms.Displacement[i], _cascades[i].DisplacementTexture);
            _material.SetShaderParameter(SurfaceUniforms.Slope[i], _cascades[i].SlopeTexture);
        }
        _near?.Bind(_cascades);
        _far?.Bind(_cascades);
        SetCascadeUniforms();
    }

    void SetFoamDetail()
    {
        var texture = FoamDetailTexture.Load(_root, _config.FoamTexture);
        _material.SetShaderParameter(SurfaceUniforms.FoamDetail, texture);
        _material.SetShaderParameter(SurfaceUniforms.FoamDetailSoft, texture);
    }

    void ApplySpray()
    {
        if (_spray == null || _sprayMaterial == null) return;
        var s = _config.Spray;
        _spray.Emitting = s.Amount >= 1f && s.Opacity > 0f;
        int amount = Math.Max(1, (int)s.Amount);
        if (_spray.Amount != amount) _spray.Amount = amount;
        if (!Mathf.IsEqualApprox((float)_spray.Lifetime, s.Lifetime)) _spray.Lifetime = s.Lifetime;
        _sprayMaterial.SetShaderParameter("spray_radius", s.Radius);
        _sprayMaterial.SetShaderParameter("spray_threshold", s.Threshold);
        _sprayMaterial.SetShaderParameter("spray_speed", s.Speed);
        _sprayMaterial.SetShaderParameter("spray_wind", s.Wind);
        _sprayMaterial.SetShaderParameter("spray_gravity", s.Gravity);
        _sprayMaterial.SetShaderParameter("spray_size", s.Size);
        _sprayMaterial.SetShaderParameter("spray_opacity", s.Opacity);
        _sprayMaterial.SetShaderParameter("spray_shore", s.Shore);
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
        SetShared(SurfaceUniforms.PatchSizes, size);
        _material.SetShaderParameter(SurfaceUniforms.TexelSizes, size / _config.Resolution);
        _material.SetShaderParameter(SurfaceUniforms.StreakAngle, Mathf.DegToRad(_config.Cascades[0].WindDirection + _config.Foam.StreakAngleOffset));
        float wind = Mathf.DegToRad(_config.Cascades[0].WindDirection);
        _sprayMaterial?.SetShaderParameter(SurfaceUniforms.WindDir, new Vector2(Mathf.Cos(wind), Mathf.Sin(wind)));
        _bodiesDirty = true; // the lee follows the swell
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
        if (_sprayMaterial == null) return;
        foreach (var name in SurfaceUniforms.ShoreShared)
        {
            var v = _material.GetShaderParameter(name);
            _sprayMaterial.SetShaderParameter(name, v.VariantType != Variant.Type.Nil ? v : RenderingServer.ShaderGetParameterDefault(_material.Shader.GetRid(), name));
        }
    }
}
