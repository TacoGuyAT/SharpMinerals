using SharpMinerals.Blocks;
using SharpMinerals.Chat;
using SharpMinerals.Items;

namespace SharpMinerals.SampleMod;

/// <summary>Makes a block an energy store: it carries an <see cref="EnergyComponent"/> block entity. Right-clicking
/// reports the current charge on the action bar; right-clicking with redstone dust consumes one and adds 100 energy
/// (capped at the maximum). A worked example of a mod block entity built on the engine's persistent-component bag.</summary>
public sealed class EnergyBlockDescriptor(int maxEnergy = 1000) : IInteract, IBlockEntityDescriptor {
    static readonly ItemType Redstone = ItemType.TryFromPath("minecraft:redstone", out var result) ? result : CoreMod.Missing;
    const int EnergyPerRedstone = 100;

    public void Initialize(BlockEntity blockEntity) => blockEntity.Add(new EnergyComponent(0, maxEnergy));

    public void OnInteract(in BlockContext ctx) {
        if (ctx.Actor is not { } actor) return;
        if (ctx.World.GetOrCreateBlockEntity(ctx.Position) is not { } be || !be.TryGet<EnergyComponent>(out var energy))
            return;

        // Charging: a held redstone dust is consumed for +100 energy (up to the cap). Resolve redstone by name so
        // the mod doesn't hard-depend on the vanilla content mod.
        if (energy.Current < energy.Max
            && actor.GetHeld().Type is { } held && held == Redstone
            && actor.ConsumeHeld(1) > 0) {
            energy.Add(EnergyPerRedstone);
            ctx.World.MarkDirty(ctx.Position); // persist the new charge (the block entity already existed - no auto-dirty)
        }

        actor.SendActionBar(new TextComponent($"Energy: {energy.Current} / {energy.Max}").SetColor(TextColor.Gold));
    }
}
