using System.Numerics;
using Ocean.Core.Shore;

namespace Ocean.Tests;

public class SeaFloorTests
{
    [Test]
    public async Task DistanceTransformMatchesBruteForce()
    {
        const int w = 23, h = 17;
        var rng = new Random(3);
        var target = Enumerable.Range(0, w * h).Select(_ => rng.NextDouble() < 0.06).ToArray();
        var d = SeaFloor.DistanceTransform(i => target[i], w, h);
        for (int i = 0; i < w * h; i++)
        {
            float best = float.MaxValue;
            for (int j = 0; j < w * h; j++)
            {
                if (!target[j]) continue;
                float dx = i % w - j % w, dz = i / w - j / w;
                best = MathF.Min(best, dx * dx + dz * dz);
            }
            await Assert.That(d[i]).IsEqualTo(best).Within(1e-3f);
        }
    }

    /// <summary>9 x 9 grid: deep sea, an island ring in the middle and a pool inside the ring.</summary>
    static SeaFloor Atoll()
    {
        const int n = 9;
        var heights = new float[n * n];
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            int r = Math.Max(Math.Abs(x - 4), Math.Abs(z - 4));
            heights[z * n + x] = r == 0 ? -1f : r <= 2 ? 3f : -10f;
        }
        return SeaFloor.FromHeights(heights, n, n, 2f, new Vector2(100f, 50f));
    }

    [Test]
    public async Task DepthIsSeaLevelMinusGround()
    {
        var floor = SeaFloor.FromHeights(new[] { -4f, 1f, 0.5f, -0.25f }, 2, 2, 1f, Vector2.Zero, seaLevel: 0.5f);
        await Assert.That(floor.WaterDepth[0]).IsEqualTo(4.5f);
        await Assert.That(floor.WaterDepth[1]).IsEqualTo(-0.5f);
        await Assert.That(floor.WaterDepth[2]).IsEqualTo(0f);
    }

    [Test]
    public async Task EnclosedPoolIsNotOpenSea()
    {
        var floor = Atoll();
        await Assert.That(floor.OpenSea[0]).IsEqualTo(1f);
        await Assert.That(floor.OpenSea[4 * 9 + 4]).IsEqualTo(0f); // the pool
        await Assert.That(floor.OpenSea[4 * 9 + 5]).IsEqualTo(0f); // the ring
        await Assert.That(floor.ShoreDistance[4 * 9 + 4]).IsLessThan(0f);
    }

    [Test]
    public async Task ShoreDistanceIsSignedAndInMetres()
    {
        var floor = Atoll();
        // Row 4: sea at x = 0, 1, ring from x = 2. Sample x = 1 is one cell from land: half a cell of 2 m to the shore.
        await Assert.That(floor.ShoreDistance[4 * 9 + 1]).IsEqualTo(1f).Within(1e-4f);
        await Assert.That(floor.ShoreDistance[4 * 9 + 0]).IsEqualTo(3f).Within(1e-4f);
        await Assert.That(floor.ShoreDistance[4 * 9 + 2]).IsEqualTo(-1f).Within(1e-4f);
    }

    [Test]
    public async Task AllWetWithoutBorderWaterIsOpen()
    {
        // A basin whose rim is land everywhere: no wet border, so the water counts as sea rather than a lake.
        var heights = new float[] { 1, 1, 1, 1, -2, 1, 1, 1, 1 };
        var floor = SeaFloor.FromHeights(heights, 3, 3, 1f, Vector2.Zero);
        await Assert.That(floor.OpenSea[4]).IsEqualTo(1f);
    }
}
