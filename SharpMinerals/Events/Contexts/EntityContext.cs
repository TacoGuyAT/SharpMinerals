using SharpMinerals.Level;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Events.Contexts;

/// <summary>A handle to any entity in a world (its <see cref="Server"/>, <see cref="World"/>, and ECS
/// <see cref="ArchEntity"/>); the generic base of <see cref="PlayerContext"/>. Reads live components from
/// the world on demand rather than snapshotting them.</summary>
public record EntityContext(Server Server, World World, ArchEntity Entity);
