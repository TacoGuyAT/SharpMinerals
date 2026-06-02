namespace SharpMinerals.Entities.Components;

[Component]
public struct HealthEntityComponent {
    public float Current;
    public float Max;

    public HealthEntityComponent(float max) { Current = max; Max = max; }
    public HealthEntityComponent(float current, float max) { Current = current; Max = max; }

    public readonly bool IsDead => Current <= 0f;
}
