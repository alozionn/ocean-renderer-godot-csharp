using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace OceanDev;

/// <summary>In-engine viewport captures, contact sheets and burst strips into .scratch/captures.</summary>
internal static class Captures
{
    public static readonly string Dir = ProjectSettings.GlobalizePath("res://.scratch/captures/");

    public static readonly Dictionary<string, float[]> Poses = new()
    {
        ["hero"] = new[] { 0f, 12, 0, -14, 0 },
        ["high_sun"] = new[] { 0f, 150, 0, -30, 0 },
        ["high_side"] = new[] { 0f, 150, 0, -30, 70 },
        ["low_sun"] = new[] { 0f, 15, 0, -10, 0 },
        ["mid_close"] = new[] { 0f, 40, 0, -25, 15 },
        ["low_away"] = new[] { 0f, 15, 0, -10, 180 },
        ["top_down"] = new[] { 0f, 250, 0, -60, 30 },
        ["close"] = new[] { 0f, 4, 0, -30, 20 },
        ["close_down"] = new[] { 0f, 7, 0, -62, 40 },
        ["shore"] = new[] { 60f, 10, -110, -15, -50 },
    };

    public static void Place(Camera3D camera, float[] pose)
    {
        camera.GlobalTransform = new Transform3D(
            Basis.FromEuler(new Vector3(Mathf.DegToRad(pose[3]), Mathf.DegToRad(pose[4]), 0)),
            new Vector3(pose[0], pose[1], pose[2]));
    }

    public static async Task<Image> Grab(Node node)
    {
        await node.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        return node.GetViewport().GetTexture().GetImage();
    }

    public static string Save(Image image, string name)
    {
        DirAccess.MakeDirRecursiveAbsolute(Dir);
        string path = Dir + name + ".png";
        image.SavePng(path);
        GD.Print($"saved {path}");
        return path;
    }

    /// <summary>Tiles images (scaled to a common cell width) in the given number of columns.</summary>
    public static Image Sheet(IReadOnlyList<Image> images, int columns, int cellWidth)
    {
        if (images.Count == 0) return null;
        int cellHeight = cellWidth * images[0].GetHeight() / images[0].GetWidth();
        int rows = (images.Count + columns - 1) / columns;
        var sheet = Image.CreateEmpty(columns * cellWidth, rows * cellHeight, false, Image.Format.Rgba8);
        for (int i = 0; i < images.Count; i++)
        {
            var img = (Image)images[i].Duplicate();
            img.Convert(Image.Format.Rgba8);
            img.Resize(cellWidth, cellHeight, Image.Interpolation.Lanczos);
            sheet.BlitRect(img, new Rect2I(0, 0, cellWidth, cellHeight), new Vector2I(i % columns * cellWidth, i / columns * cellHeight));
        }
        return sheet;
    }
}
