namespace SharpMinerals.Entities.Components;

// The transform last broadcast to other clients. PlayerMovementSystem diffs the live transform against this
// and relays only when the player actually moved, so a stationary player costs no packets. Seeded to the
// spawn transform so a freshly-joined player (already spawned to others at that position) doesn't re-broadcast.
[Component]
public struct SyncedTransformEntityComponent {
    public Mfloat X, Y, Z;
    public float Yaw, Pitch;
}
