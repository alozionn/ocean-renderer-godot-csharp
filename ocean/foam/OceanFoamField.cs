using System.Collections.Generic;
using Godot;
using Ocean.Core.Config;
using Ocean.Core.Foam;
using Ocean.Core.Gpu;
using Ocean.Simulation;

namespace Ocean.Foam;

/// <summary>
/// A camera-centred foam field (RG16F: density, freshness), snapped to whole texels, ping-ponging between two textures.
/// Fed by the Jacobian of the summed cascades and by the shore (crests meeting waterlines, breaking in the shallows).
/// </summary>
internal sealed class OceanFoamField
{
    readonly RenderingDevice _rd;
    readonly OceanPipelines _p;
    readonly Rid[] _tex = new Rid[2];
    readonly Rid[] _sets = new Rid[2];
    readonly Texture2Drd[] _textures = { new(), new() };
    readonly byte[] _push = PushConstants.FoamField.CreateBuffer();
    Rid _historySampler, _partialsSampler;
    (int X, int Z) _originTexel;
    bool _first = true;
    int _current;

    public readonly int Size;
    public readonly float Extent;
    public float Texel => Extent / Size;
    /// <summary>The latest field and the world rect (origin xz, extent) it covers.</summary>
    public Texture2Drd Texture => _textures[_current];
    public Vector4 Rect => new(_originTexel.X * Texel, _originTexel.Z * Texel, Extent, 1f / Extent);
    internal Rid CurrentRid => _tex[_current];

    public OceanFoamField(OceanPipelines pipelines, int size, float extent)
    {
        _rd = pipelines.Rd;
        _p = pipelines;
        Size = size;
        Extent = extent;
        for (int i = 0; i < 2; i++)
        {
            _tex[i] = RdResources.CreateTexture(_rd, size, size, RenderingDevice.DataFormat.R16G16Sfloat, 1,
                RdResources.Sampled | RenderingDevice.TextureUsageBits.CanCopyToBit);
            _rd.TextureClear(_tex[i], new Color(0, 0, 0, 0), 0, 1, 0, 1);
            _textures[i].TextureRdRid = _tex[i];
        }
        _historySampler = _rd.SamplerCreate(new RDSamplerState
        {
            MagFilter = RenderingDevice.SamplerFilter.Linear,
            MinFilter = RenderingDevice.SamplerFilter.Linear,
            RepeatU = RenderingDevice.SamplerRepeatMode.ClampToBorder,
            RepeatV = RenderingDevice.SamplerRepeatMode.ClampToBorder,
            BorderColor = RenderingDevice.SamplerBorderColor.FloatTransparentBlack,
        });
        _partialsSampler = _rd.SamplerCreate(new RDSamplerState
        {
            MagFilter = RenderingDevice.SamplerFilter.Linear,
            MinFilter = RenderingDevice.SamplerFilter.Linear,
            MipFilter = RenderingDevice.SamplerFilter.Linear,
            RepeatU = RenderingDevice.SamplerRepeatMode.Repeat,
            RepeatV = RenderingDevice.SamplerRepeatMode.Repeat,
        });
        PushConstants.FoamField.Set(_push, "size", size);
        PushConstants.FoamField.Set(_push, "texel_size", Texel);
    }

    /// <summary>(Re)creates the uniform sets against the cascades' partials; call whenever the cascades are rebuilt.</summary>
    public void Bind(IReadOnlyList<OceanCascade> cascades)
    {
        FreeSets();
        for (int i = 0; i < 2; i++)
        {
            var uniforms = new Godot.Collections.Array<RDUniform>
            {
                RdResources.Sampled2D(0, _historySampler, _tex[i]),
                RdResources.Image(1, _tex[1 - i]),
            };
            for (int c = 0; c < 3; c++) uniforms.Add(RdResources.Sampled2D(2 + c, _partialsSampler, cascades[System.Math.Min(c, cascades.Count - 1)].Partials));
            for (int c = 0; c < 3; c++) uniforms.Add(RdResources.Sampled2D(5 + c, _partialsSampler, cascades[System.Math.Min(c, cascades.Count - 1)].Displacement));
            _sets[i] = _rd.UniformSetCreate(uniforms, _p.FoamShader, 0);
        }
    }

    /// <param name="shore">Uniform set 1: the shore data and foam settings (Surface.ShoreTextures).</param>
    public void Record(long list, Vector2 camera, float dt, OceanConfig config, Rid shore)
    {
        var origin = FoamFieldMath.OriginTexel(camera.X, camera.Y, Texel, Size);
        var shift = FoamFieldMath.Shift(_originTexel, origin, Size, _first);
        _first = false;
        _originTexel = origin;

        var f = config.Foam;
        var l = PushConstants.FoamField;
        float angle = Mathf.DegToRad(config.Cascades[0].WindDirection + f.StreakAngleOffset);
        var (step, kAlong) = FoamFieldMath.Along(f.DiffuseAlong, dt, Texel);
        l.Set(_push, "origin", origin.X * Texel, origin.Z * Texel);
        l.Set(_push, "dt", dt);
        l.Set(_push, "shift", shift.X, shift.Z);
        l.Set(_push, "bias", f.Bias);
        l.Set(_push, "gain", f.Gain);
        l.Set(_push, "inject_rate", f.InjectRate);
        l.Set(_push, "decay", f.Decay);
        l.Set(_push, "fresh_decay", f.FreshDecay);
        l.Set(_push, "streak", Mathf.Cos(angle) * step, Mathf.Sin(angle) * step);
        l.Set(_push, "k_along", kAlong);
        l.Set(_push, "k_across", FoamFieldMath.KAcross(f.DiffuseAcross, dt, Texel));
        var c = config.Cascades;
        float P(int i) => c[System.Math.Min(i, c.Length - 1)].PatchSize;
        l.Set(_push, "patch_size", P(0), P(1), P(2), 1f);
        l.Set(_push, "weight", f.WeightSwell, c.Length > 1 ? f.WeightChop : 0f, c.Length > 2 ? f.WeightRipples : 0f, 0f);
        l.Set(_push, "lod", FoamFieldMath.CascadeLod(Texel, P(0), config.Resolution), FoamFieldMath.CascadeLod(Texel, P(1), config.Resolution),
            FoamFieldMath.CascadeLod(Texel, P(2), config.Resolution), 0f);

        _rd.ComputeListBindComputePipeline(list, _p.Foam);
        _rd.ComputeListBindUniformSet(list, _sets[_current], 0);
        _rd.ComputeListBindUniformSet(list, shore, 1);
        _rd.ComputeListSetPushConstant(list, _push, (uint)_push.Length);
        uint g = RdResources.Groups(Size, 8);
        _rd.ComputeListDispatch(list, g, g, 1);
        _rd.ComputeListAddBarrier(list);
        _current = 1 - _current;
    }

    /// <summary>Starts over: the next tick shifts the old field out.</summary>
    public void Reset() => _first = true;

    void FreeSets()
    {
        for (int i = 0; i < 2; i++)
            if (_sets[i].IsValid && _rd.UniformSetIsValid(_sets[i])) RdResources.Free(_rd, ref _sets[i]);
            else _sets[i] = default;
    }

    public void Free()
    {
        foreach (var t in _textures) t.TextureRdRid = default;
        FreeSets();
        RdResources.Free(_rd, ref _historySampler);
        RdResources.Free(_rd, ref _partialsSampler);
        RdResources.Free(_rd, ref _tex[0]);
        RdResources.Free(_rd, ref _tex[1]);
    }
}
