using SharpMinerals.Blocks.Descriptors;
using SharpMinerals.Components;
using SharpMinerals.Items;

namespace SharpMinerals.Blocks;

/// <summary>
/// The block registry. Air is id 0; every block is also an item. A block <b>places and
/// drops itself by default</b>, so only the exceptions need spelling out — each block's
/// whole definition is one line. Drop overrides use a lazy <c>() =&gt;</c> lambda so a block
/// can reference a kind that's registered later.
/// </summary>
public static class BlockRegistry {
    static readonly List<BlockType> byId = new();
    static readonly Dictionary<string, BlockType> byName = [];
    static bool frozen;

    static BlockType Define(string name, bool isAir = false) {
        if (frozen)
            throw new InvalidOperationException(
                $"BlockRegistry is frozen — register block \"{name}\" during mod OnInitialize, before the palette is built.");
        if (byName.ContainsKey(name))
            throw new ArgumentException($"A block named \"{name}\" is already registered.", nameof(name));
        var block = new BlockType(byId.Count, name, isAir);
        byId.Add(block);
        byName[name] = block;
        return block;
    }

    /// <summary>Registers a new block, returning it for fluent composition (<c>.DropSelf().Add(...)</c>).
    /// For mods — call from <c>Mod.OnInitialize</c>; throws once the registry is <see cref="Freeze">frozen</see>.
    /// The wire id of a modded block falls back to stone until a type-mapping component is added.</summary>
    public static BlockType Register(string name) => Define(name);

    /// <summary>Seals the registry so no further blocks can be added — the host calls this after mods
    /// have initialised and before the protocols/type-mappers snapshot the palette.</summary>
    public static void Freeze() => frozen = true;

    public static readonly BlockType Air         = Define("air", isAir: true);
    public static readonly BlockType Bedrock     = Define("bedrock");
    public static readonly BlockType Cobblestone = Define("cobblestone").DropSelf();
    public static readonly BlockType Stone       = Define("stone").Add(new DropBlockDescriptor(() => new ItemStack(Cobblestone)));
    public static readonly BlockType Dirt        = Define("dirt").DropSelf();
    public static readonly BlockType GrassBlock  = Define("grass_block").Add(new DropBlockDescriptor(() => new ItemStack(Dirt)));
    public static readonly BlockType Chest       = Define("chest").DropSelf().Add(new ContainerBlockDescriptor(27), new StatesBlockDescriptor(State.Facing));
    public static readonly BlockType Wool        = Define("wool").DropSelf().Add(new StatesBlockDescriptor(State.Color));
    public static readonly BlockType Sand        = Define("sand").DropSelf().Add(new FallingBlockDescriptor());
    public static readonly BlockType Gravel      = Define("gravel").DropSelf().Add(new FallingBlockDescriptor());

    public static IReadOnlyList<BlockType> All => byId;
    public static BlockType FromId(int id) => byId[id];
    public static BlockType FromState(ushort state) => byId[state];
    public static BlockType? FromName(string name) => byName.GetValueOrDefault(name);
}
