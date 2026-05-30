using SharpMinerals.Blocks.Components;
using SharpMinerals.Items.Components;

namespace SharpMinerals.Blocks;

/// <summary>
/// The block registry. Air is always id 0. Every block is also an item (see
/// <see cref="BlockType"/>). Built in two phases to dodge the static-init-order trap:
/// the field initializers construct every flyweight first (Phase 1), then the static
/// constructor wires up cross-referencing components like drops/placement (Phase 2),
/// which runs only after all field initializers per the C# spec.
/// </summary>
public static class BlockRegistry {
    static readonly List<BlockType> byId = new();
    static readonly Dictionary<string, BlockType> byName = new();

    // ── Phase 1: construct flyweights (identity + air flag/marker) ──
    // The Java Edition wire ids these map to are a network concern — see the JE763
    // VanillaMapping translator, keyed by block name.
    static BlockType Register(string name, bool isAir = false) {
        var block = new BlockType(byId.Count, name, isAir);
        if (isAir) block.With(new Air());
        byId.Add(block);
        byName[name] = block;
        return block;
    }

    public static readonly BlockType Air = Register("air", isAir: true);
    public static readonly BlockType Bedrock = Register("bedrock");
    public static readonly BlockType Stone = Register("stone");
    public static readonly BlockType Dirt = Register("dirt");
    public static readonly BlockType GrassBlock = Register("grass_block");
    public static readonly BlockType Cobblestone = Register("cobblestone");
    public static readonly BlockType Chest = Register("chest");

    // ── Phase 2: wire cross-referencing components (drops, placement) ──
    static BlockRegistry() {
        Stone.With(new Drops(Cobblestone));
        Dirt.With(new Drops(Dirt));
        GrassBlock.With(new Drops(Dirt));
        Cobblestone.With(new Drops(Cobblestone));

        // Every solid block places itself when held (it IS an item).
        Bedrock.With(new Placeable(Bedrock));
        Stone.With(new Placeable(Stone));
        Dirt.With(new Placeable(Dirt));
        GrassBlock.With(new Placeable(GrassBlock));
        Cobblestone.With(new Placeable(Cobblestone));

        // A chest places itself, drops itself when broken, and opens a 27-slot container.
        Chest.With(new Placeable(Chest));
        Chest.With(new Drops(Chest));
        Chest.With(new Container(27));
    }

    public static IReadOnlyList<BlockType> All => byId;
    public static BlockType FromId(int id) => byId[id];
    public static BlockType FromState(ushort state) => byId[state];
    public static BlockType? FromName(string name) => byName.GetValueOrDefault(name);
}
