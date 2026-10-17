using System.Numerics;
using Ocean.Core.Spectrum;

namespace Ocean.Tests;

public class JonswapTests
{
    [Test]
    [Arguments(10f, 150_000f)]
    [Arguments(5f, 30_000f)]
    public async Task PeakFrequencyMatchesFormula(float wind, float fetch)
    {
        var expected = 22.0 * Math.Pow(9.81 * 9.81 / (wind * fetch), 1.0 / 3.0);
        await Assert.That(Jonswap.PeakAngularFrequency(wind, fetch)).IsEqualTo(expected).Within(1e-6);
    }

    [Test]
    [Arguments(12f, 200_000f)]
    [Arguments(5f, 30_000f)]
    public async Task AlphaMatchesFormula(float wind, float fetch)
    {
        var expected = 0.076 * Math.Pow(wind * wind / (fetch * 9.81), 0.22);
        await Assert.That(Jonswap.Alpha(wind, fetch)).IsEqualTo(expected).Within(1e-9);
    }

    [Test]
    [Arguments(0.5)]
    [Arguments(0.95)]
    [Arguments(1.0)]
    [Arguments(1.3)]
    [Arguments(2.0)]
    [Arguments(5.0)]
    public async Task DonelanBannerIntegratesToOne(double ratio)
    {
        const int steps = 200_000;
        double sum = 0, d = 2 * Math.PI / steps;
        for (int i = 0; i < steps; i++) sum += Jonswap.DonelanBanner(-Math.PI + (i + 0.5) * d, ratio) * d;
        await Assert.That(sum).IsEqualTo(1.0).Within(1e-3);
    }
}

public class InitialSpectrumTests
{
    [Test]
    public async Task WrappedIndexIsSymmetric()
    {
        await Assert.That(InitialSpectrum.WrappedIndex(0, 512)).IsEqualTo(0);
        await Assert.That(InitialSpectrum.WrappedIndex(255, 512)).IsEqualTo(255);
        await Assert.That(InitialSpectrum.WrappedIndex(256, 512)).IsEqualTo(-256);
        await Assert.That(InitialSpectrum.WrappedIndex(511, 512)).IsEqualTo(-1);
    }

    static Vector4 Texel(int x, int y, int n = 64) =>
        InitialSpectrum.Texel(x, y, n, 73f, 8f, 0.6f, 60_000f, 0.1f, InitialSpectrum.CascadeSeed(1));

    [Test]
    public async Task NyquistRowAndColumnAreZero()
    {
        for (int i = 0; i < 64; i++)
        {
            await Assert.That(Texel(32, i)).IsEqualTo(Vector4.Zero);
            await Assert.That(Texel(i, 32)).IsEqualTo(Vector4.Zero);
        }
    }

    [Test]
    public async Task PartnerIsConjugateOfMinusK()
    {
        int n = 64;
        foreach (var (x, y) in new[] { (1, 2), (5, 60), (40, 7), (63, 63) })
        {
            var t = Texel(x, y);
            var m = Texel((n - x) % n, (n - y) % n);
            // t.zw = conj(h0(-k)) and h0(-k) = m.xy
            await Assert.That(t.Z).IsEqualTo(m.X).Within(1e-9f);
            await Assert.That(t.W).IsEqualTo(-m.Y).Within(1e-9f);
            await Assert.That(m.Z).IsEqualTo(t.X).Within(1e-9f);
            await Assert.That(m.W).IsEqualTo(-t.Y).Within(1e-9f);
        }
    }

    [Test]
    public async Task NoGaussianHashCollisionsAt512()
    {
        var seen = new HashSet<uint>();
        uint seed = InitialSpectrum.CascadeSeed(0);
        for (int y = 0; y < 512; y++)
            for (int x = 0; x < 512; x++) seen.Add(InitialSpectrum.Seed(x, y, seed));
        await Assert.That(seen.Count).IsEqualTo(512 * 512);
    }
}
