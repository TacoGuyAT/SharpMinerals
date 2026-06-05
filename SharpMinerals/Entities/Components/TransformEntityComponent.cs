using SharpMinerals.Network.Buffers;

namespace SharpMinerals.Entities.Components;

/// <summary>World-space placement: position (doubles) plus look direction in degrees. The transform every
/// visible entity carries; persisted (position + look) so a saved entity restores where it stood.</summary>
[Component]
public struct TransformEntityComponent : IPersistentComponent {
    public Vector3m Position;
    public Mfloat X { get => Position.X; set => Position.X = value; }
    public Mfloat Y { get => Position.Y; set => Position.Y = value; }
    public Mfloat Z { get => Position.Z; set => Position.Z = value; }
    public float Yaw, Pitch;

    public TransformEntityComponent(Vector3m position, float yaw = 0f, float pitch = 0f) {
        Position = position;
        Yaw = yaw; Pitch = pitch;
    }
    public TransformEntityComponent(Mfloat x, Mfloat y, Mfloat z, float yaw = 0f, float pitch = 0f) {
        Position = new(x, y, z);
        Yaw = yaw;
        Pitch = pitch;
    }

    public readonly void Write(MinecraftStream s) {
        s.WriteDouble(Position.X); s.WriteDouble(Position.Y); s.WriteDouble(Position.Z);
        s.WriteFloat(Yaw); s.WriteFloat(Pitch);
    }

    public static TransformEntityComponent Read(MinecraftStream s) =>
        new(s.ReadDouble(), s.ReadDouble(), s.ReadDouble(), s.ReadFloat(), s.ReadFloat());
}
