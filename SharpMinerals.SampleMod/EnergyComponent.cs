using SharpMinerals.Network.Buffers;

namespace SharpMinerals.SampleMod;

/// <summary>A simple energy store a block entity can carry: a current charge bounded by a maximum. Persists itself
/// (current + max) through the component bag, so a battery keeps its charge across save/load. Registered by the
/// component source generator under this mod's namespace as <c>sample:energy_component</c>.</summary>
[Component]
public sealed class EnergyComponent : IPersistentComponent {
    public int Current;
    public int Max;

    public EnergyComponent(int current = 0, int max = 1000) { Current = current; Max = max; }

    /// <summary>Adds <paramref name="amount"/> energy, clamped to <c>[0, Max]</c>.</summary>
    public void Add(int amount) => Current = System.Math.Clamp(Current + amount, 0, Max);

    public void Write(MinecraftStream s) { s.WriteVarInt(Current); s.WriteVarInt(Max); }

    public static EnergyComponent Read(MinecraftStream s) => new(s.ReadVarInt(), s.ReadVarInt());
}
