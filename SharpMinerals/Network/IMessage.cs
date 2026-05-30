namespace SharpMinerals.Network;

/// <summary>
/// A protocol-agnostic, in-memory representation of something exchanged on the
/// wire. Messages carry no serialization logic themselves — a <see cref="ICodec"/>
/// registered with the active <see cref="Protocol"/> turns them into the bytes for
/// a specific protocol version. This keeps server logic decoupled from any single
/// Minecraft version.
/// </summary>
public interface IMessage { }
