using SharpMinerals.Items;

namespace SharpMinerals.Network.Messages;

// Container / inventory windows. Messages carry OUR types (ItemStack); the codec maps
// to vanilla item ids. `Revision` is a version-neutral per-window sync counter — the
// JE763 codec writes it as the protocol "State ID".

// ── Clientbound ───────────────────────────────────────────────────────────────
public sealed record OpenScreenS2C(int WindowId, int WindowType, string Title) : IMessage;
public sealed record SetContainerContentS2C(int WindowId, int Revision, IReadOnlyList<ItemStack> Slots, ItemStack Carried) : IMessage;
public sealed record SetContainerSlotS2C(int WindowId, int Revision, int Slot, ItemStack Data) : IMessage;
public sealed record CloseContainerS2C(int WindowId) : IMessage;
public sealed record SetHeldItemS2C(int Slot) : IMessage;

// ── Serverbound ───────────────────────────────────────────────────────────────
// Click Container's changed-slot array and carried item are intentionally NOT decoded
// — the server is authoritative and recomputes state, then resyncs the client.
public sealed record ClickContainerC2S(int WindowId, int Revision, int Slot, int Button, int Mode) : IMessage;
public sealed record CloseContainerC2S(int WindowId) : IMessage;
public sealed record SetHeldItemC2S(int Slot) : IMessage;
// Carries our internal ItemStack, already resolved by the protocol codec (wire id + any custom-type NBT
// marker → ItemType). The handler stays protocol-agnostic — it never sees vanilla ids. Slot -1 = "throw".
// A null Stack means the client placed an item this server can't represent (the handler warns instead of
// clearing the slot); an empty (non-null) stack is a deliberate clear.
public sealed record SetCreativeModeSlotC2S(int Slot, ItemStack? Stack) : IMessage;
