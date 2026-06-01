using SharpMinerals.Level;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Events.Contexts;

/// <summary>
/// A handle to any entity in a world: the <see cref="Server"/> it belongs to, its <see cref="World"/>, and
/// its ECS <see cref="ArchEntity"/>. The generic base of <see cref="PlayerContext"/> — lets systems and
/// events address any entity (a dropped item, a future mob) uniformly. Reads its live components from the
/// world on demand rather than snapshotting them.
/// </summary>
public record EntityContext(Server Server, World World, ArchEntity Entity);
