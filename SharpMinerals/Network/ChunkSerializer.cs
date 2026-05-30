using SharpMinerals.Level;
using SharpMinerals.Math;
using SharpMinerals.Network.Buffers;
using SharpMinerals.Network.Messages;
using SharpMinerals.Network.Nbt;
using SharpMinerals.Network.Protocols.JE763;

namespace SharpMinerals.Network;

/// <summary>
/// Serializes one world column into the "Chunk Data and Update Light" packet a
/// 1.20.1 client expects. A vanilla column is 16×384×16 — 24 sections from y=-64 —
/// each holding a block-state and a biome paletted-container. SharpMinerals stores
/// blocks in cuboid 16³ chunks, so this assembles a vanilla column on the fly by
/// reading <see cref="World.GetBlock"/> and mapping to vanilla state ids.
/// See https://minecraft.wiki/w/Java_Edition_protocol#Chunk_Data_and_Update_Light.
/// </summary>
public static class ChunkSerializer {
    const int MinY = -64;
    const int SectionCount = 24;          // 384 / 16
    const int LightSectionCount = SectionCount + 2; // one padding section above and below
    const int BiomeId = 0;                // single biome: minecraft:badlands (1.20.1 registry id 0) — its reddish tint makes the flat world distinctive

    /// <summary>Builds the Chunk Data packet for the column at (chunkX, chunkZ).</summary>
    public static ChunkDataS2C Build(World world, int chunkX, int chunkZ) {
        using var ms = new MemoryStream();
        var s = new MinecraftStream(ms, leaveOpen: true);

        s.WriteInt(chunkX);
        s.WriteInt(chunkZ);
        WriteHeightmaps(s);

        byte[] sections = BuildSections(world, chunkX, chunkZ);
        s.WriteVarInt(sections.Length);
        s.Write(sections, 0, sections.Length);

        s.WriteVarInt(0);                 // block entity count
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

    static byte[] BuildSections(World world, int chunkX, int chunkZ) {
        using var ms = new MemoryStream();
        var s = new MinecraftStream(ms, leaveOpen: true);

        var states = new int[16 * 16 * 16];
        for (int sy = 0; sy < SectionCount; sy++) {
            int nonAir = 0;
            for (int y = 0; y < 16; y++) {
                long worldY = MinY + sy * 16 + y;
                for (int z = 0; z < 16; z++)
                    for (int x = 0; x < 16; x++) {
                        var block = world.GetBlock(new Vector3i(chunkX * 16 + x, worldY, chunkZ * 16 + z));
                        states[(y << 8) | (z << 4) | x] = VanillaMapping.StateId(block);
                        if (!block.IsAir) nonAir++;
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
    /// Full sky light, no block light. Light masks carry one bit per light section
    /// (sections plus a padding section at each end); a set bit means an array follows.
    /// Note: the combined Chunk Data and Update Light packet has NO "Trust Edges"
    /// field — that exists only in the standalone Update Light packet.
    /// </summary>
    static void WriteLight(MinecraftStream s) {
        long allSections = (1L << LightSectionCount) - 1;
        WriteBitSet(s, allSections);          // sky light mask: every section lit
        WriteBitSet(s, 0);                    // block light mask: none
        WriteBitSet(s, 0);                    // empty sky light mask: none
        WriteBitSet(s, allSections);          // empty block light mask: all zero

        s.WriteVarInt(LightSectionCount);     // one full sky-light array per section
        for (int i = 0; i < LightSectionCount; i++) {
            s.WriteVarInt(2048);
            for (int b = 0; b < 2048; b++) s.WriteUByte(0xFF); // every nibble = light 15
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
