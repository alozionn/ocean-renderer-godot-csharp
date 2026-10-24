using System;
using Godot;
using Ocean.Core.Shore;
using Ocean.Simulation;

namespace Ocean.Surface;

/// <summary>
/// The shore data on the GPU, one copy for everything that reads it: the surface and spray materials (as Texture2Drd)
/// and the foam field compute (uniform set 1: the textures plus a small block of shore foam settings). Textures are
/// replaced, not resized, when the sea floor or the bodies change; 1 x 1 placeholders stand in while there is none.
/// </summary>
internal sealed class ShoreTextures
{
    readonly RenderingDevice _rd;
    readonly Rid _shader;
    Rid _floor, _pages, _index, _buffer, _sampler, _nearest, _set;
    readonly float[] _block = new float[20];
    bool _blockDirty = true, _setDirty = true;

    public readonly Texture2Drd Floor = new(), Pages = new(), Index = new();
    public Vector4 FloorRect { get; private set; }
    public Vector4 BodyRect { get; private set; }
    public float BodySamples { get; private set; } = 65f;

    public ShoreTextures(RenderingDevice rd, Rid foamShader)
    {
        _rd = rd;
        _shader = foamShader;
        _floor = Upload(1, 1, RenderingDevice.DataFormat.R32G32B32A32Sfloat, new float[4]);
        _pages = Upload(1, 1, RenderingDevice.DataFormat.R32G32B32A32Sfloat, new float[4]);
        _index = Upload(1, 1, RenderingDevice.DataFormat.R32Sfloat, new float[1]);
        Floor.TextureRdRid = _floor;
        Pages.TextureRdRid = _pages;
        Index.TextureRdRid = _index;
        _buffer = _rd.UniformBufferCreate((uint)(_block.Length * sizeof(float)));
        _sampler = _rd.SamplerCreate(new RDSamplerState
        {
            MagFilter = RenderingDevice.SamplerFilter.Linear,
            MinFilter = RenderingDevice.SamplerFilter.Linear,
            RepeatU = RenderingDevice.SamplerRepeatMode.ClampToEdge,
            RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge,
        });
        _nearest = _rd.SamplerCreate(new RDSamplerState());
    }

    /// <summary>The terrain's sea floor (null: none). Returns the rect for the materials.</summary>
    public void SetFloor(SeaFloor floor)
    {
        if (floor == null)
        {
            FloorRect = Vector4.Zero;
            _blockDirty = true;
            return;
        }
        int n = floor.Width * floor.Depth;
        var texels = new float[n * 4];
        for (int i = 0; i < n; i++)
        {
            texels[i * 4] = floor.WaterDepth[i];
            texels[i * 4 + 1] = floor.ShoreDistance[i];
            texels[i * 4 + 2] = floor.OpenSea[i];
        }
        Replace(ref _floor, Floor, Upload(floor.Width, floor.Depth, RenderingDevice.DataFormat.R32G32B32A32Sfloat, texels));
        // Sample centres sit half a cell inside the texture's edges.
        float half = floor.CellSize * 0.5f;
        FloorRect = new Vector4(floor.Origin.X - half, floor.Origin.Y - half,
            1f / (floor.Width * floor.CellSize), 1f / (floor.Depth * floor.CellSize));
        _blockDirty = true;
    }

    /// <summary>The folded stationary bodies (null: none).</summary>
    public void SetBodies(BodyLayer layer)
    {
        if (layer == null)
        {
            BodyRect = Vector4.Zero;
            _blockDirty = true;
            return;
        }
        var index = new float[layer.Index.Length];
        for (int i = 0; i < index.Length; i++) index[i] = layer.Index[i];
        Replace(ref _pages, Pages, Upload(layer.AtlasWidth, layer.AtlasHeight, RenderingDevice.DataFormat.R32G32B32A32Sfloat, layer.Atlas));
        Replace(ref _index, Index, Upload(layer.IndexWidth, layer.IndexDepth, RenderingDevice.DataFormat.R32Sfloat, index));
        BodySamples = layer.Samples;
        BodyRect = new Vector4(layer.Origin.X, layer.Origin.Y, layer.PageSize, layer.PagesPerRow);
        _blockDirty = true;
    }

    /// <summary>The foam settings the compute reads, from the surface material's shore uniforms.</summary>
    public void SetFoam(Vector4 calmDepth, Vector4 nearShore, float breakingRatio, float breakingGain)
    {
        void Put(int at, Vector4 v)
        {
            _block[at] = v.X;
            _block[at + 1] = v.Y;
            _block[at + 2] = v.Z;
            _block[at + 3] = v.W;
        }
        Put(8, calmDepth);
        Put(12, nearShore);
        _block[16] = breakingRatio;
        _block[17] = breakingGain;
        _blockDirty = true;
    }

    /// <summary>Writes pending changes; call outside any compute list. Returns the foam field's uniform set 1.</summary>
    public Rid Flush()
    {
        if (_blockDirty)
        {
            _block[0] = FloorRect.X; _block[1] = FloorRect.Y; _block[2] = FloorRect.Z; _block[3] = FloorRect.W;
            _block[4] = BodyRect.X; _block[5] = BodyRect.Y; _block[6] = BodyRect.Z; _block[7] = BodyRect.W;
            _block[18] = BodySamples;
            _block[19] = FloorRect.Z > 0f || BodyRect.Z > 0f ? 1f : 0f;
            var bytes = new byte[_block.Length * sizeof(float)];
            Buffer.BlockCopy(_block, 0, bytes, 0, bytes.Length);
            _rd.BufferUpdate(_buffer, 0, (uint)bytes.Length, bytes);
            _blockDirty = false;
        }
        if (_setDirty || !_set.IsValid || !_rd.UniformSetIsValid(_set))
        {
            if (_set.IsValid && _rd.UniformSetIsValid(_set)) _rd.FreeRid(_set);
            var buffer = new RDUniform { UniformType = RenderingDevice.UniformType.UniformBuffer, Binding = 3 };
            buffer.AddId(_buffer);
            _set = _rd.UniformSetCreate(new Godot.Collections.Array<RDUniform>
            {
                RdResources.Sampled2D(0, _sampler, _floor),
                RdResources.Sampled2D(1, _sampler, _pages),
                RdResources.Sampled2D(2, _nearest, _index),
                buffer,
            }, _shader, 1);
            _setDirty = false;
        }
        return _set;
    }

    Rid Upload(int width, int height, RenderingDevice.DataFormat format, float[] data)
    {
        var bytes = new byte[data.Length * sizeof(float)];
        Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
        var fmt = new RDTextureFormat
        {
            Format = format, Width = (uint)width, Height = (uint)height, Depth = 1, ArrayLayers = 1, Mipmaps = 1,
            TextureType = RenderingDevice.TextureType.Type2D,
            UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanUpdateBit,
        };
        return _rd.TextureCreate(fmt, new RDTextureView(), new Godot.Collections.Array<byte[]> { bytes });
    }

    void Replace(ref Rid current, Texture2Drd wrapper, Rid next)
    {
        wrapper.TextureRdRid = next;
        if (_set.IsValid && _rd.UniformSetIsValid(_set)) _rd.FreeRid(_set);
        _set = default;
        RdResources.Free(_rd, ref current);
        current = next;
        _setDirty = true;
    }

    public void Free()
    {
        Floor.TextureRdRid = default;
        Pages.TextureRdRid = default;
        Index.TextureRdRid = default;
        if (_set.IsValid && _rd.UniformSetIsValid(_set)) _rd.FreeRid(_set);
        RdResources.Free(_rd, ref _floor);
        RdResources.Free(_rd, ref _pages);
        RdResources.Free(_rd, ref _index);
        RdResources.Free(_rd, ref _buffer);
        RdResources.Free(_rd, ref _sampler);
        RdResources.Free(_rd, ref _nearest);
    }
}
