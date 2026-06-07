using SharpMinerals.Blocks;
using SharpMinerals.Blocks.Descriptors;
using SharpMinerals.Items;
using SharpMinerals.Network;
using SharpMinerals.Network.Protocols.JE762;
using SharpMinerals.Network.Protocols.JE763;
using SharpMinerals.Vanilla;
using SharpMinerals.Vanilla.Data;
using Xunit;

namespace SharpMinerals.Tests;

/// <summary>
/// Verifies the data-driven content registration: the full vanilla block/item palette is registered from
/// minecraft-data, wire ids match the data per protocol version (catching the 1.19.4 -> 1.20.1 state-id shift),
/// and the hand-bound <see cref="VanillaMod"/> accessors resolve to the right blocks with their behavior intact.
/// </summary>
public class DataRegistrationTests {
    static readonly MinecraftData Data = MinecraftData.Load();
    static readonly TypeMapper V763 = new(typeof(ProtocolJE763));
    static readonly TypeMapper V762 = new(typeof(ProtocolJE762));

    [Fact]
    public void RegistersFullVanillaPalette() {
        // The whole 1.20.1 set, minus the engine air family and the 16 *_wool blocks, plus engine air/missing/wool.
        Assert.True(BlockRegistry.All.Count > 900, $"expected ~1000 blocks, got {BlockRegistry.All.Count}");
        Assert.True(ItemRegistry.All.Count > 1100, $"expected ~1250 items, got {ItemRegistry.All.Count}");

        // A spread of blocks that were never hand-registered before now exist with a real identity.
        foreach (var name in new[] { "diamond_block", "oak_planks", "granite", "obsidian", "glass" })
            Assert.NotNull(BlockRegistry.FromName(name));

        // The collapsed *_wool family is NOT registered (engine models one wool); air is the engine's, not data's.
        Assert.Null(BlockRegistry.FromName("white_wool"));
        Assert.Equal("sharpminerals", BlockRegistry.Air.Id.Namespace);
    }

    [Fact]
    public void WireStateIdsMatchData() {
        // For stateless blocks the wire state id is the data defaultState, per version (proves the shift is tracked).
        foreach (var (name, mapper, ver) in new[] {
                     ("dirt", V763, Data.V763), ("oak_planks", V763, Data.V763), ("diamond_block", V763, Data.V763),
                     ("dirt", V762, Data.V762), ("oak_planks", V762, Data.V762), ("diamond_block", V762, Data.V762) }) {
            var block = BlockRegistry.FromName(name)!;
            Assert.Equal(ver.DefaultState[name], mapper.StateId(block));
        }
        // diamond_block is exactly the cross-version shift: 4272 (762) vs 4276 (763).
        var diamond = BlockRegistry.FromName("diamond_block")!;
        Assert.Equal(4272, V762.StateId(diamond));
        Assert.Equal(4276, V763.StateId(diamond));
    }

    [Fact]
    public void WireItemIdsMatchData() {
        foreach (var (name, mapper, ver) in new[] {
                     ("stick", V763, Data.V763), ("stone", V763, Data.V763), ("chest", V763, Data.V763),
                     ("stick", V762, Data.V762), ("oak_planks", V762, Data.V762) }) {
            var item = ItemRegistry.FromName(name)!;
            Assert.Equal(ver.ItemId[name], mapper.ItemId(item));
        }
    }

    [Fact]
    public void KnownStaticsResolveWithBehavior() {
        // Plain accessors bind to the data-registered blocks by identity.
        Assert.Equal("minecraft:dirt", VanillaMod.Dirt.Id.Full);
        Assert.Equal("minecraft:red_sandstone", VanillaMod.RedSandstone.Id.Full);
        Assert.Equal("minecraft:grass", VanillaMod.ShortGrass.Id.Full); // 1.20.1 name for short grass

        // Enriched accessors keep their behavior after the refactor.
        Assert.True(VanillaMod.Chest.IsBlockEntity, "chest is still a block entity (container)");
        Assert.True(VanillaMod.Chest.Has<StatesBlockDescriptor>(), "chest still has the facing state");
        Assert.True(VanillaMod.Sand.Has<FallingBlockDescriptor>(), "sand still falls");
        Assert.True(VanillaMod.RedSand.Has<FallingBlockDescriptor>(), "red sand still falls");
        Assert.True(VanillaMod.Wool.Has<StatesBlockDescriptor>(), "wool still has the colour state");

        // Special (non-self) drops survive: the auto DropSelf is overridden by enrichment.
        Assert.Equal(VanillaMod.Cobblestone, VanillaMod.Stone.Drop?.Type);
        Assert.Equal(VanillaMod.Dirt, VanillaMod.GrassBlock.Drop?.Type);
        // Default self-drop for a plain block.
        Assert.Equal(VanillaMod.Dirt, VanillaMod.Dirt.Drop?.Type);
    }
}
