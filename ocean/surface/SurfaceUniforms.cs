namespace Ocean.Surface;

/// <summary>Every ocean.gdshader / spray.gdshader uniform that C# sets. C# uses only these names.</summary>
internal static class SurfaceUniforms
{
    public static readonly string[] Displacement = { "displacement_0", "displacement_1", "displacement_2" };
    public static readonly string[] Slope = { "slope_0", "slope_1", "slope_2" };
    public const string PatchSizes = "patch_sizes";
    public const string TexelSizes = "texel_sizes";
    public const string MeshSpacing = "mesh_spacing";
    public const string MeshGrowth = "mesh_growth";
    public const string FoamNear = "foam_near";
    public const string FoamFar = "foam_far";
    public const string FoamNearRect = "foam_near_rect";
    public const string FoamFarRect = "foam_far_rect";
    public const string FoamDetail = "foam_detail";
    public const string FoamDetailSoft = "foam_detail_soft";
    public const string StreakAngle = "foam_streak_angle";
    public const string SeaFloor = "sea_floor";
    public const string SeaFloorRect = "sea_floor_rect";
    public const string BodyPages = "body_pages";
    public const string BodyIndex = "body_index";
    public const string BodyRect = "body_rect";
    public const string BodySamples = "body_samples";

    // Spray only.
    public const string CameraXz = "camera_xz";
    public const string SeaLevel = "sea_level";
    public const string WindDir = "wind_dir";

    /// <summary>Surface "shore" uniforms the spray also reads; copied from the surface material whenever they change.</summary>
    public static readonly string[] ShoreShared =
    {
        "shallow_swell_depth", "shallow_chop_depth", "shallow_ripple_depth", "lake_waves", "shore_waves", "shore_waves_land",
        "breaking_ratio",
    };
}
