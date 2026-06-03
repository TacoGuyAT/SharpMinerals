using SharpMinerals.Blocks;
using SharpMinerals.Blocks.Descriptors;
using SharpMinerals.Components;
using SharpMinerals.Items;
using SharpMinerals.Modding;

namespace SharpMinerals.Minecraft;

/// <summary>
/// The vanilla content "mod": registers the default blocks and items that used to be hardcoded built-ins in the
/// core registries. Because <see cref="ModLoader"/> sets <c>ModContent.CurrentNamespace = ModId</c> ("minecraft")
/// around <see cref="OnInitialize"/>, every <c>BlockRegistry.Register</c>/<c>ItemRegistry.Register</c> call here
/// lands in the <c>minecraft</c> namespace — no namespace forcing needed. The host must load this FIRST so its
/// blocks get the lowest palette ids right after the engine's air/missing.
/// </summary>
[ModInfo("minecraft", "1.0.0", ["SharpMinerals"], TargetServerVersion = "0.1.0")]
public sealed class MinecraftMod : Mod {
    public override void OnInitialize() {
        // Register before the blocks that reference them in drop lambdas (lambdas are lazy, but keep the order tidy).
        Vanilla.Bedrock     = BlockRegistry.Register("bedrock");
        Vanilla.Cobblestone = BlockRegistry.Register("cobblestone").DropSelf();
        Vanilla.Stone       = BlockRegistry.Register("stone").Add(new DropBlockDescriptor(() => new ItemStack(Vanilla.Cobblestone)));
        Vanilla.Dirt        = BlockRegistry.Register("dirt").DropSelf();
        Vanilla.GrassBlock  = BlockRegistry.Register("grass_block").Add(new DropBlockDescriptor(() => new ItemStack(Vanilla.Dirt)));
        Vanilla.Chest       = BlockRegistry.Register("chest").DropSelf().Add(new ContainerBlockDescriptor(27), new StatesBlockDescriptor(State.Facing));
        Vanilla.Wool        = BlockRegistry.Register("wool").DropSelf().Add(new StatesBlockDescriptor(State.Color));
        Vanilla.Sand        = BlockRegistry.Register("sand").DropSelf().Add(new FallingBlockDescriptor());
        Vanilla.Gravel      = BlockRegistry.Register("gravel").DropSelf().Add(new FallingBlockDescriptor());
        Vanilla.RedSand     = BlockRegistry.Register("red_sand").DropSelf();
        // Red sand falls like sand — borrow the stateless falling behaviour (its drop stays its own, via DropSelf).
        Vanilla.RedSand.Copy<FallingBlockDescriptor>(Vanilla.Sand);

        Vanilla.Stick       = ItemRegistry.Register("stick");

        // Vanilla wire-id mappings (per protocol version) for the data-driven TypeMapper — the core knows no ids.
        WireMappings.Register();
    }
}

/// <summary>The registered vanilla flyweights, assigned during <see cref="MinecraftMod.OnInitialize"/>. They are
/// null until the mod loads — reference them only after the host/test bootstrap has loaded <see cref="MinecraftMod"/>.</summary>
public static class Vanilla {
    public static BlockType Bedrock { get; internal set; } = null!;
    public static BlockType Cobblestone { get; internal set; } = null!;
    public static BlockType Stone { get; internal set; } = null!;
    public static BlockType Dirt { get; internal set; } = null!;
    public static BlockType GrassBlock { get; internal set; } = null!;
    public static BlockType Chest { get; internal set; } = null!;
    public static BlockType Wool { get; internal set; } = null!;
    public static BlockType Sand { get; internal set; } = null!;
    public static BlockType Gravel { get; internal set; } = null!;
    public static BlockType RedSand { get; internal set; } = null!;
    public static ItemType Stick { get; internal set; } = null!;
}
