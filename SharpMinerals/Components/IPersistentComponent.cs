using SharpMinerals.Network.Buffers;

namespace SharpMinerals.Components;

/// <summary>A component (one of the objects in a <see cref="ComponentObject"/>'s bag) that persists itself.
/// Registered under a namespaced id in <see cref="ComponentRegistry"/> (engine or mod), which also holds the
/// read-factory that reconstructs it. The framework writes the id + a length-prefixed blob; this writes that
/// blob's body. Any block-entity / item-stack component that should survive save/load implements this.</summary>
public interface IPersistentComponent {
    void Write(MinecraftStream s);
}
