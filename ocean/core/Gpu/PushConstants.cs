namespace Ocean.Core.Gpu;

/// <summary>The C# side of every compute shader's push-constant block (a test checks them against the .glsl files).</summary>
public static class PushConstants
{
    public static readonly PushConstantLayout Spectrum = new(
        ("patch_size", PushField.Float), ("wind_speed", PushField.Float), ("wind_direction", PushField.Float), ("fetch", PushField.Float),
        ("suppression", PushField.Float), ("seed", PushField.UInt), ("size", PushField.Int), ("pad0", PushField.Float));

    public static readonly PushConstantLayout Fft = new(
        ("size", PushField.Int), ("pass_index", PushField.Int), ("time", PushField.Float), ("patch_size", PushField.Float),
        ("height_scale", PushField.Float), ("choppiness", PushField.Float), ("pad0", PushField.Float), ("pad1", PushField.Float));

    public static readonly PushConstantLayout Mipmap = new(
        ("dst_size", PushField.IVec2), ("pad0", PushField.Float), ("pad1", PushField.Float));

    public static readonly PushConstantLayout FoamField = new(
        ("origin", PushField.Vec2), ("texel_size", PushField.Float), ("dt", PushField.Float),
        ("shift", PushField.IVec2), ("size", PushField.Int), ("bias", PushField.Float),
        ("gain", PushField.Float), ("inject_rate", PushField.Float), ("decay", PushField.Float), ("fresh_decay", PushField.Float),
        ("streak", PushField.Vec2), ("k_along", PushField.Float), ("k_across", PushField.Float),
        ("patch_size", PushField.Vec4), ("weight", PushField.Vec4), ("lod", PushField.Vec4));

    /// <summary>Shader file name to layout, for the layout test.</summary>
    public static readonly (string File, PushConstantLayout Layout)[] ByShader =
    {
        ("spectrum.glsl", Spectrum), ("fft.glsl", Fft), ("mipmap.glsl", Mipmap), ("foam_field.glsl", FoamField),
    };
}
