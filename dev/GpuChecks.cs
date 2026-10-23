using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Ocean.Core.Config;
using Ocean.Core.Gpu;
using Ocean.Core.Sampling;
using Ocean.Core.Spectrum;
using Ocean.Simulation;
using OceanDemo.Boat;
using OceanNode = Ocean.Ocean;
using Vec2 = System.Numerics.Vector2;
using Vec3 = System.Numerics.Vector3;

namespace OceanDev;

/// <summary>GPU self-tests with readback (section 15.2). Each check prints PASS/FAIL.</summary>
internal static class GpuChecks
{
    public static bool Run(OceanPipelines pipelines)
    {
        bool ok = true;
        ok &= Report("spectrum vs CPU", SpectrumVsCpu(pipelines));
        ok &= Report("single wave", SingleWave(pipelines));
        ok &= Report("mip chain of a constant", MipConstant(pipelines));
        return ok;
    }

    public static bool Report(string name, (bool ok, string detail) r)
    {
        GD.Print($"{(r.ok ? "PASS" : "FAIL")} {name}: {r.detail}");
        return r.ok;
    }

    static (bool, string) SpectrumVsCpu(OceanPipelines p)
    {
        const int n = 512;
        var c = OceanConfig.Default.Cascades[0];
        var cascade = new OceanCascade(p, 0, n);
        var rd = p.Rd;
        long list = rd.ComputeListBegin();
        cascade.RecordSpectrum(list, c);
        rd.ComputeListEnd();
        float[] gpu = Floats(rd.TextureGetData(cascade.H0, 0));
        cascade.Free();

        var rng = new Random(1234);
        double worst = 0;
        for (int s = 0; s < 64; s++)
        {
            int x = rng.Next(n), y = rng.Next(n);
            if (s < 8) { x = rng.Next(1, 12); y = rng.Next(1, 12); } // include the energetic low frequencies
            var cpu = InitialSpectrum.Texel(x, y, n, c.PatchSize, c.WindSpeed, Mathf.DegToRad(c.WindDirection), c.Fetch, c.Suppression, InitialSpectrum.CascadeSeed(0));
            int o = (y * n + x) * 4;
            var g = new System.Numerics.Vector4(gpu[o], gpu[o + 1], gpu[o + 2], gpu[o + 3]);
            float norm = cpu.Length();
            if (norm < 1e-12f && g.Length() < 1e-12f) continue;
            worst = Math.Max(worst, (g - cpu).Length() / Math.Max(norm, 1e-12f));
        }
        return (worst < 1e-3, $"worst relative error {worst:E2} over 64 texels");
    }

    static (bool, string) SingleWave(OceanPipelines p)
    {
        const int n = 512, m = 4;
        const float L = 100f, a = 0.5f, chop = 0.8f, t = 0.37f;
        var rd = p.Rd;
        var cascade = new OceanCascade(p, 0, n);
        var h0 = new float[n * n * 4];
        h0[m * 4] = a / 2;
        h0[(n - m) * 4 + 2] = a / 2;
        rd.TextureUpdate(cascade.H0, 0, Bytes(h0));
        cascade.Time = t;
        long list = rd.ComputeListBegin();
        cascade.RecordTick(list, new CascadeConfig { PatchSize = L, HeightScale = 1, Choppiness = chop });
        rd.ComputeListEnd();
        float[] disp = Halves(rd.TextureGetData(cascade.Displacement, 0));
        float[] slope = Halves(rd.TextureGetData(cascade.Slope, 0));
        cascade.Free();

        float k = 2 * Mathf.Pi * m / L, w = Mathf.Sqrt(9.81f * k);
        double eD = 0, eS = 0, eJ = 0, minJ = double.MaxValue;
        bool sharpens = true;
        foreach (int y in new[] { 0, 77, 300 })
            for (int x = 0; x < n; x++)
            {
                float th = k * x * L / n + w * t;
                float dy = a * Mathf.Cos(th), dx = -chop * a * Mathf.Sin(th), jxx = 1 - chop * a * k * Mathf.Cos(th);
                float sx = -a * k * Mathf.Sin(th) / Mathf.Max(jxx, 0.25f);
                int o = (y * n + x) * 4;
                eD = Math.Max(eD, Math.Max(Math.Abs(disp[o] - dx), Math.Max(Math.Abs(disp[o + 1] - dy), Math.Abs(disp[o + 2]))));
                eS = Math.Max(eS, Math.Max(Math.Abs(slope[o] - sx), Math.Abs(slope[o + 1])));
                eJ = Math.Max(eJ, Math.Abs(disp[o + 3] - jxx));
                minJ = Math.Min(minJ, disp[o + 3]);
                // Near a crest (dy > 0.9a) points must move towards it: dx has the sign of -sin(th).
                if (dy > 0.9f * a && Mathf.Abs(dx) > 0.01f && Mathf.Sign(disp[o]) != Mathf.Sign(dx)) sharpens = false;
            }
        double expectedMinJ = 1 - chop * k * a;
        bool ok = eD < 1e-3 && eS < 1e-3 && eJ < 2e-3 && sharpens && Math.Abs(minJ - expectedMinJ) < 2e-3;
        return (ok, $"displacement {eD:E2} m, slope {eS:E2}, J {eJ:E2}, min J {minJ:F4} (expected {expectedMinJ:F4}), crests sharpen: {sharpens}");
    }

    /// <summary>
    /// The CPU water sampler against the GPU (swell and chop displacement textures read back once). With every swell
    /// component the raw sum must equal the texels to half precision (same spectrum, phases, signs, texel centres). Then,
    /// for the swell alone and for swell plus chop, heights over random world xz and the pose a boat fits to its six hull
    /// points, against the GPU surface found the shader's way (bilinear lookup at grid / patch size, displacement inverted).
    /// </summary>
    public static (bool, string) WaterSampler(OceanNode ocean)
    {
        var config = ocean.Config;
        int n = config.Resolution;
        const double t = 61.7;
        var rd = ocean.Pipelines.Rd;
        var cascades = new OceanCascade[2];
        for (int i = 0; i < 2; i++)
        {
            cascades[i] = new OceanCascade(ocean.Pipelines, i, n);
            long list = rd.ComputeListBegin();
            cascades[i].RecordSpectrum(list, config.Cascades[i]);
            cascades[i].Time = t;
            cascades[i].RecordTick(list, config.Cascades[i]);
            rd.ComputeListEnd();
        }
        float[] patches = { config.Cascades[0].PatchSize, config.Cascades[1].PatchSize };
        GpuSurface[] gpuSea = { new(rd, cascades.AsSpan(0, 1), patches), new(rd, cascades, patches) }; // swell; swell + chop
        foreach (var c in cascades) c.Free();

        var rng = new Random(9);
        Vec2 Anywhere() => new((float)rng.NextDouble() * 4000 - 2000, (float)rng.NextDouble() * 4000 - 2000);
        const int samples = 4000, poses = 500;
        var xzs = Enumerable.Range(0, samples).Select(_ => Anywhere()).ToArray();
        // The demo boat's hull points (Boat.cs) at random places and headings.
        var hull = Boat.HullPoints.ToArray();
        var hullXz = new Vec2[poses * hull.Length];
        for (int i = 0; i < poses; i++)
        {
            var centre = Anywhere();
            float yaw = (float)rng.NextDouble() * MathF.Tau, cos = MathF.Cos(yaw), sin = MathF.Sin(yaw);
            for (int j = 0; j < hull.Length; j++)
                hullXz[i * hull.Length + j] = centre + new Vec2(cos * hull[j].X + sin * hull[j].Y, -sin * hull[j].X + cos * hull[j].Y);
        }
        Vec3[] Poses(float[] h) => Enumerable.Range(0, poses).Select(i => HullPose(h.AsSpan(i * hull.Length, hull.Length))).ToArray();
        static double Rms(IEnumerable<double> v) => Math.Sqrt(v.Average(x => x * x));

        // Every swell component.
        var c0 = config.Cascades[0];
        var all = WaveSampler.Select(c0, n, InitialSpectrum.CascadeSeed(0), n * n, out _, window: n / 2 - 1);
        var full = new WaveSampler { Iterations = 0 };
        full.Use(0, all, c0.PatchSize, c0.HeightScale, c0.Choppiness, 0.5f * c0.PatchSize / n);
        full.SetTime(t);
        double worstTexel = 0, worstFull = 0;
        for (int i = 0; i < 32; i++)
        {
            int x = rng.Next(n), y = rng.Next(n);
            worstTexel = Math.Max(worstTexel, Math.Abs(full.Sample(new Vec2(x + 0.5f, y + 0.5f) * (c0.PatchSize / n)).Height - gpuSea[0].Texel(0, x, y).Y));
        }
        full.Iterations = new WaveSampler().Iterations;
        for (int i = 0; i < 32; i++) worstFull = Math.Max(worstFull, Math.Abs(full.Sample(xzs[i]).Height - gpuSea[0].Height(xzs[i])));
        double total = all.Sum(w => (double)w.Amplitude * w.Amplitude);
        var report = new System.Text.StringBuilder(FormattableString.Invariant(
            $"all {all.Length} swell components: texels worst {worstTexel * 100:F2} cm, world xz worst {worstFull * 100:F1} cm"));

        int swell = ocean.SampledSwellWaves, chop = ocean.SampledChopWaves;
        bool ok = worstTexel < 0.01 && worstFull < 0.2;
        var results = new WaveSample[samples];
        var hulls = new WaveSample[hullXz.Length];
        foreach (var (name, sea, rows) in new[]
        {
            ("swell", 1, new[] { new[] { swell }, new[] { swell / 4 }, new[] { swell / 2 }, new[] { 2 * swell } }),
            ("swell + chop", 2, new[] { new[] { swell, chop }, new[] { swell, 0 } }),
        })
        {
            var gpu = xzs.Select(gpuSea[sea - 1].Height).ToArray();
            var gpuPoses = Poses(hullXz.Select(gpuSea[sea - 1].Height).ToArray());
            double heaveRms = Rms(gpuPoses.Select(p => (double)p.X)), tiltRms = Rms(gpuPoses.SelectMany(p => new double[] { p.Y, p.Z }));
            report.Append(FormattableString.Invariant(
                $"; {name}: GPU RMS height {Rms(gpu.Select(h => (double)h)) * 100:F0} cm, boat heave {heaveRms * 100:F0} cm, pitch/roll {tiltRms:F1} deg"));
            foreach (var counts in rows)
            {
                var watch = Stopwatch.StartNew();
                var sampler = new WaveSampler();
                sampler.Configure(config, counts);
                double selectMs = watch.Elapsed.TotalMilliseconds;
                sampler.SetTime(t);
                sampler.Sample(xzs, results);
                var errors = results.Select((r, i) => (double)(r.Height - gpu[i])).ToArray();
                sampler.Sample(hullXz, hulls);
                var poseErrors = Poses(hulls.Select(h => h.Height).ToArray()).Select((p, i) => p - gpuPoses[i]).ToArray();
                double heave = Rms(poseErrors.Select(p => (double)p.X)), tilt = Rms(poseErrors.SelectMany(p => new double[] { p.Y, p.Z }));
                for (int i = 0; i < 100; i++) sampler.Sample(xzs.AsSpan(0, 300), results.AsSpan(0, 300));
                watch.Restart();
                for (int i = 0; i < 100; i++) sampler.Sample(xzs.AsSpan(0, 300), results.AsSpan(0, 300));
                double batchMs = watch.Elapsed.TotalMilliseconds / 100;
                double kept = all.Take(counts[0]).Sum(w => (double)w.Amplitude * w.Amplitude);
                report.Append(FormattableString.Invariant(
                    $"; {string.Join("+", counts)} components ({kept / total * 100:F0} % of the swell energy): RMS {Rms(errors) * 100:F0} cm, max {errors.Max(Math.Abs) * 100:F0} cm, boat heave {heave * 100:F0} cm, pitch/roll {tilt:F1} deg, selection {selectMs:F0} ms, 300 samples {batchMs:F2} ms"));
                // The ocean's own counts must keep a boat within a fifth of the heave and half the pitch/roll it feels.
                if (counts[0] == swell && (counts.Length == 1 || counts[1] == chop)) ok &= heave < 0.2 * heaveRms && tilt < 0.5 * tiltRms;
            }
        }
        return (ok, report.ToString());
    }

    /// <summary>Heave (m), pitch and roll (degrees, as Boat.cs) of the least-squares plane through water heights under the boat's hull points.</summary>
    public static Vec3 HullPose(ReadOnlySpan<float> heights)
    {
        var hull = Boat.HullPoints;
        float a = 0, hx = 0, hz = 0, xx = 0, zz = 0;
        for (int j = 0; j < hull.Length; j++)
        {
            a += heights[j];
            hx += heights[j] * hull[j].X;
            hz += heights[j] * hull[j].Y;
            xx += hull[j].X * hull[j].X;
            zz += hull[j].Y * hull[j].Y;
        }
        return new(a / hull.Length, Mathf.RadToDeg(MathF.Atan(-hz / zz)), Mathf.RadToDeg(MathF.Atan(hx / xx)));
    }

    static (bool, string) MipConstant(OceanPipelines p)
    {
        const int n = 64;
        var rd = p.Rd;
        int mips = RdResources.MipCount(n);
        var tex = RdResources.CreateTexture(rd, n, n, RenderingDevice.DataFormat.R16G16B16A16Sfloat, mips,
            RdResources.Sampled | RenderingDevice.TextureUsageBits.CanUpdateBit);
        var constant = new[] { 0.25f, -0.5f, 0.75f, 1.0f };
        int total = 0;
        for (int m = 0; m < mips; m++) total += (n >> m) * (n >> m) * 4;
        var data = new float[total]; // the update covers every level: the constant in level 0, zeros below
        for (int i = 0; i < n * n * 4; i++) data[i] = constant[i % 4];
        rd.TextureUpdate(tex, 0, HalfBytes(data));
        var views = new List<Rid>();
        var sets = new List<Rid>();
        for (int m = 0; m < mips; m++) views.Add(RdResources.MipView(rd, tex, m));
        long list = rd.ComputeListBegin();
        rd.ComputeListBindComputePipeline(list, p.Mipmap);
        for (int m = 0; m < mips - 1; m++)
        {
            var set = RdResources.ImageSet(rd, p.MipmapShader, views[m], views[m + 1]);
            sets.Add(set);
            var push = PushConstants.Mipmap.CreateBuffer();
            int s = n >> (m + 1);
            PushConstants.Mipmap.Set(push, "dst_size", s, s);
            rd.ComputeListBindUniformSet(list, set, 0);
            rd.ComputeListSetPushConstant(list, push, (uint)push.Length);
            rd.ComputeListDispatch(list, RdResources.Groups(s, 8), RdResources.Groups(s, 8), 1);
            rd.ComputeListAddBarrier(list);
        }
        rd.ComputeListEnd();
        // texture_get_data returns every mip level, largest first.
        float[] all = Halves(rd.TextureGetData(tex, 0));
        double worst = 0;
        for (int i = n * n * 4; i < all.Length; i++) worst = Math.Max(worst, Math.Abs(all[i] - constant[i % 4]));
        bool complete = all.Length > n * n * 4;
        foreach (var s in sets) rd.FreeRid(s);
        foreach (var v in views) rd.FreeRid(v);
        rd.FreeRid(tex);
        return (complete && worst < 1e-3, $"worst error {worst:E2} over {mips - 1} levels");
    }

    public static float[] Floats(byte[] b)
    {
        var f = new float[b.Length / 4];
        Buffer.BlockCopy(b, 0, f, 0, f.Length * 4);
        return f;
    }

    public static float[] Halves(byte[] b)
    {
        var f = new float[b.Length / 2];
        for (int i = 0; i < f.Length; i++) f[i] = (float)BitConverter.ToHalf(b, i * 2);
        return f;
    }

    static byte[] Bytes(float[] f)
    {
        var b = new byte[f.Length * 4];
        Buffer.BlockCopy(f, 0, b, 0, b.Length);
        return b;
    }

    static byte[] HalfBytes(float[] f)
    {
        var b = new byte[f.Length * 2];
        for (int i = 0; i < f.Length; i++) BitConverter.TryWriteBytes(b.AsSpan(i * 2), (Half)f[i]);
        return b;
    }
}
