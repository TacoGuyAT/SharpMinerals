using System.IO.Compression;
using SharpMinerals.Math;
using SharpMinerals.Network.Messages;
using World = SharpMinerals.Level.World;

namespace SharpMinerals.Network;

/// <summary>
/// Builds a 1.5.2 (protocol 61) Chunk Data (0x33) column, the pre-Anvil "SMP map" format: up to sixteen
/// 16³ sections by bitmask. Payload order across all present sections: block ids, metadata, block light,
/// sky light, then a 256-byte biome array; whole payload zlib-compressed. Light is full-bright; metadata/add unmodeled.
/// </summary>
public static class LegacyChunkSerializer {
    const int Sections = 16;                    // 256-tall column
    const int BlocksPerSection = 16 * 16 * 16;  // 4096
    const int NibblesPerSection = BlocksPerSection / 2; // 2048
    const byte PlainsBiome = 1;

    public static LegacyChunkDataS2C Build(ITypeMapper types, World world, int cx, int cz) {
        int baseX = cx << 4, baseZ = cz << 4;

        var present = new List<byte[]>(); // block-id arrays for non-empty sections, low → high
        int primaryBitmap = 0;

        for (int sy = 0; sy < Sections; sy++) {
            var ids = new byte[BlocksPerSection];
            bool any = false;
            for (int y = 0; y < 16; y++) {
                int worldY = (sy << 4) | y;
                for (int z = 0; z < 16; z++)
                    for (int x = 0; x < 16; x++) {
                        var block = world.GetBlock(new Vector3i(baseX + x, worldY, baseZ + z));
                        if (block.IsAir) continue;
                        ids[(y << 8) | (z << 4) | x] = (byte)types.StateId(block); // flat 1.5.2 id (<256)
                        any = true;
                    }
            }
            if (any) { present.Add(ids); primaryBitmap |= 1 << sy; }
        }

        int n = present.Count;
        var zero = new byte[NibblesPerSection];                              // 0x00 metadata / block light
        var full = new byte[NibblesPerSection]; Array.Fill(full, (byte)0xFF); // 0xFF = sky light 15 (full bright)

        using var payload = new MemoryStream();
        foreach (var ids in present) payload.Write(ids, 0, BlocksPerSection); // block ids
        for (int i = 0; i < n; i++) payload.Write(zero, 0, NibblesPerSection); // metadata
        for (int i = 0; i < n; i++) payload.Write(zero, 0, NibblesPerSection); // block light
        for (int i = 0; i < n; i++) payload.Write(full, 0, NibblesPerSection); // sky light (overworld)
        // No add array (all ids < 256). Biome column (sent because ground-up-continuous).
        var biome = new byte[256]; Array.Fill(biome, PlainsBiome);
        payload.Write(biome, 0, biome.Length);

        return new LegacyChunkDataS2C(cx, cz, GroundUpContinuous: true, primaryBitmap, AddBitmap: 0,
            ZlibCompress(payload.ToArray()));
    }

    static byte[] ZlibCompress(byte[] data) {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(data, 0, data.Length);
        return ms.ToArray();
    }
}
