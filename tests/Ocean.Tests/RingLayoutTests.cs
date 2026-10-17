using Ocean.Core.Mesh;

namespace Ocean.Tests;

public class RingLayoutTests
{
    [Test]
    public async Task RingsReachTheOuterSize()
    {
        await Assert.That(RingLayout.HalfSize(32)).IsEqualTo(8f).Within(1e-4f);
        await Assert.That(RingLayout.HalfSize(256)).IsEqualTo(4000f).Within(0.5f);
    }

    [Test]
    [Arguments(40)]
    [Arguments(120)]
    [Arguments(255)]
    public async Task GrowthPredictsRingSpacing(int ring)
    {
        float d = RingLayout.HalfSize(ring);
        float actual = RingLayout.HalfSize(ring + 1) - d;
        await Assert.That(d * RingLayout.Growth()).IsEqualTo(actual).Within(actual * 1e-3f);
    }

    [Test]
    public async Task MeshHasExpectedSize()
    {
        var (positions, indices) = RingLayout.Build(16, 4, 0.25f, 100f);
        await Assert.That(positions.Length).IsEqualTo(33 * 33);
        await Assert.That(indices.Length).IsEqualTo(32 * 32 * 6);
    }
}
