using System.Numerics;
using Ocean.Core.Bodies;
using Ocean.Core.Config;
using Ocean.Core.Sampling;
using Ocean.Core.Shore;

namespace Ocean.Tests;

public class BodyTests
{
    static readonly Vector2 East = Vector2.UnitX; // waves travelling towards +x

    [Test]
    public async Task CircleDistanceIsRadial()
    {
        var rock = WaterBody.Circle(new Vector2(10, -5), 3f);
        await Assert.That(rock.SignedDistance(new Vector2(10, -5))).IsEqualTo(-3f).Within(1e-5f);
        await Assert.That(rock.SignedDistance(new Vector2(15, -5))).IsEqualTo(2f).Within(1e-5f);
        await Assert.That(rock.SignedDistance(new Vector2(10 + 3 * MathF.Sqrt(0.5f), -5 + 3 * MathF.Sqrt(0.5f)))).IsEqualTo(0f).Within(1e-5f);
    }

    [Test]
    public async Task BoxFollowsGodotYaw()
    {
        // Yaw 90°: local +x turns to world −z, so a 10 x 2 box lies along z.
        var box = WaterBody.Box(Vector2.Zero, MathF.PI / 2, 10f, 2f);
        await Assert.That(box.Axis.X).IsEqualTo(0f).Within(1e-6f);
        await Assert.That(box.Axis.Y).IsEqualTo(-1f).Within(1e-6f);
        await Assert.That(box.SignedDistance(new Vector2(0, 4.5f))).IsEqualTo(-0.5f).Within(1e-5f);
        await Assert.That(box.SignedDistance(new Vector2(3, 0))).IsEqualTo(2f).Within(1e-5f);
        await Assert.That(box.SignedDistance(new Vector2(4, 8))).IsEqualTo(MathF.Sqrt(18)).Within(1e-5f); // corner (1, 5) → (4, 8)
        await Assert.That(box.HalfExtent(Vector2.UnitY)).IsEqualTo(5f).Within(1e-5f);
        await Assert.That(box.HalfExtent(Vector2.UnitX)).IsEqualTo(1f).Within(1e-5f);
    }

    [Test]
    public async Task HullIsACapsule()
    {
        var hull = WaterBody.Hull(Vector2.Zero, 0f, 30f, 10f, 2.5f);
        await Assert.That(hull.SignedDistance(new Vector2(15, 0))).IsEqualTo(0f).Within(1e-5f);
        await Assert.That(hull.SignedDistance(new Vector2(10, 5))).IsEqualTo(0f).Within(1e-5f);
        await Assert.That(hull.SignedDistance(new Vector2(15, 5))).IsEqualTo(MathF.Sqrt(50) - 5).Within(1e-4f);
    }

    [Test]
    public async Task NothingToFoldWithoutStationaryBodies()
    {
        await Assert.That(BodyLayer.Build(null, Array.Empty<WaterBody>(), East)).IsNull();
        var boat = WaterBody.Hull(Vector2.Zero, 0f, 20f, 6f, 2f);
        boat.Stationary = false;
        await Assert.That(BodyLayer.Build(null, new[] { boat }, East)).IsNull();
    }

    [Test]
    public async Task GroundedRockIsLandWithASlopingFoot()
    {
        var rock = WaterBody.Circle(new Vector2(3, 3), 4f, top: 12f);
        rock.Skirt = 5f;
        var layer = BodyLayer.Build(null, new[] { rock }, East)!;
        var centre = BodyLayer.Lookup(null, layer, new Vector2(3, 3));
        await Assert.That(centre.Depth).IsEqualTo(-12f).Within(1e-3f);
        await Assert.That(centre.Open).IsEqualTo(1f); // the waves keep running inside: the rock hides them
        await Assert.That(centre.Distance).IsEqualTo(-4f).Within(0.05f);
        // Skirt: deep sea (1e4 m) scaled by distance / skirt, so 2.5 m from the waterline it is half as deep.
        var foot = BodyLayer.Lookup(null, layer, new Vector2(3, 3 + 4 + 2.5f));
        await Assert.That(foot.Depth).IsEqualTo(5e3f).Within(60f);
        await Assert.That(foot.Distance).IsEqualTo(2.5f).Within(0.05f);
        await Assert.That(foot.Open).IsEqualTo(1f);
    }


    [Test]
    public async Task WavesDoNotStepAtARockEdge()
    {
        // The wave calm must not change across a footprint's edge: a step over 0.25 m tore the mesh into a wall.
        var floor = SeaFloor.FromHeights(Enumerable.Repeat(-12f, 16 * 16).ToArray(), 16, 16, 8f, new Vector2(-60, -60));
        var rock = WaterBody.Circle(Vector2.Zero, 6f, top: 14f);
        rock.Skirt = 5f;
        rock.Lee = 0f;
        var layer = BodyLayer.Build(floor, new[] { rock }, East);
        for (int c = 0; c < 3; c++)
        {
            float inside = ShoreCalm.Default.Factor(BodyLayer.Lookup(floor, layer, new Vector2(5.5f, 0)), c);
            float outside = ShoreCalm.Default.Factor(BodyLayer.Lookup(floor, layer, new Vector2(6.5f, 0)), c);
            await Assert.That(inside).IsEqualTo(outside).Within(1e-5f);
        }
    }
    [Test]
    public async Task BodiesBreakWavesButDoNotCalmThem()
    {
        // A reef 1 m under the surface in 12 m of water: shallow for breaking and foam, but the waves keep the calm of
        // the terrain's 12 m (a swell is not calmed over a few metres; it breaks).
        var floor = SeaFloor.FromHeights(Enumerable.Repeat(-12f, 16 * 16).ToArray(), 16, 16, 8f, new Vector2(-60, -60));
        var reef = WaterBody.Circle(Vector2.Zero, 4f, top: -1f);
        reef.Skirt = 3f;
        var p = BodyLayer.Lookup(floor, BodyLayer.Build(floor, new[] { reef }, East), Vector2.Zero);
        await Assert.That(p.Depth).IsEqualTo(1f).Within(1e-3f);
        await Assert.That(p.WaveDepth).IsEqualTo(12f).Within(1e-3f);
        await Assert.That(ShoreCalm.Default.Factor(p, 1)).IsEqualTo(1f).Within(1e-5f);
        await Assert.That(ShoreCalm.Default.Factor(p, 0)).IsEqualTo(ShoreCalm.Default.Factor((12f, 1f), 0)).Within(1e-5f);
    }

    [Test]
    public async Task FloatingHullKeepsTheWaterUnderIt()
    {
        var heights = Enumerable.Repeat(-20f, 16 * 16).ToArray();
        var floor = SeaFloor.FromHeights(heights, 16, 16, 8f, new Vector2(-60, -60));
        var hull = WaterBody.Hull(Vector2.Zero, 0f, 30f, 10f, 2.5f);
        var layer = BodyLayer.Build(floor, new[] { hull }, East)!;
        var under = BodyLayer.Lookup(floor, layer, Vector2.Zero);
        await Assert.That(under.Depth).IsEqualTo(20f).Within(1e-3f); // waves keep running under a moored boat
        await Assert.That(under.Open).IsEqualTo(1f);
        await Assert.That(under.Distance).IsEqualTo(-5f).Within(0.05f);
        // Aground (water shallower than its draft) it is land like a rock.
        var shallow = SeaFloor.FromHeights(Enumerable.Repeat(-1f, 16 * 16).ToArray(), 16, 16, 8f, new Vector2(-60, -60));
        var stuck = BodyLayer.Build(shallow, new[] { hull }, East)!;
        await Assert.That(BodyLayer.Lookup(shallow, stuck, Vector2.Zero).Depth).IsEqualTo(-1.5f).Within(1e-3f);
    }

    [Test]
    public async Task SubmergedReefIsShallowOpenWater()
    {
        var reef = WaterBody.Circle(Vector2.Zero, 3f, top: -1.5f);
        var p = BodyLayer.Lookup(null, BodyLayer.Build(null, new[] { reef }, East), Vector2.Zero);
        await Assert.That(p.Depth).IsEqualTo(1.5f).Within(1e-3f);
        await Assert.That(p.Open).IsEqualTo(1f);
        await Assert.That(p.Distance).IsEqualTo(1e4f); // no waterline: no contact foam ring, only breaking
    }

    [Test]
    public async Task PagesHandBackToTheTerrainAtTheirEdges()
    {
        // A sloping sea floor and a post: beyond 1.5 × reach + 1 m every channel is the terrain's own, so the pages
        // join the coarse texture without a seam.
        int n = 64;
        var heights = new float[n * n];
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++) heights[z * n + x] = -2f - 0.3f * x;
        var floor = SeaFloor.FromHeights(heights, n, n, 4f, new Vector2(-128, -128));
        var post = WaterBody.Circle(new Vector2(5, 7), 0.15f, top: 2f);
        var layer = BodyLayer.Build(floor, new[] { post }, East)!;
        float far = 1.5f * post.EffectiveReach + 1f;
        var rng = new Random(5);
        for (int i = 0; i < 400; i++)
        {
            var p = new Vector2(-20 + 50 * (float)rng.NextDouble(), -20 + 50 * (float)rng.NextDouble());
            if (post.SignedDistance(p) < far + 0.6f) continue; // + a page sample's bilinear reach
            var a = BodyLayer.Lookup(floor, layer, p);
            var b = floor.Sample(p);
            await Assert.That(a.Depth).IsEqualTo(b.Depth).Within(1e-3f);
            await Assert.That(a.Distance).IsEqualTo(b.Distance).Within(1e-3f);
            await Assert.That(a.Shelter).IsEqualTo(0f).Within(1e-4f);
        }
        // Near the post its waterline is the nearest shore.
        await Assert.That(BodyLayer.Lookup(floor, layer, new Vector2(5.65f, 7)).Distance).IsEqualTo(0.5f).Within(0.08f);
    }

    [Test]
    public async Task PageLookupIsContinuousAcrossPageBorders()
    {
        var rock = WaterBody.Circle(new Vector2(16, 16), 6f, top: 10f); // centred on a page corner
        rock.Skirt = 4f;
        var layer = BodyLayer.Build(null, new[] { rock }, East)!;
        for (float x = 4f; x < 28f; x += 0.37f)
        {
            var left = BodyLayer.Lookup(null, layer, new Vector2(15.999f, x));
            var right = BodyLayer.Lookup(null, layer, new Vector2(16.001f, x));
            await Assert.That(right.Distance).IsEqualTo(left.Distance).Within(0.01f);
        }
    }

    [Test]
    public async Task LeeIsDownwindOnly()
    {
        var rock = WaterBody.Circle(Vector2.Zero, 5f, top: 10f);
        var layer = BodyLayer.Build(null, new[] { rock }, East)!;
        float behind = BodyLayer.Lookup(null, layer, new Vector2(12, 0)).Shelter;
        await Assert.That(behind).IsGreaterThan(0.4f);
        await Assert.That(BodyLayer.Lookup(null, layer, new Vector2(-12, 0)).Shelter).IsEqualTo(0f);
        await Assert.That(BodyLayer.Lookup(null, layer, new Vector2(12, 12)).Shelter).IsEqualTo(0f);
        // Fades out by LeeLength × the width shown to the waves (3 × 10 m) behind it.
        await Assert.That(BodyLayer.Lookup(null, layer, new Vector2(5 + 29, 0)).Shelter).IsLessThan(0.01f);
        // The calm takes the lee: swell loses all of it, ripples a little.
        var calm = ShoreCalm.Default;
        var p = new ShorePoint(1e4f, 7f, 1f, behind);
        await Assert.That(calm.Factor(p, 0)).IsEqualTo(1f - behind).Within(1e-5f);
        await Assert.That(calm.Factor(p, 2)).IsEqualTo(1f - 0.3f * behind).Within(1e-5f);
    }

    [Test]
    public async Task SamplerCalmsInTheLee()
    {
        var config = OceanConfig.Default;
        var open = new WaveSampler { Iterations = 0 };
        open.Configure(config, new[] { 16 });
        open.SetTime(30);
        var rock = WaterBody.Circle(Vector2.Zero, 5f, top: 10f);
        var lee = new WaveSampler { Iterations = 0, Bodies = BodyLayer.Build(null, new[] { rock }, East) };
        lee.Configure(config, new[] { 16 });
        lee.SetTime(30);
        var p = new Vector2(12, 0);
        float shelter = BodyLayer.Lookup(null, lee.Bodies, p).Shelter;
        await Assert.That(lee.Sample(p).Height).IsEqualTo(open.Sample(p).Height * (1f - shelter)).Within(1e-4f);
    }

    [Test]
    public async Task CrestsTravelAgainstTheWindVector()
    {
        // The ocean's swell direction (Ocean.WaveDirection) is minus cascade 0's wind vector: energy sits at k along
        // the wind and each component is cos(k·p + ωt + φ). One component: its crest at t + dt sits c·dt along −k.
        var config = OceanConfig.Default;
        var sampler = new WaveSampler { Iterations = 0 };
        sampler.Configure(config, new[] { 1 });
        var w = sampler.Waves(0)[0];
        var k = new Vector2(w.Kx, w.Kz);
        float wind = config.Cascades[0].WindDirection * MathF.PI / 180f;
        await Assert.That(Vector2.Dot(Vector2.Normalize(k), new Vector2(MathF.Cos(wind), MathF.Sin(wind)))).IsGreaterThan(0.5f);
        float speed = w.Omega / k.Length(), dt = 0.7f;
        var p = new Vector2(13, -40);
        sampler.SetTime(10);
        float before = sampler.Sample(p).Height;
        sampler.SetTime(10 + dt);
        float after = sampler.Sample(p - Vector2.Normalize(k) * speed * dt).Height;
        await Assert.That(after).IsEqualTo(before).Within(1e-3f);
    }
}
