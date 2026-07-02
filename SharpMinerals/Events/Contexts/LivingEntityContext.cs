using SharpMinerals.Entities.Components;
using SharpMinerals.Level;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Events.Contexts;
public record LivingEntityContext(Server Server, World World, ArchEntity Entity): EntityContext(Server, World, Entity) {
    /// <summary>
    /// This method queries ECS
    /// </summary>
    public ref StateEntityComponent GetState() => ref World.Ecs.Get<StateEntityComponent>(Entity);
}
