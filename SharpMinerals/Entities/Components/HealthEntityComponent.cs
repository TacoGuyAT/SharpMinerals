namespace SharpMinerals.Entities.Components;

/// <summary>Hit points. Entities at zero health are dead.</summary>
public struct HealthEntityComponent {
    public float Current;
    public float Max;

    public HealthEntityComponent(float max) { Current = max; Max = max; }
    public HealthEntityComponent(float current, float max) { Current = current; Max = max; }

    public readonly bool IsDead => Current <= 0f;
}
