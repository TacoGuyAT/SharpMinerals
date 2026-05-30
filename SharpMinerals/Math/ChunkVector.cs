using System.Runtime.CompilerServices;

namespace SharpMinerals.Math;

/// <summary>
/// Stores local position of blocks inside of chunks.
/// Unlike in Minecraft, SharpMinerals has cuboid chunks, so X/Y/Z each fit in a
/// nibble (0-15); the remaining nibble carries the block light level. The four are
/// packed into a single <see cref="ushort"/>: X = bits 0-3, Y = 4-7, Z = 8-11,
/// Light = 12-15.
/// </summary>
public struct ChunkVector {
    ushort bits;

    public byte X {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (byte)(bits & 0x0F);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => bits = (ushort)((bits & ~0x000F) | (value & 0x0F));
    }
    public byte Y {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (byte)((bits >> 4) & 0x0F);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => bits = (ushort)((bits & ~0x00F0) | ((value & 0x0F) << 4));
    }
    public byte Z {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (byte)((bits >> 8) & 0x0F);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => bits = (ushort)((bits & ~0x0F00) | ((value & 0x0F) << 8));
    }
    public byte Light {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (byte)((bits >> 12) & 0x0F);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => bits = (ushort)((bits & ~0xF000) | ((value & 0x0F) << 12));
    }
}
