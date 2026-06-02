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
    int StateId(BlockType block);

    int StateId(BlockState state);

    int EntityTypeId(EntityType type);

    int BlockEntityTypeId(BlockType block);

    int ItemId(ItemType item);

    /// <summary>
    /// True if this type has no native wire id and falls back to a placeholder (e.g. a mod block as stone).
    /// The slot encoder then attaches a custom name + identity marker so it shows distinctly and doesn't stack.
    /// </summary>
    bool IsCustom(ItemType item);

    /// <summary>Honors any state the stack carries (e.g. a wool's colour).</summary>
    int ItemId(ItemStack stack);

    ItemType? FromItemId(int vanillaId);

    /// <summary>Our stack (with any carried state) for a wire item id the client sends back.</summary>
    ItemStack FromVanillaItem(int vanillaId);
}
