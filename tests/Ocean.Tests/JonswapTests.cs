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
}
