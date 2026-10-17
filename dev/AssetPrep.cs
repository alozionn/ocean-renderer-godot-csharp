using System;
using System.Linq;
using Godot;

namespace OceanDev;

/// <summary>One-off asset processing (section 5.4) from .scratch/downloads into the committed asset folders.</summary>
public static class AssetPrep
{
    public static void Run()
    {
        string dl = ProjectSettings.GlobalizePath("res://.scratch/downloads/");
        string Res(string p) => ProjectSettings.GlobalizePath(p);

        System.IO.File.Copy(dl + "foam.png", Res("res://ocean/assets/foam_crest.png"), true);
        PrintStats("crest", Image.LoadFromFile(dl + "foam.png"));

        var opacity = Image.LoadFromFile(dl + "x_foam/Foam001_2K-JPG_Opacity.jpg");
        var foam = MatchCrestDistribution(opacity);
        foam.SavePng(Res("res://ocean/assets/foam_ambientcg_001.png"));
        PrintStats("ambientcg_001", foam);

        var evening = Image.LoadFromFile(dl + "evening_road.jpg");
        evening.Resize(4096, 2048, Image.Interpolation.Lanczos);
        evening.SavePng(Res("res://demo/skies/evening_road_01_puresky.png"));

        foreach (var n in new[] { "07", "03", "17" })
            System.IO.File.Copy(dl + $"x_sbs/Panorama/Panorama_Sky_{n}-512x512.png", Res($"res://demo/skies/sbs_cloudy_sky_{n}.png"), true);
    }

    /// <summary>Red channel, levels stretched from the 1st..99th percentile, gamma so the mean is 0.41 (Crest's).</summary>
    static Image MatchCrestDistribution(Image src)
    {
        src.Convert(Image.Format.Rgb8);
        byte[] rgb = src.GetData();
        var red = new byte[rgb.Length / 3];
        for (int i = 0; i < red.Length; i++) red[i] = rgb[i * 3];
        var hist = Histogram(red);
        float lo = Percentile(hist, red.Length, 0.01f) / 255f, hi = Percentile(hist, red.Length, 0.99f) / 255f;
        float Stretch(int v) => Mathf.Clamp((v / 255f - lo) / (hi - lo), 0f, 1f);
        double MeanFor(double g) => Enumerable.Range(0, 256).Sum(v => hist[v] * Math.Pow(Stretch(v), g)) / red.Length;
        double a = 0.05, b = 20;
        for (int it = 0; it < 60; it++) { double m = Math.Sqrt(a * b); if (MeanFor(m) > 0.41) a = m; else b = m; }
        double gamma = Math.Sqrt(a * b);
        var lut = Enumerable.Range(0, 256).Select(v => (byte)Math.Round(255 * Math.Pow(Stretch(v), gamma))).ToArray();
        GD.Print($"ambientcg: levels {lo:F3}..{hi:F3}, gamma {gamma:F3}");
        return Image.CreateFromData(src.GetWidth(), src.GetHeight(), false, Image.Format.L8, red.Select(v => lut[v]).ToArray());
    }

    static int[] Histogram(byte[] values)
    {
        var h = new int[256];
        foreach (var v in values) h[v]++;
        return h;
    }

    static int Percentile(int[] hist, int count, float p)
    {
        long acc = 0;
        for (int v = 0; v < 256; v++) { acc += hist[v]; if (acc >= p * count) return v; }
        return 255;
    }

    static void PrintStats(string name, Image img)
    {
        img = (Image)img.Duplicate();
        img.Convert(Image.Format.Rgb8);
        byte[] d = img.GetData();
        var red = new byte[d.Length / 3];
        for (int i = 0; i < red.Length; i++) red[i] = d[i * 3];
        var hist = Histogram(red);
        GD.Print($"{name}: {img.GetWidth()}x{img.GetHeight()} mean {red.Average(v => v / 255.0):F3} p10 {Percentile(hist, red.Length, 0.1f) / 255f:F3} p90 {Percentile(hist, red.Length, 0.9f) / 255f:F3}");
    }

}
