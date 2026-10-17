using Godot;

namespace Ocean.Simulation;

/// <summary>Loads and frees every compute shader and pipeline of the ocean (pipelines before shaders).</summary>
internal sealed class OceanPipelines
{
    public readonly RenderingDevice Rd;
    public Rid SpectrumShader, FftShader, MipmapShader, FoamShader;
    public Rid Spectrum, Fft, Mipmap, Foam;

    public OceanPipelines(RenderingDevice rd, string root)
    {
        Rd = rd;
        (SpectrumShader, Spectrum) = Load(root + "simulation/shaders/spectrum.glsl");
        (FftShader, Fft) = Load(root + "simulation/shaders/fft.glsl");
        (MipmapShader, Mipmap) = Load(root + "simulation/shaders/mipmap.glsl");
        (FoamShader, Foam) = Load(root + "foam/shaders/foam_field.glsl");
    }

    public bool Valid => Spectrum.IsValid && Fft.IsValid && Mipmap.IsValid && Foam.IsValid;

    (Rid shader, Rid pipeline) Load(string path)
    {
        var spirv = GD.Load<RDShaderFile>(path)?.GetSpirV();
        if (spirv == null || !string.IsNullOrEmpty(spirv.CompileErrorCompute))
        {
            GD.PushError($"Ocean: compiling {path} failed: {spirv?.CompileErrorCompute}");
            return default;
        }
        var shader = Rd.ShaderCreateFromSpirV(spirv, path.GetFile());
        return (shader, Rd.ComputePipelineCreate(shader));
    }

    public void Free()
    {
        RdResources.Free(Rd, ref Spectrum);
        RdResources.Free(Rd, ref Fft);
        RdResources.Free(Rd, ref Mipmap);
        RdResources.Free(Rd, ref Foam);
        RdResources.Free(Rd, ref SpectrumShader);
        RdResources.Free(Rd, ref FftShader);
        RdResources.Free(Rd, ref MipmapShader);
        RdResources.Free(Rd, ref FoamShader);
    }
}
