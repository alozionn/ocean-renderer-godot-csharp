using System.Numerics;
using Ocean.Core.Fft;

namespace Ocean.Tests;

public class FftTests
{
    [Test]
    [Arguments(8)]
    [Arguments(64)]
    [Arguments(512)]
    public async Task StockhamEqualsNaiveInverseDft(int n)
    {
        var rng = new Random(n);
        var x = Enumerable.Range(0, n).Select(_ => new Complex(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5)).ToArray();
        var expected = ReferenceFft.NaiveInverseDft(x);
        var actual = (Complex[])x.Clone();
        ReferenceFft.Inverse(actual);
        double worst = expected.Zip(actual, (e, a) => (e - a).Magnitude).Max();
        await Assert.That(worst).IsLessThan(1e-9 * n);
    }

    [Test]
    public async Task PackingRecoversTwoRealFields()
    {
        const int n = 64;
        var rng = new Random(7);
        double[] f = Enumerable.Range(0, n).Select(_ => rng.NextDouble()).ToArray();
        double[] g = Enumerable.Range(0, n).Select(_ => rng.NextDouble()).ToArray();
        // Spectra of the real fields (forward DFT, scaled so the inverse needs no normalisation).
        Complex[] Spectrum(double[] v) => Enumerable.Range(0, n).Select(k =>
            Enumerable.Range(0, n).Aggregate(Complex.Zero, (s, t) => s + v[t] * Complex.FromPolarCoordinates(1.0 / n, -2 * Math.PI * k * t / n))).ToArray();
        var fs = Spectrum(f);
        var gs = Spectrum(g);
        var packed = fs.Zip(gs, (a, b) => a + Complex.ImaginaryOne * b).ToArray();
        ReferenceFft.Inverse(packed);
        double worst = 0;
        for (int i = 0; i < n; i++) worst = Math.Max(worst, Math.Max(Math.Abs(packed[i].Real - f[i]), Math.Abs(packed[i].Imaginary - g[i])));
        await Assert.That(worst).IsLessThan(1e-9);
    }
}
