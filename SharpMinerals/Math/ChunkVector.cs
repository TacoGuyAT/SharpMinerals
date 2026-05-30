using System.Runtime.CompilerServices;

namespace SharpMinerals.Math;

/// <summary>
/// Stores local position of blocks inside of chunks.
/// Unlike in Minecraft, SharpMinerals has cuboid chunks.
/// </summary>
public struct ChunkVector {
    ushort bits;

    public byte X {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (byte)(bits & 0x000F);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set {
            bits |= 0x000F;
            bits &= (byte)(value & 0x0F);
        }
    }
    public ushort Y {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (byte)((bits >> 4) & 0x00F0);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set {
            bits |= 0x00F0;
            bits &= (byte)((value & 0x0F) << 4);
        }
    }
    public byte Z {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (byte)((bits >> 8) & 0x0F00);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set {
            bits |= 0x0F00;
            bits &= (byte)((value & 0x0F) << 8);
        }
    }
    public byte Light {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (byte)((bits >> 12) & 0xF000);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set {
            bits |= 0xF000;
            bits &= (byte)((value & 0x0F) << 12);
        }
    }
}
