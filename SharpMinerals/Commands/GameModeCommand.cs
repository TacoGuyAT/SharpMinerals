using Brigadier.NET;
using Brigadier.NET.Builder;
using Brigadier.NET.Context;
using SharpMinerals.Entities;
using SharpMinerals.Entities.Components;
using SharpMinerals.Events.Contexts;
using SharpMinerals.Network.Messages;
using System.Diagnostics.CodeAnalysis;

namespace SharpMinerals.Commands;

/// <summary>
/// <c>/gamemode &lt;survival|creative|spectator&gt;</c>: switches the issuing player's gamemode in place. Stores
/// the new capability bits, derives <see cref="AbilitiesEntityComponent"/> from them, and pushes both the
/// Abilities packet and a <see cref="GameEvent.ChangeGameMode"/> Game Event so the client updates with no
/// respawn / world reload. Player-only; applies to the sender.
/// </summary>
public static class GameModeCommands {
    public static CommandDispatcher RegisterGameMode(this CommandDispatcher d) => d.Register(l => l
        .Literal("gamemode").Requires(s => s.IsPlayer)
        .Then(a => a.Argument("mode", ResourceLocationArgumentType.ResourceLocation())
            .Suggests((ctx, builder) => {
                foreach(var mode in GameMode.All)
                    if(mode.Identifier.Full.StartsWith(builder.Remaining, StringComparison.OrdinalIgnoreCase)
                            || mode.Identifier.Name.StartsWith(builder.Remaining, StringComparison.OrdinalIgnoreCase))
                        builder.Suggest(mode.Identifier.Full);
                return builder.BuildFuture();
            })
            .Executes(c => SetMode(c))));

    static int SetMode(CommandContext<SenderContext> c) {
        if(!Resolve(c, out var ctx))
            return 0;

        string name = Arguments.GetString(c, "mode");
        if(!GameMode.TryFromPath(name, out var match)) {
            c.Source.Reply($"Unknown gamemode '{name}'.");
            return 0;
        }

        ref var player = ref ctx.GetPlayer();
        ref var state = ref ctx.GetState();
        player.GameMode = match;

        ctx.Client.Send(new PlayerAbilitiesS2C(player.GameMode.Flags, player.FlyingSpeed, player.FieldOfViewModifier, state.State.HasFlag(EntityState.Flying)));
        ctx.Client.Send(new PlayerGameModeS2C(match));

        c.Source.Reply($"Gamemode set to '{match.Identifier}'.");
        return 1;
    }

    // The issuing player's context, or false (with a reply) if they aren't an available online player.
    static bool Resolve(CommandContext<SenderContext> c, [MaybeNullWhen(false)] out PlayerContext ctx) {
        ctx = default!;
        if(c.Source.Client is not { } client
            || !c.Source.Server.TryGetPlayer(client.Id, out ctx)
            || !ctx.World.Ecs.IsAlive(ctx.Entity)) {
            c.Source.Reply("Only an online player can change gamemode.");
            return false;
        }
        return true;
    }
}