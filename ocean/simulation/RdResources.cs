using Godot;
using Godot.Collections;

namespace Ocean.Simulation;

/// <summary>Small helpers around the main RenderingDevice.</summary>
internal static class RdResources
{
    public const RenderingDevice.TextureUsageBits Sampled =
        RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanCopyFromBit;

    public static Rid CreateTexture(RenderingDevice rd, int width, int height, RenderingDevice.DataFormat format, int mips,
        RenderingDevice.TextureUsageBits usage)
    {
        var fmt = new RDTextureFormat
        {
            Format = format,
            Width = (uint)width,
            Height = (uint)height,
            Depth = 1,
            ArrayLayers = 1,
            Mipmaps = (uint)mips,
            TextureType = RenderingDevice.TextureType.Type2D,
            UsageBits = usage,
        };
        return rd.TextureCreate(fmt, new RDTextureView());
    }

    public static Rid MipView(RenderingDevice rd, Rid texture, int mip) =>
        rd.TextureCreateSharedFromSlice(new RDTextureView(), texture, 0, (uint)mip, 1, RenderingDevice.TextureSliceType.Slice2D);

    public static RDUniform Image(int binding, Rid texture)
    {
        var u = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = binding };
        u.AddId(texture);
        return u;
    }

    public static RDUniform Sampled2D(int binding, Rid sampler, Rid texture)
    {
        var u = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = binding };
        u.AddId(sampler);
        u.AddId(texture);
        return u;
    }

    /// <summary>A set of storage images bound to bindings 0..n-1.</summary>
    public static Rid ImageSet(RenderingDevice rd, Rid shader, params Rid[] images)
    {
        var uniforms = new Array<RDUniform>();
        for (int i = 0; i < images.Length; i++) uniforms.Add(Image(i, images[i]));
        return rd.UniformSetCreate(uniforms, shader, 0);
    }

    public static void Free(RenderingDevice rd, ref Rid rid)
    {
        if (rid.IsValid) rd.FreeRid(rid);
        rid = default;
    }

    public static int MipCount(int size) => Mathf.RoundToInt(Mathf.Log(size) / Mathf.Log(2)) + 1;

    public static uint Groups(int size, int groupSize) => (uint)((size + groupSize - 1) / groupSize);
}
