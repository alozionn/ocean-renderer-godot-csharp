using System;
using System.Collections.Generic;
using Godot;
using Ocean.Core.Bodies;
using Ocean.Core.Shore;
using Ocean.Surface;

namespace Ocean;

// Bodies in the water: one list every water effect reads. Stationary bodies fold into the shore data (BodyLayer: fine
// pages over the sea floor), so a rock, a post or a moored hull gets the islands' contact foam, breaking and calm
// lee. Rebuilt at most once a frame, only when the stationary set or the swell direction changed; the terrain's sea
// floor is sampled, never rebuilt.
public partial class Ocean
{
    readonly List<WaterBody> _bodies = new();
    BodyLayer _bodyLayer;
    bool _bodiesDirty;
    int _bodySignature;

    /// <summary>Everything registered with <see cref="AddBody"/>: rocks, posts, pontoons, hulls.</summary>
    public IReadOnlyList<WaterBody> Bodies => _bodies;
    /// <summary>The stationary bodies folded over the sea floor, as the shader reads them (null: none).</summary>
    public BodyLayer BodyLayer => _bodyLayer;

    /// <summary>Registers a body. Stationary ones join the shore effects from the next frame.</summary>
    public void AddBody(WaterBody body)
    {
        if (body == null || _bodies.Contains(body)) return;
        _bodies.Add(body);
        _bodiesDirty = true;
    }

    public void RemoveBody(WaterBody body)
    {
        if (_bodies.Remove(body)) _bodiesDirty = true;
    }

    public void ClearBodies()
    {
        _bodies.Clear();
        _bodiesDirty = true;
    }

    /// <summary>
    /// Call after changing registered bodies (moved, resized, made stationary or not). Refolds on the next frame only if
    /// a stationary body actually changed, so updating moving bodies every frame costs nothing here.
    /// </summary>
    public void UpdateBodies() => _bodiesDirty = true;

    /// <summary>Refolds the stationary bodies if anything they depend on changed; uploads the pages.</summary>
    void FoldBodies()
    {
        if (!_bodiesDirty) return;
        _bodiesDirty = false;
        var wave = WaveDirection();
        int signature = BodySignature(wave);
        if (signature == _bodySignature) return;
        _bodySignature = signature;
        _bodyLayer = BodyLayer.Build(_seaFloor, _bodies, new System.Numerics.Vector2(wave.X, wave.Y));
        _waves.Bodies = _bodyLayer;
        if (_shore == null) return;
        _shore.SetBodies(_bodyLayer);
        SetShared(SurfaceUniforms.BodySamples, _shore.BodySamples);
        SetShared(SurfaceUniforms.BodyRect, _shore.BodyRect);
    }

    /// <summary>
    /// The way the swell travels in world xz. Cascade 0's spectrum puts its energy at wave vectors k along its wind
    /// direction, and each component is cos(k·p + ωt + φ) (Core.Sampling.WaveSampler), so crests move along −k.
    /// </summary>
    Vector2 WaveDirection()
    {
        float wind = _config.Cascades.Length > 0 ? Mathf.DegToRad(_config.Cascades[0].WindDirection) : 0f;
        return -new Vector2(Mathf.Cos(wind), Mathf.Sin(wind));
    }

    /// <summary>A hash of everything a fold depends on: the stationary bodies, the floor and the swell direction.</summary>
    int BodySignature(Vector2 wave)
    {
        var h = new HashCode();
        h.Add(wave);
        h.Add(_seaFloor);
        foreach (var b in _bodies)
        {
            if (!b.Stationary) continue;
            h.Add(b.Position);
            h.Add(b.Heading);
            h.Add(b.Length);
            h.Add(b.Beam);
            h.Add(b.Rounding);
            h.Add(b.Top);
            h.Add(b.Draft);
            h.Add(b.Skirt);
            h.Add(b.Reach);
            h.Add(b.Lee);
            h.Add(b.LeeLength);
        }
        return h.ToHashCode();
    }
}
