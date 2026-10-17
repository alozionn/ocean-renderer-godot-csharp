using System.Globalization;
using System.Numerics;
using Ocean.Core.Config;

namespace Ocean.Tests;

public class ConfigTests
{
    [Test]
    public async Task RoundTripKeepsEverythingUnderGermanCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            var c = OceanConfig.Default;
            c.Resolution = 1024;
            c.Cascades[1].WindSpeed = 9.75f;
            c.Foam.Bias = 0.625f;
            c.FoamTexture = "ambientcg_001";
            c.Surface["roughness"] = OceanConfigJson.Number(0.15f);
            c.Surface["sss_color"] = OceanConfigJson.Color(new Vector3(0.2f, 0.4f, 0.6f));
            string json = OceanConfigJson.Serialize(c);
            await Assert.That(json).Contains("9.75");
            var back = OceanConfigJson.Deserialize(json);
            await Assert.That(OceanConfigJson.Serialize(back)).IsEqualTo(json);
            await Assert.That(back.Cascades[1].WindSpeed).IsEqualTo(9.75f);
            await Assert.That(OceanConfigJson.TryColor(back.Surface["sss_color"], out var col)).IsTrue();
            await Assert.That(col.Y).IsEqualTo(0.4f).Within(0.003f);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Test]
    public async Task ColoursReadAsHexOrArray()
    {
        var c = OceanConfigJson.Deserialize("""{ "surface": { "a": "#ff8000", "b": [0.1, 0.2, 0.3] } }""");
        await Assert.That(OceanConfigJson.TryColor(c.Surface["a"], out var a)).IsTrue();
        await Assert.That(a.Y).IsEqualTo(128 / 255f).Within(1e-6f);
        await Assert.That(OceanConfigJson.TryColor(c.Surface["b"], out var b)).IsTrue();
        await Assert.That(b.Z).IsEqualTo(0.3f).Within(1e-6f);
    }

    [Test]
    public async Task MissingFieldsKeepDefaults()
    {
        var c = OceanConfigJson.Deserialize("""{ "cascades": [ { "windSpeed": 16 }, {}, {} ], "foam": { "bias": 0.64 }, "unknown": 3 }""");
        var d = OceanConfig.Default;
        await Assert.That(c.Cascades[0].WindSpeed).IsEqualTo(16f);
        await Assert.That(c.Cascades[0].PatchSize).IsEqualTo(d.Cascades[0].PatchSize);
        await Assert.That(c.Cascades[2].Fetch).IsEqualTo(d.Cascades[2].Fetch);
        await Assert.That(c.Foam.Bias).IsEqualTo(0.64f);
        await Assert.That(c.Foam.Gain).IsEqualTo(d.Foam.Gain);
        await Assert.That(c.Resolution).IsEqualTo(512);
        await Assert.That(c.FoamTexture).IsEqualTo("crest");
    }

    [Test]
    public async Task DiffReportsExactlyTheChangedSpectra()
    {
        var a = OceanConfig.Default;
        var b = a.Clone();
        b.Cascades[1].Fetch = 50_000;
        b.Cascades[2].HeightScale = 0.3f; // not a spectrum field
        b.Foam.Gain = 7;
        var diff = OceanConfigDiff.Compare(a, b);
        await Assert.That(diff.Spectra).IsEquivalentTo(new[] { 1 });
        await Assert.That(diff.Resolution).IsFalse();

        b.Resolution = 1024;
        await Assert.That(OceanConfigDiff.Compare(a, b).Spectra).IsEquivalentTo(new[] { 0, 1, 2 });
    }

    [Test]
    public async Task DiffReportsSurfaceChanges()
    {
        var a = OceanConfig.Default;
        a.Surface["x"] = OceanConfigJson.Number(1);
        var b = a.Clone();
        b.Surface.Remove("x");
        b.Surface["y"] = OceanConfigJson.Number(2);
        await Assert.That(OceanConfigDiff.Compare(a, b).Surface).IsEquivalentTo(new[] { "x", "y" });
    }
}
