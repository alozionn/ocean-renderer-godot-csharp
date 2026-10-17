using Ocean.Core.Foam;

namespace Ocean.Tests;

public class FoamTests
{
    [Test]
    public async Task OriginSnapsToWholeTexelsAroundTheCamera()
    {
        var o = FoamFieldMath.OriginTexel(10.1f, -3.2f, 0.25f, 1024);
        await Assert.That(o.X).IsEqualTo(40 - 512);
        await Assert.That(o.Z).IsEqualTo(-13 - 512);
    }

    [Test]
    public async Task ShiftIsTheIntegerMoveAndFirstTickShiftsEverythingOut()
    {
        await Assert.That(FoamFieldMath.Shift((10, 20), (13, 18), 1024, false)).IsEqualTo((3, -2));
        await Assert.That(FoamFieldMath.Shift((10, 20), (13, 18), 1024, true)).IsEqualTo((1024, 1024));
        await Assert.That(FoamFieldMath.Shift((0, 0), (2000, 0), 1024, false)).IsEqualTo((1024, 1024));
    }

    [Test]
    public async Task AcrossCoefficientIsClamped()
    {
        await Assert.That(FoamFieldMath.KAcross(0.06f, 1 / 60f, 0.25f)).IsEqualTo(0.06f / 60f / 0.0625f).Within(1e-7f);
        await Assert.That(FoamFieldMath.KAcross(4f, 1 / 30f, 0.25f)).IsEqualTo(FoamFieldMath.MaxK);
    }

    [Test]
    public async Task AlongStencilWidensInsteadOfClamping()
    {
        var small = FoamFieldMath.Along(0.35f, 1 / 60f, 0.25f);
        await Assert.That(small.Step).IsEqualTo(1f);
        await Assert.That(small.K).IsEqualTo(0.35f / 60f / 0.0625f).Within(1e-6f);

        float d = 20f, dt = 1 / 60f, texel = 0.25f;
        var wide = FoamFieldMath.Along(d, dt, texel);
        await Assert.That(wide.Step).IsGreaterThan(1f);
        await Assert.That(wide.K).IsEqualTo(FoamFieldMath.MaxK).Within(1e-5f);
        // The widened stencil keeps the requested diffusion: k * (step * texel)^2 = D * dt.
        await Assert.That(wide.K * wide.Step * wide.Step * texel * texel).IsEqualTo(d * dt).Within(1e-5f);
    }

    static (float Density, float Fresh) Run(float injected, float rate, int ticksPerSecond, float seconds)
    {
        float d = 0, f = 0, dt = 1f / ticksPerSecond;
        for (int i = 0; i < seconds * ticksPerSecond; i++) (d, f) = FoamFieldMath.Step(d, f, injected, rate, 0.35f, 2.5f, dt);
        return (d, f);
    }

    [Test]
    public async Task SteadyStateIsInjectOverDecayCappedAtOne()
    {
        var low = Run(0.1f, 1.2f, 60, 60);
        await Assert.That(low.Density).IsEqualTo(0.1f * 1.2f / 0.35f).Within(0.01f);
        var high = Run(1f, 1.2f, 60, 60);
        await Assert.That(high.Density).IsEqualTo(1f);
    }

    [Test]
    public async Task FreshNeverExceedsDensity()
    {
        float d = 0, f = 0;
        var rng = new Random(5);
        for (int i = 0; i < 2000; i++)
        {
            (d, f) = FoamFieldMath.Step(d, f, rng.NextSingle() < 0.3f ? rng.NextSingle() : 0f, 1.2f, 0.35f, 2.5f, 1 / 60f);
            await Assert.That(f).IsLessThanOrEqualTo(d);
        }
    }

    [Test]
    public async Task TickRateBarelyChangesTheResult()
    {
        var a = Run(0.1f, 1.2f, 60, 20);
        var b = Run(0.1f, 1.2f, 120, 20);
        await Assert.That(Math.Abs(a.Density - b.Density) / a.Density).IsLessThan(0.05f);
    }

    [Test]
    public async Task LowFrequencyTextureMatchesCrestAndTiles()
    {
        const int n = 128;
        var v = FoamDetail.GenerateLow(n);
        var (mean, _, _) = FoamDetail.Stats(v);
        await Assert.That(mean).IsEqualTo(0.41f).Within(0.01f);
        // Tileable: the step across the wrap is no larger than typical neighbour steps.
        double wrap = 0, inner = 0;
        for (int y = 0; y < n; y++)
        {
            wrap += Math.Abs(v[y * n] - v[y * n + n - 1]);
            inner += Math.Abs(v[y * n + n / 2] - v[y * n + n / 2 - 1]);
        }
        await Assert.That(wrap).IsLessThan(inner * 2);
    }

    [Test]
    public async Task ProceduralDetailMatchesCrestDistribution()
    {
        var (mean, p10, p90) = FoamDetail.Stats(FoamDetail.Generate(128));
        await Assert.That(mean).IsEqualTo(0.41f).Within(0.01f);
        await Assert.That(p10).IsBetween(0.0f, 0.2f);
        await Assert.That(p90).IsBetween(0.6f, 0.85f);
    }
}
