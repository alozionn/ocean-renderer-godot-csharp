using System.Diagnostics;
using System.Numerics;
using Ocean.Core.Config;
using Ocean.Core.Fft;
using Ocean.Core.Sampling;
using Ocean.Core.Shore;
using Ocean.Core.Spectrum;
using Wave = Ocean.Core.Sampling.WaveSampler.Wave;

namespace Ocean.Tests;

public class WaveSamplerTests
{
    /// <summary>The breeze preset's swell and chop cascades (demo/presets/ocean_breeze.json).</summary>
    static OceanConfig Breeze()
    {
        var c = OceanConfig.Default;
        c.Cascades[0] = new CascadeConfig { PatchSize = 400, WindSpeed = 16, WindDirection = 20, Fetch = 300000, Suppression = 0.5f, HeightScale = 1.3f, Choppiness = 0.9f };
        c.Cascades[1] = new CascadeConfig { PatchSize = 73, WindSpeed = 11, WindDirection = 35, Fetch = 60000, Suppression = 0.1f, HeightScale = 0.85f, Choppiness = 0.9f };
        return c;
    }

    static float Uniform(Random rng, float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

    [Test]
    public async Task SimdSinCosMatchesMath()
    {
        var rng = new Random(5);
        int lanes = Vector<float>.Count;
        double worst = 0;
        var x = new float[lanes];
        for (int i = 0; i < 2000; i++)
        {
            for (int l = 0; l < lanes; l++) x[l] = Uniform(rng, -2000f, 2000f);
            WaveSampler.SinCos(new Vector<float>(x), out var s, out var c);
            for (int l = 0; l < lanes; l++)
                worst = Math.Max(worst, Math.Max(Math.Abs(s[l] - Math.Sin(x[l])), Math.Abs(c[l] - Math.Cos(x[l]))));
        }
        // Dominated by the float argument itself near 2000 rad (half an ulp is 6e-5).
        await Assert.That(worst).IsLessThan(2e-4);
    }

    [Test]
    [Arguments(0, 256)]
    [Arguments(1, 256)]
    [Arguments(0, 512)]
    public async Task WindowedSelectionEqualsFullGrid(int cascade, int n)
    {
        foreach (var config in new[] { OceanConfig.Default, Breeze() })
        {
            var s = config.Cascades[cascade];
            uint seed = InitialSpectrum.CascadeSeed(cascade);
            var windowed = WaveSampler.Select(s, n, seed, 64, out _);
            var full = WaveSampler.Select(s, n, seed, 64, out _, window: n / 2 - 1);
            await Assert.That(windowed.SequenceEqual(full)).IsTrue();
        }
    }

    [Test]
    public async Task SelectionKeepsTheLargestInOrder()
    {
        var s = Breeze().Cascades[0];
        var waves = WaveSampler.Select(s, 128, InitialSpectrum.CascadeSeed(0), 48, out float captured, window: 63);
        var all = WaveSampler.Select(s, 128, InitialSpectrum.CascadeSeed(0), 127 * 127, out _, window: 63);
        await Assert.That(waves.Length).IsEqualTo(48);
        await Assert.That(waves.Zip(all.Take(48)).All(p => p.First == p.Second)).IsTrue();
        for (int i = 1; i < waves.Length; i++) await Assert.That(waves[i].Amplitude).IsLessThanOrEqualTo(waves[i - 1].Amplitude);
        double kept = waves.Sum(w => (double)w.Amplitude * w.Amplitude), total = all.Sum(w => (double)w.Amplitude * w.Amplitude);
        await Assert.That(captured).IsEqualTo((float)(kept / total)).Within(1e-4f);
        await Assert.That(captured).IsGreaterThan(0.3f).And.IsLessThan(1f);
    }

    /// <summary>
    /// The whole GPU pipeline on the CPU at N = 64 (h0 per spectrum.glsl, evolution and packing per fft.glsl, the reference
    /// Stockham transform): with every component and no refinement, the sampler reproduces the displacement texels.
    /// </summary>
    [Test]
    public async Task AllComponentsReproduceTheFftPipeline()
    {
        const int n = 64;
        const double t = 37.25;
        var config = Breeze();
        config.Resolution = n;
        var s = config.Cascades[0];
        float l = s.HeightScale * s.Choppiness, dir = s.WindDirection * MathF.PI / 180f;
        var hy = new Complex[n, n];
        var hxz = new Complex[n, n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var h0 = InitialSpectrum.Texel(x, y, n, s.PatchSize, s.WindSpeed, dir, s.Fetch, s.Suppression, InitialSpectrum.CascadeSeed(0));
                double kx = 2 * Math.PI * InitialSpectrum.WrappedIndex(x, n) / s.PatchSize, kz = 2 * Math.PI * InitialSpectrum.WrappedIndex(y, n) / s.PatchSize;
                double k = Math.Sqrt(kx * kx + kz * kz), phase = Math.Sqrt(9.81 * k) * t;
                var e = Complex.FromPolarCoordinates(1, phase);
                var h = new Complex(h0.X, h0.Y) * e + new Complex(h0.Z, h0.W) * Complex.Conjugate(e);
                hy[x, y] = h;
                // Dx + i Dz, as packed pair A.
                hxz[x, y] = k > 0 ? Complex.ImaginaryOne * (kx / k) * h - (kz / k) * h : 0;
            }
        var dy = Inverse2D(hy, n);
        var dxz = Inverse2D(hxz, n);

        var sampler = new WaveSampler { Iterations = 0 };
        sampler.Configure(config, new[] { n * n });
        sampler.SetTime(t);
        await Assert.That(sampler.Count(0)).IsEqualTo((n - 1) * (n - 1) - 1);
        double worstY = 0, peak = 0;
        for (int y = 0; y < n; y += 3)
            for (int x = 0; x < n; x += 3)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f) * (s.PatchSize / n); // texel centres, where the shader shows texel (x, y)
                worstY = Math.Max(worstY, Math.Abs(sampler.Sample(p).Height - s.HeightScale * dy[x, y].Real));
                peak = Math.Max(peak, Math.Abs(s.HeightScale * dy[x, y].Real));
                await Assert.That(Math.Abs(dy[x, y].Imaginary)).IsLessThan(1e-6);
            }
        await Assert.That(worstY).IsLessThan(1e-4 * Math.Max(peak, 1));
        await Assert.That(peak).IsGreaterThan(0.1);

        // Horizontal displacement: refinement off, so the height at p + D(p) needs D. Recover it with refinement on.
        sampler.Iterations = 6;
        double worstMoved = 0;
        for (int y = 1; y < n; y += 7)
            for (int x = 2; x < n; x += 7)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f) * (s.PatchSize / n); // texel centres, where the shader shows texel (x, y)
                var moved = p + l * new Vector2((float)dxz[x, y].Real, (float)dxz[x, y].Imaginary);
                worstMoved = Math.Max(worstMoved, Math.Abs(sampler.Sample(moved).Height - s.HeightScale * dy[x, y].Real));
            }
        await Assert.That(worstMoved).IsLessThan(1e-3);
    }

    static Complex[,] Inverse2D(Complex[,] f, int n)
    {
        var r = (Complex[,])f.Clone();
        var line = new Complex[n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++) line[x] = r[x, y];
            ReferenceFft.Inverse(line);
            for (int x = 0; x < n; x++) r[x, y] = line[x];
        }
        for (int x = 0; x < n; x++)
        {
            for (int y = 0; y < n; y++) line[y] = r[x, y];
            ReferenceFft.Inverse(line);
            for (int y = 0; y < n; y++) r[x, y] = line[y];
        }
        return r;
    }

    /// <summary>One wave along x (the shape GpuChecks' single-wave test feeds the GPU): a Gerstner trochoid.</summary>
    [Test]
    public async Task SingleWaveIsATrochoid()
    {
        const float patch = 100f, a = 0.5f, chop = 0.8f, height = 1.3f;
        const double t = 0.37;
        float k = 2 * MathF.PI * 4 / patch, w = MathF.Sqrt(9.81f * k);
        var sampler = new WaveSampler { Iterations = 4 };
        sampler.Use(0, new[] { new Wave(k, 0, w, a, 0.3) }, patch, height, chop);
        sampler.SetTime(t);
        double worstH = 0, worstN = 0, worstV = 0;
        for (int i = 0; i < 200; i++)
        {
            float p = i * 0.73f - 40f;
            double th = k * p + w * t + 0.3;
            // Undisplaced p moves to p - chop a sin(th) and sits at height a cos(th).
            var s = sampler.Sample(new Vector2((float)(p - height * chop * a * Math.Sin(th)), 5f));
            worstH = Math.Max(worstH, Math.Abs(s.Height - height * a * Math.Cos(th)));
            var tangent = Vector3.Normalize(new Vector3((float)(1 - height * chop * a * k * Math.Cos(th)), (float)(-height * a * k * Math.Sin(th)), 0));
            worstN = Math.Max(worstN, Math.Abs(Vector3.Dot(s.Normal, tangent)) + Math.Abs(s.Normal.Z));
            worstV = Math.Max(worstV, Math.Abs(s.Velocity.Y + height * a * w * Math.Sin(th)) + Math.Abs(s.Velocity.X + height * chop * a * w * Math.Cos(th)));
        }
        await Assert.That(worstH).IsLessThan(1e-4);
        await Assert.That(worstN).IsLessThan(1e-4);
        await Assert.That(worstV).IsLessThan(1e-4);
    }

    /// <summary>
    /// With the breeze swell's 48 components: for an undisplaced point p the sampler, asked at p + D(p), finds p again
    /// (its height is the height at p), and its normal and rise rate match finite differences.
    /// </summary>
    [Test]
    public async Task RefinementInvertsTheChoppyDisplacement()
    {
        var config = Breeze();
        var s = config.Cascades[0];
        var sampler = new WaveSampler { Iterations = 3 };
        sampler.Configure(config, new[] { 48 });
        const double t = 123.4;
        sampler.SetTime(t);
        var waves = sampler.Waves(0);
        double l = s.HeightScale * s.Choppiness, centre = 0.5 * s.PatchSize / config.Resolution;
        (Vector2 d, double h) At(Vector2 p)
        {
            double px = p.X - centre, pz = p.Y - centre; // the texel-centre shift the sampler copies from the shader
            double dx = 0, dz = 0, h = 0;
            foreach (var w in waves)
            {
                double th = w.Kx * px + w.Kz * pz + w.Omega * t + w.Phase, k = Math.Sqrt(w.Kx * w.Kx + w.Kz * w.Kz);
                h += w.Amplitude * Math.Cos(th);
                dx -= w.Amplitude * w.Kx / k * Math.Sin(th);
                dz -= w.Amplitude * w.Kz / k * Math.Sin(th);
            }
            return (new Vector2((float)(l * dx), (float)(l * dz)), s.HeightScale * h);
        }
        var rng = new Random(11);
        double worst = 0, worstUnrefined = 0, maxShift = 0, worstNormal = 0;
        for (int i = 0; i < 300; i++)
        {
            var p = new Vector2(Uniform(rng, -3000, 3000), Uniform(rng, -3000, 3000));
            var (d, h) = At(p);
            maxShift = Math.Max(maxShift, d.Length());
            var sample = sampler.Sample(p + d);
            worst = Math.Max(worst, Math.Abs(sample.Height - h));
            worstUnrefined = Math.Max(worstUnrefined, Math.Abs(At(p + d).h - h));
            // Normal against central differences of the refined height.
            const float e = 0.05f;
            var q = p + d;
            float hx = (sampler.Sample(q + new Vector2(e, 0)).Height - sampler.Sample(q - new Vector2(e, 0)).Height) / (2 * e);
            float hz = (sampler.Sample(q + new Vector2(0, e)).Height - sampler.Sample(q - new Vector2(0, e)).Height) / (2 * e);
            worstNormal = Math.Max(worstNormal, (sample.Normal - Vector3.Normalize(new Vector3(-hx, 1, -hz))).Length());
        }
        await Assert.That(maxShift).IsGreaterThan(0.5); // the swell really is choppy
        await Assert.That(worst).IsLessThan(2e-3);
        await Assert.That(worstUnrefined).IsGreaterThan(20 * worst);
        await Assert.That(worstNormal).IsLessThan(5e-3);

        // Rise rate: the vertical velocity of the water particle is dY/dt at fixed p.
        sampler.Iterations = 0;
        var p0 = new Vector2(17, -230);
        sampler.SetTime(t + 1e-3);
        float up = sampler.Sample(p0).Height;
        sampler.SetTime(t - 1e-3);
        float down = sampler.Sample(p0).Height;
        sampler.SetTime(t);
        await Assert.That(sampler.Sample(p0).Velocity.Y).IsEqualTo((up - down) / 2e-3f).Within(5e-3f);
    }

    [Test]
    public async Task CalmFactorIsTheShaderFormula()
    {
        var calm = new ShoreCalm(16f, 5f, 0.6f, 0.15f);
        static float Smoothstep(float e1, float x) => Math.Clamp(x / e1, 0, 1) * Math.Clamp(x / e1, 0, 1) * (3 - 2 * Math.Clamp(x / e1, 0, 1));
        float[] full = { 16f, 5f, 0.6f };
        foreach (float depth in new[] { -2f, 0f, 0.3f, 2f, 4f, 8f, 15.9f, 40f })
            foreach (float open in new[] { 0f, 0.4f, 1f })
                for (int c = 0; c < 3; c++)
                {
                    float expected = Smoothstep(full[c], depth) * (0.15f + (1 - 0.15f) * open); // smoothstep * mix(lake_waves, 1, open)
                    await Assert.That(calm.Factor((depth, open), c)).IsEqualTo(expected).Within(1e-6f);
                }
        await Assert.That(calm.Factor(ShoreCalm.Deep, 0)).IsEqualTo(1f);
    }

    [Test]
    public async Task SeaFloorLookupIsBilinearAndDeepOutside()
    {
        // 3 x 2 samples, 10 m cells, origin (100, 50): depths 2, 4, 6 / 12, 14, 16.
        var floor = SeaFloor.FromHeights(new[] { -2f, -4f, -6f, -12f, -14f, -16f }, 3, 2, 10f, new Vector2(100, 50));
        await Assert.That(ShoreCalm.Lookup(floor, new Vector2(110, 50)).Depth).IsEqualTo(4f).Within(1e-5f);
        await Assert.That(ShoreCalm.Lookup(floor, new Vector2(115, 55)).Depth).IsEqualTo(10f).Within(1e-5f);
        await Assert.That(ShoreCalm.Lookup(floor, new Vector2(97, 50)).Depth).IsEqualTo(2f).Within(1e-5f); // clamped edge half cell
        await Assert.That(ShoreCalm.Lookup(floor, new Vector2(94, 50))).IsEqualTo(ShoreCalm.Deep);
        await Assert.That(ShoreCalm.Lookup(floor, new Vector2(110, 66))).IsEqualTo(ShoreCalm.Deep);
    }

    [Test]
    public async Task ShallowWaterCalmsEachCascade()
    {
        var config = Breeze();
        var deep = new WaveSampler { Iterations = 0 };
        deep.Configure(config, new[] { 24, 8 });
        deep.SetTime(50);
        // Uniform 4 m deep open water: swell keeps smoothstep(0, 16, 4), chop smoothstep(0, 5, 4).
        var heights = Enumerable.Repeat(-4f, 16 * 16).ToArray();
        var shallow = new WaveSampler { Iterations = 0, Floor = SeaFloor.FromHeights(heights, 16, 16, 100f, new Vector2(-800, -800)) };
        shallow.Configure(config, new[] { 24, 8 });
        shallow.SetTime(50);
        var swellOnly = new WaveSampler { Iterations = 0 };
        swellOnly.Configure(config, new[] { 24 });
        swellOnly.SetTime(50);
        float a0 = ShoreCalm.Default.Factor((4f, 1f), 0), a1 = ShoreCalm.Default.Factor((4f, 1f), 1);
        var rng = new Random(2);
        for (int i = 0; i < 20; i++)
        {
            var p = new Vector2(Uniform(rng, -500, 500), Uniform(rng, -500, 500));
            float swell = swellOnly.Sample(p).Height, chop = deep.Sample(p).Height - swell;
            await Assert.That(shallow.Sample(p).Height).IsEqualTo(a0 * swell + a1 * chop).Within(1e-4f);
        }
    }

    /// <summary>300 points (50 boats x 6 hull points): 48 swell waves, and the ocean's defaults (256 swell + 32 chop).</summary>
    [Test]
    [Arguments(48, 0, 0.1)]
    [Arguments(256, 32, 0.6)]
    public async Task FiftyBoatsAreCheap(int swell, int chop, double budgetMs)
    {
        var sampler = new WaveSampler();
        sampler.Configure(Breeze(), new[] { swell, chop });
        var rng = new Random(4);
        var points = Enumerable.Range(0, 300).Select(_ => new Vector2(Uniform(rng, -2000, 2000), Uniform(rng, -2000, 2000))).ToArray();
        var result = new WaveSample[points.Length];
        var times = new List<double>();
        // Past tiered JIT start-up (tier 0 code is several times slower), as in a running game.
        for (int run = 0; run < 3000; run++) sampler.Sample(points, result);
        for (int run = 0; run < 200; run++)
        {
            var sw = Stopwatch.StartNew();
            sampler.SetTime(run * 0.016);
            sampler.Sample(points, result);
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        double median = times[times.Count / 2];
        Console.WriteLine($"300 samples, {swell}+{chop} components, {sampler.Iterations} iterations, {Vector<float>.Count} lanes: median {median:F4} ms");
        // A Debug build (JIT optimiser off) is about 10x slower.
        bool optimised = typeof(WaveSampler).Assembly.GetCustomAttributes(typeof(DebuggableAttribute), false).Cast<DebuggableAttribute>().All(d => !d.IsJITOptimizerDisabled);
        await Assert.That(median).IsLessThan(optimised ? budgetMs : 10 * budgetMs);
    }
}
