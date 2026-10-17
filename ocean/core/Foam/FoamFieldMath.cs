using System;

namespace Ocean.Core.Foam;

/// <summary>CPU side of the foam field: texel snapping, the camera shift, diffusion coefficients and a reference update.</summary>
public static class FoamFieldMath
{
    /// <summary>Explicit diffusion is stable for k up to 0.25; keep a margin.</summary>
    public const float MaxK = 0.24f;

    /// <summary>Texel index of the field's corner when it is centred on the camera, snapped to whole texels.</summary>
    public static (int X, int Z) OriginTexel(float cameraX, float cameraZ, float texel, int size) =>
        ((int)MathF.Round(cameraX / texel) - size / 2, (int)MathF.Round(cameraZ / texel) - size / 2);

    /// <summary>Integer move since the last tick; a first tick (or a jump past the field) shifts everything out.</summary>
    public static (int X, int Z) Shift((int X, int Z) previous, (int X, int Z) current, int size, bool first)
    {
        int sx = current.X - previous.X, sz = current.Z - previous.Z;
        if (first || Math.Abs(sx) >= size || Math.Abs(sz) >= size) return (size, size);
        return (sx, sz);
    }

    /// <summary>Across the streak: one-texel taps, k clamped for stability.</summary>
    public static float KAcross(float diffusion, float dt, float texel) => MathF.Min(diffusion * dt / (texel * texel), MaxK);

    /// <summary>Along the streak: widen the tap spacing (in texels) instead of clamping k.</summary>
    public static (float Step, float K) Along(float diffusion, float dt, float texel)
    {
        float step = MathF.Max(1f, MathF.Sqrt(diffusion * dt / MaxK) / texel);
        float k = diffusion * dt / (step * texel * step * texel);
        return (step, MathF.Min(k, MaxK));
    }

    /// <summary>LOD for sampling a cascade of the given patch size at the field's texel size.</summary>
    public static float CascadeLod(float fieldTexel, float patchSize, int resolution) =>
        MathF.Max(0f, MathF.Log2(fieldTexel / (patchSize / resolution)));

    /// <summary>One texel's update after spreading (foam_field.glsl step 5).</summary>
    public static (float Density, float Fresh) Step(float density, float fresh, float injected, float injectRate, float decay, float freshDecay, float dt)
    {
        float d = MathF.Min(density * MathF.Exp(-decay * dt) + injected * injectRate * dt, 1f);
        float f = MathF.Min(fresh * MathF.Exp(-freshDecay * dt) + injected * injectRate * 2f * dt, d);
        return (d, f);
    }
}
