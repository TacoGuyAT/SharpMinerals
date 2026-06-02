using Microsoft.Win32.SafeHandles;
using SharpMinerals.Blocks.Descriptors;
using SharpMinerals.Level;
using SharpMinerals.Math;
using SharpMinerals.Network.Buffers;
using SharpMinerals.Network.Messages;
using SharpMinerals.Network.Nbt;

namespace SharpMinerals.Network;

/// <summary>
/// Serializes one world column into the 1.20.1 "Chunk Data and Update Light" packet (16×384×16,
/// 24 sections from y=-64), assembled on the fly from <see cref="World.GetBlock"/> and vanilla state ids.
/// </summary>
public static class ChunkSerializer {
    const int MinY = -64;
    const int MinSectionY = MinY / 16;    // chunk-cube Y of the bottom section (-4)
    const int SectionCount = 24;          // 384 / 16
    const int LightSectionCount = SectionCount + 2; // one padding section above and below
    const int BiomeId = 0;                // minecraft:badlands (1.20.1 registry id 0)

    // A full-bright sky-light section (4096 nibbles = 2048 bytes, all 0xFF), written verbatim per section.
    static readonly byte[] FullSkyLight = CreateFullSkyLight();
    static byte[] CreateFullSkyLight() { var a = new byte[2048]; Array.Fill(a, (byte)0xFF); return a; }

    /// <summary>Builds the Chunk Data packet for the column at (chunkX, chunkZ), mapping ids via <paramref name="types"/>.</summary>
    public static ChunkDataS2C Build(ITypeMapper types, World world, int chunkX, int chunkZ) {
        using var ms = new MemoryStream();
        var s = new MinecraftStream(ms, leaveOpen: true);

        s.WriteInt(chunkX);
        s.WriteInt(chunkZ);
        WriteHeightmaps(s);

        // Block entities derived from block states, not the lazily-created server-side BlockEntity instances,
        // so a placed-but-unopened chest still renders.
        var blockEntities = new List<(byte Packed, int Y, int TypeId)>();
        byte[] sections = BuildSections(types, world, chunkX, chunkZ, blockEntities);
        s.WriteVarInt(sections.Length);
        s.Write(sections, 0, sections.Length);

        // Each block entity: packed local XZ byte, world-Y short, block-entity-type id, then data NBT (empty).
        s.WriteVarInt(blockEntities.Count);
        foreach (var (packed, y, typeId) in blockEntities) {
            s.WriteUByte(packed);
            s.WriteShort((short)y);
            s.WriteVarInt(typeId);
            new NbtCompound().WriteRoot(s); // 1.20.1 named root
        }

        WriteLight(s);

        return new ChunkDataS2C(ms.ToArray());
    }

    static void WriteHeightmaps(MinecraftStream s) {
        // MOTION_BLOCKING: 256 entries of 9 bits, packed non-spanning (7 per long → 37 longs).
        // Flat surface sits on grass at GrassY, so the lowest motion-blocking air is GrassY+1.
        int height = FlatChunkGenerator.GrassY + 1 - MinY;
        long[] packed = PackBits(Enumerable.Repeat(height, 256).ToArray(), 9);
        new NbtCompound()
            .Put("MOTION_BLOCKING", new NbtLongArray(packed))
            .WriteRoot(s);
    }

    static byte[] BuildSections(ITypeMapper types, World world, int chunkX, int chunkZ,
                                List<(byte Packed, int Y, int TypeId)> blockEntities) {
        using var ms = new MemoryStream();
        var s = new MinecraftStream(ms, leaveOpen: true);

        var states = new int[16 * 16 * 16];
        for (int sy = 0; sy < SectionCount; sy++) {
            // A vanilla section IS one server chunk cube — fetch it once and read cells directly, rather than
            // a GetBlock/GetChunk dictionary lookup (+ Vector3i alloc) per cell.
            var cube = world.GetChunk(new Vector3i(chunkX, MinSectionY + sy, chunkZ));
            int sectionBaseY = (MinSectionY + sy) * 16;
            int nonAir = 0;
            for (int y = 0; y < 16; y++) {
                for (int z = 0; z < 16; z++) {
                    for (int x = 0; x < 16; x++) {
                        var block = cube.GetBlock(x, y, z);
                        // Stateful blocks (chest facing, …) map via their stored state; the rest by type.
                        states[(y << 8) | (z << 4) | x] =
                            block.Has<StatesBlockDescriptor>() && cube.GetBlockState(x, y, z) is { } bs
                                ? types.StateId(bs)
                                : types.StateId(block);
                        if (block.IsAir) continue;
                        nonAir++;

                        // Block entities are derived from the block itself, not the lazily-created server-side
                        // BlockEntity instances (empty for a placed-but-unopened chest). IsBlockEntity is a cheap
                        // cached gate so the wire-id lookup only runs for the rare block that carries one.
                        if (block.IsBlockEntity)
                            blockEntities.Add(((byte)((x << 4) | z), sectionBaseY + y, types.BlockEntityTypeId(block)));
                    }
                }
            }

            s.WriteShort((short)nonAir);
            WritePalettedStates(s, states);
            WriteSingleValuedBiome(s);
        }

        return ms.ToArray();
    }

    /// <summary>Writes a block-state paletted container (single-valued or indirect).</summary>
    static void WritePalettedStates(MinecraftStream s, int[] states) {
        var palette = states.Distinct().ToArray();

        if (palette.Length == 1) {
            s.WriteUByte(0);                  // 0 bits per entry → single value
            s.WriteVarInt(palette[0]);
            s.WriteVarInt(0);                 // no data array
            return;
        }

        int bits = System.Math.Max(4, BitsFor(palette.Length));
        var indexOf = new Dictionary<int, int>(palette.Length);
        for (int i = 0; i < palette.Length; i++) indexOf[palette[i]] = i;

        s.WriteUByte((byte)bits);
        s.WriteVarInt(palette.Length);
        foreach (var state in palette) s.WriteVarInt(state);

        var indices = new int[states.Length];
        for (int i = 0; i < states.Length; i++) indices[i] = indexOf[states[i]];

        long[] packed = PackBits(indices, bits);
        s.WriteVarInt(packed.Length);
        foreach (var l in packed) s.WriteLong(l);
    }

    static void WriteSingleValuedBiome(MinecraftStream s) {
        s.WriteUByte(0);                      // single-valued palette
        s.WriteVarInt(BiomeId);
        s.WriteVarInt(0);
    }

    /// <summary>
    /// Full sky light, no block light; masks carry one bit per light section, set bit = array follows.
    /// The combined Chunk Data + Update Light packet has NO "Trust Edges" field (only the standalone one does).
    /// </summary>
    static void WriteLight(MinecraftStream s) {
        long allSections = (1L << LightSectionCount) - 1;
        WriteBitSet(s, allSections);          // sky light mask: every section lit
        WriteBitSet(s, 0);                    // block light mask: none
        WriteBitSet(s, 0);                    // empty sky light mask: none
        WriteBitSet(s, allSections);          // empty block light mask: all zero

        s.WriteVarInt(LightSectionCount);     // one full sky-light array per section
        for (int i = 0; i < LightSectionCount; i++) {
            s.WriteVarInt(FullSkyLight.Length);
            s.Write(FullSkyLight, 0, FullSkyLight.Length); // every nibble = light 15
        }

        s.WriteVarInt(0);                     // no block light arrays
    }

    static void WriteBitSet(MinecraftStream s, long bits) {
        if (bits == 0) {
            s.WriteVarInt(0);
            return;
        }
        s.WriteVarInt(1);
        s.WriteLong(bits);
    }

    // ── Bit packing (non-spanning: entries never cross a long boundary) ──────
    static long[] PackBits(int[] values, int bitsPerEntry) {
        int perLong = 64 / bitsPerEntry;
        int longCount = (values.Length + perLong - 1) / perLong;
        long mask = (1L << bitsPerEntry) - 1;

        var data = new long[longCount];
        for (int i = 0; i < values.Length; i++) {
            int longIndex = i / perLong;
            int offset = (i % perLong) * bitsPerEntry;
            data[longIndex] |= (values[i] & mask) << offset;
        }
        return data;
    }

    static int BitsFor(int count) {
        int bits = 0;
        while ((1 << bits) < count) bits++;
        return bits;
    }
}
