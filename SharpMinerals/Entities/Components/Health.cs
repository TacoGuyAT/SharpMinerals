namespace SharpMinerals.Entities.Components;

/// <summary>Hit points. Entities at zero health are dead.</summary>
public struct Health {
    public float Current;
    public float Max;

    public Health(float max) { Current = max; Max = max; }
    public Health(float current, float max) { Current = current; Max = max; }

    public readonly bool IsDead => Current <= 0f;
}
