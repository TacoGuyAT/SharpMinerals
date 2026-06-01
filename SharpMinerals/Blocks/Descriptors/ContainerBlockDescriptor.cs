namespace SharpMinerals.Blocks.Descriptors;

/// <summary>Marks a block that opens a container window when used; carries its slot count.</summary>
public sealed class ContainerBlockDescriptor {
    public int Size;
    public ContainerBlockDescriptor(int size) => Size = size;
}
