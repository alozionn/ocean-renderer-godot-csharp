using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Ocean.Core.Config;

/// <summary>What changed between two configs, so <c>Ocean.Apply</c> regenerates only that.</summary>
public sealed class OceanConfigDiff
{
    static readonly PropertyInfo[] SpectrumFields = typeof(CascadeConfig).GetProperties()
        .Where(p => p.GetCustomAttribute<SpectrumFieldAttribute>() != null).ToArray();

    public bool Resolution { get; private init; }
    public bool CascadeCount { get; private init; }
    /// <summary>Indices of cascades whose initial spectrum must be regenerated.</summary>
    public IReadOnlyList<int> Spectra { get; private init; } = [];
    public bool FoamTexture { get; private init; }
    public IReadOnlyList<string> Surface { get; private init; } = [];

    public static OceanConfigDiff Compare(OceanConfig a, OceanConfig b)
    {
        bool countChanged = a.Cascades.Length != b.Cascades.Length;
        var spectra = Enumerable.Range(0, b.Cascades.Length)
            .Where(i => countChanged || a.Resolution != b.Resolution
                        || SpectrumFields.Any(p => !Equals(p.GetValue(a.Cascades[i]), p.GetValue(b.Cascades[i]))))
            .ToList();
        var surface = a.Surface.Keys.Union(b.Surface.Keys)
            .Where(k => !a.Surface.TryGetValue(k, out var x) || !b.Surface.TryGetValue(k, out var y) || x.GetRawText() != y.GetRawText())
            .ToList();
        return new OceanConfigDiff
        {
            Resolution = a.Resolution != b.Resolution,
            CascadeCount = countChanged,
            Spectra = spectra,
            FoamTexture = a.FoamTexture != b.FoamTexture,
            Surface = surface,
        };
    }
}
