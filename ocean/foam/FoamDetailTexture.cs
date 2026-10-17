using Godot;
using Ocean.Core.Config;
using Ocean.Core.Foam;

namespace Ocean.Foam;

/// <summary>The greyscale foam detail texture named by <see cref="OceanConfig.FoamTexture"/>; procedural when the file is missing.</summary>
internal static class FoamDetailTexture
{
    static ImageTexture _procedural;

    public static Texture2D Load(string root, string name)
    {
        string path = $"{root}assets/foam_{name}.png";
        if (name != "procedural" && ResourceLoader.Exists(path)) return GD.Load<Texture2D>(path);
        if (name != "procedural") GD.PushWarning($"Ocean: foam texture {path} not found, using the procedural one.");
        return _procedural ??= Generate();
    }

    public static ImageTexture Generate() => ToTexture(FoamDetail.Generate(512), 512);

    static ImageTexture ToTexture(float[] v, int size)
    {
        var bytes = new byte[v.Length];
        for (int i = 0; i < v.Length; i++) bytes[i] = (byte)Mathf.RoundToInt(Mathf.Clamp(v[i], 0f, 1f) * 255f);
        var image = Image.CreateFromData(size, size, false, Image.Format.R8, bytes);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }
}
