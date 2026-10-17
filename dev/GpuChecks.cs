using System;
using System.Collections.Generic;
using Godot;
using Ocean.Core.Config;
using Ocean.Core.Gpu;
using Ocean.Core.Spectrum;
using Ocean.Simulation;

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
