namespace SharpMinerals.Components;

/// <summary>Marks a DATA component - an ECS entity component (struct) or a per-instance <see cref="ComponentObject"/>
/// component (a block entity's, an item stack's). Our replacement for Arch's source-generator <c>[Component]</c>
/// marker (the Arch component-array AOT registration is done by hand - each mod lists its own components, see
/// <c>CoreMod</c>). It's a bare marker: a component's persisted id is derived at registration as
/// <c>{registering mod's namespace}:{snake_case type name}</c> (e.g. <c>sharpminerals:inventory_component</c>),
/// so the namespace comes from the mod that registers it - no per-component namespace here.</summary>
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false)]
public sealed class ComponentAttribute : Attribute;
