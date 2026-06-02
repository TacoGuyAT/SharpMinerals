namespace SharpMinerals.Network;

/// <summary>
/// A protocol-agnostic, in-memory representation of something exchanged on the wire.
/// Carries no serialization logic; a <see cref="ICodec"/> turns it into version-specific bytes.
/// </summary>
public interface IMessage { }
