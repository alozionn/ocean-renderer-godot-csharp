using System.Text.RegularExpressions;
using Ocean.Core.Gpu;

namespace Ocean.Tests;

public class PushConstantTests
{
    static string OceanDir([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "../../ocean"));

    static readonly Dictionary<string, PushField> GlslTypes = new()
    {
        ["float"] = PushField.Float, ["int"] = PushField.Int, ["uint"] = PushField.UInt,
        ["vec2"] = PushField.Vec2, ["ivec2"] = PushField.IVec2, ["vec4"] = PushField.Vec4,
    };

    /// <summary>Parses the push_constant block of a .glsl into (type, name) pairs with std430 offsets.</summary>
    static List<(string Name, int Offset, int Size)> Parse(string source)
    {
        var block = Regex.Match(source, @"layout\s*\(\s*push_constant[^)]*\)\s*uniform\s+\w+\s*\{(?<body>[^}]*)\}");
        var fields = new List<(string, int, int)>();
        int offset = 0;
        foreach (Match m in Regex.Matches(block.Groups["body"].Value, @"^\s*(?<type>\w+)\s+(?<name>\w+)\s*;", RegexOptions.Multiline))
        {
            int size = PushConstantLayout.SizeOf(GlslTypes[m.Groups["type"].Value]);
            offset = (offset + size - 1) / size * size;
            fields.Add((m.Groups["name"].Value, offset, size));
            offset += size;
        }
        return fields;
    }

    [Test]
    public async Task EveryShaderHasALayout()
    {
        var shaders = Directory.GetFiles(OceanDir(), "*.glsl", SearchOption.AllDirectories).Select(Path.GetFileName).ToHashSet();
        await Assert.That(shaders.SetEquals(PushConstants.ByShader.Select(s => s.File))).IsTrue();
    }

    [Test]
    public async Task LayoutsMatchGlslBlocks()
    {
        foreach (var (file, layout) in PushConstants.ByShader)
        {
            var path = Directory.GetFiles(OceanDir(), file, SearchOption.AllDirectories).Single();
            var glsl = Parse(File.ReadAllText(path));
            await Assert.That(glsl.Count).IsEqualTo(layout.Fields.Count);
            for (int i = 0; i < glsl.Count; i++)
            {
                await Assert.That(glsl[i].Name).IsEqualTo(layout.Fields[i].Name);
                await Assert.That(glsl[i].Offset).IsEqualTo(layout.Fields[i].Offset);
            }
            int size = glsl[^1].Offset + glsl[^1].Size;
            await Assert.That(size).IsEqualTo(layout.Size);
            await Assert.That(size % 16).IsEqualTo(0);
        }
    }
}
