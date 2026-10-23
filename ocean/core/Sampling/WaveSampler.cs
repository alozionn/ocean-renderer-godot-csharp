#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Ocean.Core.Config;
using Ocean.Core.Shore;
using Ocean.Core.Spectrum;

namespace Ocean.Core.Sampling;

/// <summary>The water at one world xz: height above sea level, surface normal and the velocity of the surface water.</summary>
public struct WaveSample
{
    public float Height;
    public Vector3 Normal;
    /// <summary>Orbital velocity of the surface water there (m/s); Y is how fast it rises.</summary>
    public Vector3 Velocity;
}

/// <summary>
/// CPU re-evaluation of the strongest waves of the GPU spectrum, for gameplay: floating objects, buoys, splashes.
/// Takes the N largest components of a cascade's initial spectrum (same seed, phases and conventions as
/// spectrum.glsl / fft.glsl) and sums them analytically, so it follows the swell exactly but not the fine chop.
/// Each texel k contributes a(k) cos(k·p + ωt + φ) to the height and −a(k)·λ·(k/|k|) sin(...) to the horizontal
/// displacement, where a = 2|h0(k)| and p is the undisplaced position. The water over a world xz belongs to the p that
/// the choppy displacement moves onto it, found by a few Newton steps.
/// </summary>
public sealed class WaveSampler
{
    /// <summary>One SIMD vector of components; padding lanes have zero amplitude.</summary>
    struct Block
    {
        public Vector<float> Kx, Kz, Tau, Ah, Ax, Az, AhKx, AhKz, AxKx, AxKz, AzKz, AhW, AxW, AzW;
    }

    /// <summary>A selected texel: wave vector, angular frequency, amplitude 2|h0| and phase of h0.</summary>
    internal readonly record struct Wave(float Kx, float Kz, float Omega, float Amplitude, double Phase);

    sealed class Cascade
    {
        public CascadeConfig Spectrum = new();
        public int Resolution, Count;
        public Wave[] Waves = Array.Empty<Wave>();
        public float CapturedEnergy;
        public Block[] Blocks = Array.Empty<Block>();
        public double Time = double.NaN;
        public float PatchSize;
        /// <summary>The shader samples the texture at grid / patch size, so texel i (the FFT at i * size / N) shows at its centre.</summary>
        public float TexelCentre;
    }

    static readonly int Lanes = Vector<float>.Count;
    Cascade[] _cascades = Array.Empty<Cascade>();

    /// <summary>Newton steps that undo the choppy displacement (0: the height at the undisplaced point).</summary>
    public int Iterations { get; set; } = 2;
    /// <summary>The ground under the water; null: deep open sea everywhere.</summary>
    public SeaFloor? Floor { get; set; }
    /// <summary>Stationary bodies folded over the floor (their lee calms the waves); null: none.</summary>
    public BodyLayer? Bodies { get; set; }
    /// <summary>The shader's shallow-water calming (shore.gdshaderinc shore_calm).</summary>
    public ShoreCalm Calm { get; set; } = ShoreCalm.Default;

    public int CascadeCount => _cascades.Length;
    /// <summary>Components summed for a cascade.</summary>
    public int Count(int cascade) => _cascades[cascade].Waves.Length;
    /// <summary>Share of the cascade's height variance the selected components carry (0-1).</summary>
    public float CapturedEnergy(int cascade) => _cascades[cascade].CapturedEnergy;
    internal IReadOnlyList<Wave> Waves(int cascade) => _cascades[cascade].Waves;

    /// <summary>
    /// Follows a config: re-selects a cascade's components when its spectrum, the resolution or the count changed
    /// (tens of ms), otherwise only rescales them (height scale, choppiness). counts[i]: components of cascade i.
    /// </summary>
    public void Configure(OceanConfig config, ReadOnlySpan<int> counts)
    {
        int n = Math.Min(counts.Length, config.Cascades.Length);
        while (n > 0 && counts[n - 1] <= 0) n--;
        if (_cascades.Length != n)
        {
            var next = new Cascade[n];
            for (int i = 0; i < n; i++) next[i] = i < _cascades.Length ? _cascades[i] : new Cascade();
            _cascades = next;
        }
        for (int i = 0; i < n; i++)
        {
            var c = _cascades[i];
            var s = config.Cascades[i];
            int count = Math.Max(counts[i], 0);
            if (c.Resolution != config.Resolution || c.Count != count || !SameSpectrum(c.Spectrum, s))
            {
                c.Waves = Select(s, config.Resolution, InitialSpectrum.CascadeSeed(i), count, out c.CapturedEnergy);
                c.Resolution = config.Resolution;
                c.Count = count;
            }
            c.Spectrum = Copy(s);
            c.PatchSize = s.PatchSize;
            c.TexelCentre = 0.5f * s.PatchSize / config.Resolution;
            c.Blocks = Pack(c.Waves, s.HeightScale, s.HeightScale * s.Choppiness);
            c.Time = double.NaN;
        }
    }

    /// <summary>Sums exactly these components for a cascade (tests, GPU checks). The next <see cref="Configure"/> re-selects.</summary>
    internal void Use(int cascade, Wave[] waves, float patchSize, float height, float chop, float texelCentre = 0f)
    {
        if (_cascades.Length <= cascade) Array.Resize(ref _cascades, cascade + 1);
        for (int i = 0; i < _cascades.Length; i++) _cascades[i] ??= new Cascade();
        var c = _cascades[cascade];
        c.Waves = waves;
        c.Resolution = -1;
        c.PatchSize = patchSize;
        c.TexelCentre = texelCentre;
        c.Blocks = Pack(waves, height, height * chop);
        c.Time = double.NaN;
    }

    static bool SameSpectrum(CascadeConfig a, CascadeConfig b) => a.PatchSize == b.PatchSize && a.WindSpeed == b.WindSpeed
        && a.WindDirection == b.WindDirection && a.Fetch == b.Fetch && a.Suppression == b.Suppression;

    static CascadeConfig Copy(CascadeConfig s) => new()
    {
        PatchSize = s.PatchSize, WindSpeed = s.WindSpeed, WindDirection = s.WindDirection, Fetch = s.Fetch, Suppression = s.Suppression,
    };

    /// <summary>
    /// The count largest |h0(k)| of an N x N spectrum. Only texels up to 8x the peak wavenumber (at least 32) are searched:
    /// beyond that every amplitude is far below the swell's (the tests check this against the full grid).
    /// </summary>
    internal static Wave[] Select(CascadeConfig s, int n, uint seed, int count, out float captured, int window = 0)
    {
        float dir = s.WindDirection * MathF.PI / 180f;
        if (window <= 0)
        {
            double kp = Math.Pow(Jonswap.PeakAngularFrequency(s.WindSpeed, s.Fetch), 2) / Jonswap.G;
            window = (int)Math.Ceiling(8 * kp * s.PatchSize / (2 * Math.PI));
            window = Math.Max(window, 32);
        }
        window = Math.Min(window, n / 2 - 1);
        var best = new PriorityQueue<Wave, float>(count + 1);
        double total = 0, kept = 0;
        for (int my = -window; my <= window; my++)
            for (int mx = -window; mx <= window; mx++)
            {
                float kx = 2f * MathF.PI * mx / s.PatchSize, kz = 2f * MathF.PI * my / s.PatchSize;
                float amp = InitialSpectrum.Amplitude(kx, kz, s.PatchSize, s.WindSpeed, dir, s.Fetch, s.Suppression);
                if (amp == 0) continue;
                // h0 = gaussian * amp / sqrt 2, so a = 2|h0| = sqrt 2 * amp * |gaussian|.
                var g = InitialSpectrum.Gaussian((mx + n) % n, (my + n) % n, seed) * (amp * MathF.Sqrt(2f));
                float a = g.Length();
                total += (double)a * a;
                if (count == 0) continue;
                if (best.Count == count)
                {
                    best.TryPeek(out _, out float smallest);
                    if (a <= smallest) continue;
                    best.Dequeue();
                }
                float omega = MathF.Sqrt((float)Jonswap.G * MathF.Sqrt(kx * kx + kz * kz));
                best.Enqueue(new Wave(kx, kz, omega, a, Math.Atan2(g.Y, g.X)), a);
            }
        var waves = new Wave[best.Count];
        for (int i = waves.Length - 1; i >= 0; i--)
        {
            waves[i] = best.Dequeue();
            kept += (double)waves[i].Amplitude * waves[i].Amplitude;
        }
        captured = total > 0 ? (float)(kept / total) : 1f;
        return waves;
    }

    static Block[] Pack(Wave[] waves, float height, float chop)
    {
        var blocks = new Block[(waves.Length + Lanes - 1) / Lanes];
        var f = new float[Lanes * 13];
        for (int b = 0; b < blocks.Length; b++)
        {
            Array.Clear(f);
            for (int l = 0; l < Lanes && b * Lanes + l < waves.Length; l++)
            {
                var w = waves[b * Lanes + l];
                float k = MathF.Sqrt(w.Kx * w.Kx + w.Kz * w.Kz);
                float ah = w.Amplitude * height, ax = w.Amplitude * chop * w.Kx / k, az = w.Amplitude * chop * w.Kz / k;
                float[] v = { w.Kx, w.Kz, ah, ax, az, ah * w.Kx, ah * w.Kz, ax * w.Kx, ax * w.Kz, az * w.Kz, ah * w.Omega, ax * w.Omega, az * w.Omega };
                for (int i = 0; i < v.Length; i++) f[i * Lanes + l] = v[i];
            }
            Vector<float> V(int i) => new(f, i * Lanes);
            blocks[b] = new Block
            {
                Kx = V(0), Kz = V(1), Ah = V(2), Ax = V(3), Az = V(4), AhKx = V(5), AhKz = V(6),
                AxKx = V(7), AxKz = V(8), AzKz = V(9), AhW = V(10), AxW = V(11), AzW = V(12),
            };
        }
        return blocks;
    }

    /// <summary>Sets every cascade's simulation time (s).</summary>
    public void SetTime(double time)
    {
        for (int i = 0; i < _cascades.Length; i++) SetTime(i, time);
    }

    /// <summary>Sets one cascade's simulation time (its OceanCascade.Time, which already includes the time scale).</summary>
    public void SetTime(int cascade, double time)
    {
        var c = _cascades[cascade];
        if (c.Time == time) return;
        c.Time = time;
        Span<float> tau = stackalloc float[Lanes];
        for (int b = 0; b < c.Blocks.Length; b++)
        {
            for (int l = 0; l < Lanes; l++)
            {
                int i = b * Lanes + l;
                // Wrapped in double: the phase grows without bound, float would lose it within minutes.
                tau[l] = i < c.Waves.Length ? (float)Math.IEEERemainder(c.Waves[i].Omega * time + c.Waves[i].Phase, 2 * Math.PI) : 0f;
            }
            c.Blocks[b].Tau = new Vector<float>(tau);
        }
    }

    public WaveSample Sample(Vector2 xz, double time)
    {
        SetTime(time);
        return Sample(xz);
    }

    public WaveSample Sample(Vector2 xz)
    {
        WaveSample s = default;
        Sample(new ReadOnlySpan<Vector2>(in xz), new Span<WaveSample>(ref s));
        return s;
    }

    /// <summary>Samples many points at the current time(s).</summary>
    public void Sample(ReadOnlySpan<Vector2> xz, Span<WaveSample> result)
    {
        int nc = _cascades.Length;
        Span<float> lx = stackalloc float[Math.Max(nc, 1)], lz = stackalloc float[Math.Max(nc, 1)], calm = stackalloc float[Math.Max(nc, 1)];
        for (int i = 0; i < xz.Length; i++)
        {
            var target = xz[i];
            var p = target;
            // Each cascade is periodic in its patch: evaluate it near the origin to keep k·p small in float.
            for (int c = 0; c < nc; c++)
            {
                double size = _cascades[c].PatchSize, x = target.X - _cascades[c].TexelCentre, z = target.Y - _cascades[c].TexelCentre;
                lx[c] = (float)(x - size * Math.Floor(x / size));
                lz[c] = (float)(z - size * Math.Floor(z / size));
            }
            for (int it = 0; it < Iterations; it++)
            {
                CalmAt(p, calm);
                var d = p - target;
                Displacement(lx, lz, d, calm, out var disp, out float jxx, out float jxz, out float jzz);
                // Solve p + D(p) = target: Newton with the displacement's Jacobian, a plain fixed-point step near a fold.
                var r = p + disp - target;
                float a = 1 + jxx, b = jxz, e = 1 + jzz, det = a * e - b * b;
                p -= det > 0.2f ? new Vector2(e * r.X - b * r.Y, a * r.Y - b * r.X) / det : r;
            }
            CalmAt(p, calm);
            result[i] = Evaluate(lx, lz, p - target, calm);
        }
    }

    void CalmAt(Vector2 p, Span<float> calm)
    {
        var shore = BodyLayer.Lookup(Floor, Bodies, p);
        for (int c = 0; c < calm.Length; c++) calm[c] = Calm.Factor(shore, c);
    }

    void Displacement(Span<float> lx, Span<float> lz, Vector2 offset, Span<float> calm, out Vector2 disp, out float jxx, out float jxz, out float jzz)
    {
        float dx = 0, dz = 0;
        jxx = jxz = jzz = 0;
        for (int c = 0; c < _cascades.Length; c++)
        {
            var px = new Vector<float>(lx[c] + offset.X);
            var pz = new Vector<float>(lz[c] + offset.Y);
            Vector<float> sx = default, sz = default, sxx = default, sxz = default, szz = default;
            foreach (ref readonly var b in _cascades[c].Blocks.AsSpan())
            {
                SinCos(b.Kx * px + b.Kz * pz + b.Tau, out var sin, out var cos);
                sx += b.Ax * sin;
                sz += b.Az * sin;
                sxx += b.AxKx * cos;
                sxz += b.AxKz * cos;
                szz += b.AzKz * cos;
            }
            float k = calm[c];
            dx -= k * Vector.Sum(sx);
            dz -= k * Vector.Sum(sz);
            jxx -= k * Vector.Sum(sxx);
            jxz -= k * Vector.Sum(sxz);
            jzz -= k * Vector.Sum(szz);
        }
        disp = new Vector2(dx, dz);
    }

    WaveSample Evaluate(Span<float> lx, Span<float> lz, Vector2 offset, Span<float> calm)
    {
        float y = 0, yx = 0, yz = 0, jxx = 0, jxz = 0, jzz = 0, vx = 0, vy = 0, vz = 0;
        for (int c = 0; c < _cascades.Length; c++)
        {
            var px = new Vector<float>(lx[c] + offset.X);
            var pz = new Vector<float>(lz[c] + offset.Y);
            Vector<float> sy = default, syx = default, syz = default, sxx = default, sxz = default, szz = default, svx = default, svy = default, svz = default;
            foreach (ref readonly var b in _cascades[c].Blocks.AsSpan())
            {
                SinCos(b.Kx * px + b.Kz * pz + b.Tau, out var sin, out var cos);
                sy += b.Ah * cos;
                syx += b.AhKx * sin;
                syz += b.AhKz * sin;
                sxx += b.AxKx * cos;
                sxz += b.AxKz * cos;
                szz += b.AzKz * cos;
                svx += b.AxW * cos;
                svy += b.AhW * sin;
                svz += b.AzW * cos;
            }
            float k = calm[c];
            y += k * Vector.Sum(sy);
            yx -= k * Vector.Sum(syx);
            yz -= k * Vector.Sum(syz);
            jxx -= k * Vector.Sum(sxx);
            jxz -= k * Vector.Sum(sxz);
            jzz -= k * Vector.Sum(szz);
            vx -= k * Vector.Sum(svx);
            vy -= k * Vector.Sum(svy);
            vz -= k * Vector.Sum(svz);
        }
        // Tangents of the displaced surface along the undisplaced x and z; the normal is their cross product.
        var tx = new Vector3(1 + jxx, yx, jxz);
        var tz = new Vector3(jxz, yz, 1 + jzz);
        return new WaveSample { Height = y, Normal = Vector3.Normalize(Vector3.Cross(tz, tx)), Velocity = new Vector3(vx, vy, vz) };
    }

    /// <summary>sin and cos per lane: quadrant reduction to [-π/4, π/4], then Taylor polynomials (error below 4e-7).</summary>
    internal static void SinCos(Vector<float> x, out Vector<float> sin, out Vector<float> cos)
    {
        var j = Vector.Floor(x * new Vector<float>(2f / MathF.PI) + new Vector<float>(0.5f));
        var r = x - j * new Vector<float>(1.5707963705062866f) - j * new Vector<float>(-4.371139e-8f);
        var r2 = r * r;
        var s = r + r * r2 * (new Vector<float>(-1f / 6f) + r2 * (new Vector<float>(1f / 120f) + r2 * new Vector<float>(-1f / 5040f)));
        var c = Vector<float>.One + r2 * (new Vector<float>(-0.5f) + r2 * (new Vector<float>(1f / 24f) + r2 * (new Vector<float>(-1f / 720f) + r2 * new Vector<float>(1f / 40320f))));
        var q = Vector.ConvertToInt32(j);
        var odd = Vector.Equals(q & Vector<int>.One, Vector<int>.One);
        var two = new Vector<int>(2);
        sin = Vector.AsVectorSingle(Vector.AsVectorInt32(Vector.ConditionalSelect(odd, c, s)) ^ Vector.ShiftLeft(q & two, 30));
        cos = Vector.AsVectorSingle(Vector.AsVectorInt32(Vector.ConditionalSelect(odd, s, c)) ^ Vector.ShiftLeft((q + Vector<int>.One) & two, 30));
    }
}
