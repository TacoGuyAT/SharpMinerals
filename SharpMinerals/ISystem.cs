namespace SharpMinerals;

/// <summary>Marker base for a world system. A concrete system implements <see cref="ITickable"/> and/or
/// <see cref="Network.INetworkSystem"/> for the roles it plays; a purely event-driven one (e.g. the entity
/// tracker) implements just this. <see cref="Level.World"/> keeps every registered ISystem alive and iterates
/// the role-specific lists, so no per-element interface test is needed on the hot paths.</summary>
public interface ISystem { }
