using SharpMinerals.Network.Buffers;

namespace SharpMinerals.Entities.Components;

[Component]
public struct HealthEntityComponent : IPersistentComponent {
    public float Current;
    public float Max;

    public HealthEntityComponent(float max) { Current = max; Max = max; }
    public HealthEntityComponent(float current, float max) { Current = current; Max = max; }

    public readonly bool IsDead => Current <= 0f;

    public readonly void Write(MinecraftStream s) { s.WriteFloat(Current); s.WriteFloat(Max); }

    public static HealthEntityComponent Read(MinecraftStream s) => new(s.ReadFloat(), s.ReadFloat());
}
