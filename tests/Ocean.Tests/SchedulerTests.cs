using Ocean.Core.Scheduling;

namespace Ocean.Tests;

public class SchedulerTests
{
    const int Cascades = 3;

    [Test]
    public async Task At1400FpsEachCascadeTicks600TimesAndFramesRecordOneClient()
    {
        var s = new TickScheduler(Cascades + 1, 60);
        var ticks = new int[Cascades + 1];
        int maxPerFrame = 0;
        const int frames = 14_000;
        for (int f = 1; f <= frames; f++)
        {
            double clock = f / 1400.0;
            int thisFrame = 0;
            for (int c = 0; c <= Cascades; c++)
                if (s.TryTick(c, clock, false, out _)) { ticks[c]++; thisFrame++; }
            maxPerFrame = Math.Max(maxPerFrame, thisFrame);
        }
        for (int c = 0; c < Cascades; c++) await Assert.That(ticks[c]).IsBetween(599, 601);
        await Assert.That(maxPerFrame).IsEqualTo(1);
    }

    [Test]
    public async Task At30FpsEverythingTicksEveryFrame()
    {
        var s = new TickScheduler(Cascades + 1, 60);
        for (int f = 1; f <= 300; f++)
            for (int c = 0; c <= Cascades; c++)
                await Assert.That(s.TryTick(c, f / 30.0, false, out _)).IsTrue();
    }

    [Test]
    [Arguments(1.0)]
    [Arguments(0.5)]
    public async Task SimulatedTimeIsElapsedTimesTimeScale(double timeScale)
    {
        var s = new TickScheduler(Cascades + 1, 60);
        var rng = new Random(3);
        double clock = 0, simulated = 0;
        while (clock < 10)
        {
            clock += 0.0005 + rng.NextDouble() * 0.04;
            if (s.TryTick(1, clock, false, out double dt)) simulated += dt * timeScale;
        }
        // The last tick is at most one interval (plus a frame) behind the clock.
        await Assert.That(simulated).IsEqualTo(clock * timeScale).Within(timeScale * (1 / 60.0 + 0.041));
    }
}
