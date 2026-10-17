using Godot;
using Ocean.Core.Config;
using Ocean.Core.Gpu;
using Ocean.Core.Spectrum;

namespace Ocean.Simulation;

/// <summary>One FFT cascade: its GPU textures, uniform sets and push constants, and the work it records per tick.</summary>
internal sealed class OceanCascade
{
    readonly RenderingDevice _rd;
    readonly OceanPipelines _p;
    readonly byte[] _spectrumPush = PushConstants.Spectrum.CreateBuffer();
    readonly byte[] _fftPush0 = PushConstants.Fft.CreateBuffer();
    readonly byte[] _fftPush1 = PushConstants.Fft.CreateBuffer();
    readonly byte[][] _mipPush;
    readonly Rid[] _dispMips, _slopeMips, _partialMips, _dispMipSets, _slopeMipSets, _partialMipSets;
    Rid _h0, _specA, _specB, _displacement, _slope, _partials;
    Rid _spectrumSet, _fftSet;

    public readonly int Size;
    public readonly int Mips;
    public double Time;
    public readonly Texture2Drd DisplacementTexture = new(), SlopeTexture = new(), PartialsTexture = new();

    internal Rid H0 => _h0;
    internal Rid Displacement => _displacement;
    internal Rid Slope => _slope;
    internal Rid Partials => _partials;

    public OceanCascade(OceanPipelines pipelines, int index, int size)
    {
        _rd = pipelines.Rd;
        _p = pipelines;
        Size = size;
        Mips = RdResources.MipCount(size);
        const RenderingDevice.DataFormat F32 = RenderingDevice.DataFormat.R32G32B32A32Sfloat, F16 = RenderingDevice.DataFormat.R16G16B16A16Sfloat;
        _h0 = RdResources.CreateTexture(_rd, size, size, F32, 1, RdResources.Sampled | RenderingDevice.TextureUsageBits.CanUpdateBit);
        _specA = RdResources.CreateTexture(_rd, size, size, F32, 1, RenderingDevice.TextureUsageBits.StorageBit);
        _specB = RdResources.CreateTexture(_rd, size, size, F32, 1, RenderingDevice.TextureUsageBits.StorageBit);
        _displacement = RdResources.CreateTexture(_rd, size, size, F16, Mips, RdResources.Sampled);
        _slope = RdResources.CreateTexture(_rd, size, size, F16, Mips, RdResources.Sampled);
        _partials = RdResources.CreateTexture(_rd, size, size, F16, Mips, RdResources.Sampled);

        _dispMips = new Rid[Mips];
        _slopeMips = new Rid[Mips];
        _partialMips = new Rid[Mips];
        for (int m = 0; m < Mips; m++)
        {
            _dispMips[m] = RdResources.MipView(_rd, _displacement, m);
            _slopeMips[m] = RdResources.MipView(_rd, _slope, m);
            _partialMips[m] = RdResources.MipView(_rd, _partials, m);
        }
        _spectrumSet = RdResources.ImageSet(_rd, _p.SpectrumShader, _h0);
        _fftSet = RdResources.ImageSet(_rd, _p.FftShader, _h0, _specA, _specB, _dispMips[0], _slopeMips[0], _partialMips[0]);
        _dispMipSets = new Rid[Mips - 1];
        _slopeMipSets = new Rid[Mips - 1];
        _partialMipSets = new Rid[Mips - 1];
        _mipPush = new byte[Mips - 1][];
        for (int m = 0; m < Mips - 1; m++)
        {
            _dispMipSets[m] = RdResources.ImageSet(_rd, _p.MipmapShader, _dispMips[m], _dispMips[m + 1]);
            _slopeMipSets[m] = RdResources.ImageSet(_rd, _p.MipmapShader, _slopeMips[m], _slopeMips[m + 1]);
            _partialMipSets[m] = RdResources.ImageSet(_rd, _p.MipmapShader, _partialMips[m], _partialMips[m + 1]);
            int s = System.Math.Max(1, size >> (m + 1));
            _mipPush[m] = PushConstants.Mipmap.CreateBuffer();
            PushConstants.Mipmap.Set(_mipPush[m], "dst_size", s, s);
        }
        PushConstants.Fft.Set(_fftPush0, "size", size);
        PushConstants.Fft.Set(_fftPush1, "size", size);
        PushConstants.Fft.Set(_fftPush1, "pass_index", 1);
        PushConstants.Spectrum.Set(_spectrumPush, "size", size);
        PushConstants.Spectrum.Set(_spectrumPush, "seed", InitialSpectrum.CascadeSeed(index));

        DisplacementTexture.TextureRdRid = _displacement;
        SlopeTexture.TextureRdRid = _slope;
        PartialsTexture.TextureRdRid = _partials;
    }

    public void RecordSpectrum(long list, CascadeConfig c)
    {
        var l = PushConstants.Spectrum;
        l.Set(_spectrumPush, "patch_size", c.PatchSize);
        l.Set(_spectrumPush, "wind_speed", c.WindSpeed);
        l.Set(_spectrumPush, "wind_direction", Mathf.DegToRad(c.WindDirection));
        l.Set(_spectrumPush, "fetch", c.Fetch);
        l.Set(_spectrumPush, "suppression", c.Suppression);
        _rd.ComputeListBindComputePipeline(list, _p.Spectrum);
        _rd.ComputeListBindUniformSet(list, _spectrumSet, 0);
        _rd.ComputeListSetPushConstant(list, _spectrumPush, (uint)_spectrumPush.Length);
        uint g = RdResources.Groups(Size, 16);
        _rd.ComputeListDispatch(list, g, g, 1);
        _rd.ComputeListAddBarrier(list);
    }

    /// <summary>Evolves the spectrum to <see cref="Time"/>, transforms it and rebuilds the mip chains (displacement too: the
    /// outer mesh rings sample it at the mip matching their vertex spacing, or they alias and swim).</summary>
    public void RecordTick(long list, CascadeConfig c)
    {
        SetFftParams(_fftPush0, c);
        SetFftParams(_fftPush1, c);
        _rd.ComputeListBindComputePipeline(list, _p.Fft);
        _rd.ComputeListBindUniformSet(list, _fftSet, 0);
        _rd.ComputeListSetPushConstant(list, _fftPush0, (uint)_fftPush0.Length);
        _rd.ComputeListDispatch(list, 1, (uint)Size, 2);
        _rd.ComputeListAddBarrier(list);
        _rd.ComputeListSetPushConstant(list, _fftPush1, (uint)_fftPush1.Length);
        _rd.ComputeListDispatch(list, (uint)Size, 1, 1);
        _rd.ComputeListAddBarrier(list);

        _rd.ComputeListBindComputePipeline(list, _p.Mipmap);
        for (int m = 0; m < Mips - 1; m++)
        {
            uint g = RdResources.Groups(System.Math.Max(1, Size >> (m + 1)), 8);
            _rd.ComputeListSetPushConstant(list, _mipPush[m], (uint)_mipPush[m].Length);
            _rd.ComputeListBindUniformSet(list, _dispMipSets[m], 0);
            _rd.ComputeListDispatch(list, g, g, 1);
            _rd.ComputeListBindUniformSet(list, _slopeMipSets[m], 0);
            _rd.ComputeListDispatch(list, g, g, 1);
            _rd.ComputeListBindUniformSet(list, _partialMipSets[m], 0);
            _rd.ComputeListDispatch(list, g, g, 1);
            _rd.ComputeListAddBarrier(list);
        }
    }

    void SetFftParams(byte[] push, CascadeConfig c)
    {
        var l = PushConstants.Fft;
        l.Set(push, "time", (float)Time);
        l.Set(push, "patch_size", c.PatchSize);
        l.Set(push, "height_scale", c.HeightScale);
        l.Set(push, "choppiness", c.Choppiness);
    }

    public void Free()
    {
        DisplacementTexture.TextureRdRid = default;
        SlopeTexture.TextureRdRid = default;
        PartialsTexture.TextureRdRid = default;
        RdResources.Free(_rd, ref _spectrumSet);
        RdResources.Free(_rd, ref _fftSet);
        for (int m = 0; m < Mips - 1; m++)
        {
            RdResources.Free(_rd, ref _dispMipSets[m]);
            RdResources.Free(_rd, ref _slopeMipSets[m]);
            RdResources.Free(_rd, ref _partialMipSets[m]);
        }
        for (int m = 0; m < Mips; m++)
        {
            RdResources.Free(_rd, ref _dispMips[m]);
            RdResources.Free(_rd, ref _slopeMips[m]);
            RdResources.Free(_rd, ref _partialMips[m]);
        }
        RdResources.Free(_rd, ref _h0);
        RdResources.Free(_rd, ref _specA);
        RdResources.Free(_rd, ref _specB);
        RdResources.Free(_rd, ref _displacement);
        RdResources.Free(_rd, ref _slope);
        RdResources.Free(_rd, ref _partials);
    }
}
