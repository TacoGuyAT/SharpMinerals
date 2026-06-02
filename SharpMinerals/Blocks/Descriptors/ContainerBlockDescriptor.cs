namespace SharpMinerals.Blocks.Descriptors;

/// <summary>Marks a block that opens a container window when used.</summary>
public sealed class ContainerBlockDescriptor {
    public int Size;
    public ContainerBlockDescriptor(int size) => Size = size;
}
