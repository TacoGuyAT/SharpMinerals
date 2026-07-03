using Brigadier.NET;
using Brigadier.NET.Builder;
using Brigadier.NET.Context;
using SharpMinerals.Entities.Components;
using SharpMinerals.Events.Contexts;
using SharpMinerals.Network.Messages;
using System.Diagnostics.CodeAnalysis;

namespace SharpMinerals.Commands;

/// <summary>
/// <c>/speed &lt;multiplier&gt;</c> and <c>/flyspeed &lt;multiplier&gt;</c>: scale the issuing player's walk / fly
/// speed (1 = vanilla, 0 = frozen, up to 10x). Player-only; applies to the sender. Walk speed rides the
/// <c>generic.movement_speed</c> attribute (Update Attributes); fly speed rides the Player Abilities packet.
/// </summary>
public static class SpeedCommands {
    const double Min = 0.0, Max = 10.0;

    public static CommandDispatcher RegisterSpeed(this CommandDispatcher d) => d.Register(l => l
        .Literal("speed").Requires(s => s.IsPlayer)
        .Then(a => a.Argument("multiplier", Arguments.Double(Min, Max)).Executes(c => SetWalkSpeed(c))));

    public static CommandDispatcher RegisterFlySpeed(this CommandDispatcher d) => d.Register(l => l
        .Literal("flyspeed").Requires(s => s.IsPlayer)
        .Then(a => a.Argument("multiplier", Arguments.Double(Min, Max)).Executes(c => SetFlySpeed(c))));

    static int SetWalkSpeed(CommandContext<SenderContext> c) {
        if (!Resolve(c, out var ctx, out _, out var s)) return 0;
        var state = s!.Value;
        double mult = Arguments.GetDouble(c, "multiplier");
        state.WalkingSpeed = (float)(StateEntityComponent.DefaultWalkSpeed * mult);
        int netId = ctx.World.Ecs.Get<PlayerEntityComponent>(ctx.Entity).NetId;
        ctx.Client.Send(new UpdateAttributesS2C(netId, state.WalkingSpeed));
        c.Source.Reply($"Walk speed set to {mult:0.##}x.");
        return 1;
    }

    static int SetFlySpeed(CommandContext<SenderContext> c) {
        if (!Resolve(c, out var ctx, out var p, out var s)) return 0;
        var player = p!.Value;
        var state = s!.Value;
        double mult = Arguments.GetDouble(c, "multiplier");
        player.FlyingSpeed = (float)(PlayerEntityComponent.DefaultFlyingSpeed * mult);
        // Send the current flags (the tracked Flying bit keeps them in the same flight state) + the matching FOV.
        ctx.Client.Send(new PlayerAbilitiesS2C(player.GameMode.Flags, player.FlyingSpeed, player.FieldOfViewModifier, state.State.HasFlag(Entities.EntityState.Flying)));
        c.Source.Reply($"Fly speed set to {mult:0.##}x.");
        return 1;
    }

    // The issuing player's context + abilities, or false (with a reply) if they aren't an available online player.
    static bool Resolve(
        CommandContext<SenderContext> c, 
        [MaybeNullWhen(false)] out PlayerContext ctx, 
        // TODO: Recheck nullability safety
        [MaybeNullWhen(false)] out PlayerEntityComponent? player, 
        [MaybeNullWhen(false)] out StateEntityComponent? state
    ) {
        ctx = default!;
        player = null!;
        state = null!;
        if (c.Source.Client is not { } client
            || !c.Source.Server.TryGetPlayer(client.Id, out ctx)
            || !ctx.World.Ecs.IsAlive(ctx.Entity)) {
            c.Source.Reply("Only an online player can set speed.");
            return false;
        }
        player = ctx.GetPlayer();
        state = ctx.GetState();
        return true;
    }
}
