namespace Ocean.Surface;

/// <summary>Every ocean.gdshader uniform that C# sets. C# uses only these names.</summary>
internal static class SurfaceUniforms
{
    public static readonly string[] Displacement = { "displacement_0", "displacement_1", "displacement_2" };
    public static readonly string[] Slope = { "slope_0", "slope_1", "slope_2" };
    public const string PatchSizes = "patch_sizes";
    public const string TexelSizes = "texel_sizes";
    public const string FoamNear = "foam_near";
    public const string FoamFar = "foam_far";
    public const string FoamNearRect = "foam_near_rect";
    public const string FoamFarRect = "foam_far_rect";
    public const string FoamDetail = "foam_detail";
    public const string FoamLow = "foam_low";
    public const string StreakAngle = "foam_streak_angle";
}
