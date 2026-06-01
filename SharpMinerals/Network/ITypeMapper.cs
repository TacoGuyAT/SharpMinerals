using SharpMinerals.Blocks;
using SharpMinerals.Entities;
using SharpMinerals.Items;

namespace SharpMinerals.Network;

/// <summary>
/// Translates protocol-agnostic SharpMinerals block/item definitions to a specific protocol
/// version's wire ids (the Geyser/ViaVersion-style seam). Each <see cref="Protocol"/> exposes its
/// own implementation via <see cref="Protocol.Types"/>, so a multi-version server maps per client.
/// </summary>
public interface ITypeMapper {
    /// <summary>The wire block-state id for a block's default state.</summary>
    int StateId(BlockType block);

    /// <summary>The wire block-state id for a placed block, applying its modeled state properties.</summary>
    int StateId(BlockState state);

    /// <summary>The wire entity-type id for an entity kind (e.g. a dropped item).</summary>
    int EntityTypeId(EntityType type);

    /// <summary>The wire block-entity-type id for a block that carries one (e.g. a chest), for the chunk
    /// packet's block-entity list. False if this version has no mapping — the serializer then omits it
    /// (the block still renders from its state; only the block-entity model/data is skipped).</summary>
    bool TryBlockEntityTypeId(BlockType block, out int id);

    /// <summary>The wire item id for an item type.</summary>
    int ItemId(ItemType item);

    /// <summary>
    /// True if this type has no native wire id for this version, so it falls back to a placeholder
    /// (e.g. a mod-added block rendering as stone). The slot encoder uses this to attach a custom
    /// display name + an identity marker, so the client shows it distinctly and doesn't stack it
    /// with the placeholder item (or with other custom types sharing the same fallback).
    /// </summary>
    bool IsCustom(ItemType item);

    /// <summary>The wire item id for a stack, honoring any state it carries (e.g. a wool's colour).</summary>
    int ItemId(ItemStack stack);

    /// <summary>Our item definition for a wire item id, or null if unmapped.</summary>
    ItemType? FromItemId(int vanillaId);

    /// <summary>Our stack (with any carried state) for a wire item id the client sends back.</summary>
    ItemStack FromVanillaItem(int vanillaId);
}
