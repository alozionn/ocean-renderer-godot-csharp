using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Ocean.Core.Gpu;

public enum PushField { Float, Int, UInt, Vec2, IVec2, Vec4 }

/// <summary>std430 push-constant block layout with offsets by field name. Blocks must be padded to a multiple of 16 bytes.</summary>
public sealed class PushConstantLayout
{
    readonly Dictionary<string, int> _offsets = new();
    public IReadOnlyList<(string Name, PushField Type, int Offset)> Fields { get; }
    public int Size { get; }

    public PushConstantLayout(params (string Name, PushField Type)[] fields)
    {
        var list = new List<(string, PushField, int)>();
        int offset = 0;
        foreach (var (name, type) in fields)
        {
            int align = SizeOf(type);
            offset = (offset + align - 1) / align * align;
            list.Add((name, type, offset));
            _offsets[name] = offset;
            offset += SizeOf(type);
        }
        Fields = list;
        Size = offset;
        if (Size % 16 != 0) throw new InvalidOperationException($"Push-constant block of {Size} bytes is not a multiple of 16.");
    }

    /// <summary>std430 size, which is also the alignment for these types.</summary>
    public static int SizeOf(PushField t) => t switch { PushField.Vec2 or PushField.IVec2 => 8, PushField.Vec4 => 16, _ => 4 };

    public byte[] CreateBuffer() => new byte[Size];

    public void Set(byte[] b, string name, float v) => BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(_offsets[name]), v);
    public void Set(byte[] b, string name, int v) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(_offsets[name]), v);
    public void Set(byte[] b, string name, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(_offsets[name]), v);

    public void Set(byte[] b, string name, float x, float y)
    {
        var s = b.AsSpan(_offsets[name]);
        BinaryPrimitives.WriteSingleLittleEndian(s, x);
        BinaryPrimitives.WriteSingleLittleEndian(s[4..], y);
    }

    public void Set(byte[] b, string name, int x, int y)
    {
        var s = b.AsSpan(_offsets[name]);
        BinaryPrimitives.WriteInt32LittleEndian(s, x);
        BinaryPrimitives.WriteInt32LittleEndian(s[4..], y);
    }

    public void Set(byte[] b, string name, float x, float y, float z, float w)
    {
        var s = b.AsSpan(_offsets[name]);
        BinaryPrimitives.WriteSingleLittleEndian(s, x);
        BinaryPrimitives.WriteSingleLittleEndian(s[4..], y);
        BinaryPrimitives.WriteSingleLittleEndian(s[8..], z);
        BinaryPrimitives.WriteSingleLittleEndian(s[12..], w);
    }
}
