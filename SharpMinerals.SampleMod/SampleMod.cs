using Brigadier.NET.Builder;
using Microsoft.Extensions.Logging;
using SharpMinerals.Blocks;
using SharpMinerals.Chat;
using SharpMinerals.Entities.Components;
using SharpMinerals.Events;
using SharpMinerals.Math;
using SharpMinerals.Modding;
using SharpMinerals.Network.Messages;


namespace SharpMinerals.SampleMod;

/// <summary>
/// A worked-example mod, built against the public API only. Ports HarmonyMine's MOTD changer (here just
/// <c>server.MOTD = …</c>, no Harmony patch needed since the server exposes it) and adds content on top:
/// a custom block and a custom command that places it. The custom block has no vanilla state id, so a
/// stock 1.20.1 client renders it as stone (the registry's fallback) — its identity is server-side until a
/// type-mapping component lets a mod pick what it masquerades as.
/// </summary>
[ModInfo("sample", "1.0.0", ["SharpMinerals"])]
public sealed class SampleMod : Mod {
    BlockType rubyBlock = null!;

    public override void OnInitialize() {
        // Content registration happens here — before the palette is frozen and the protocols are built.
        rubyBlock = BlockRegistry.Register("ruby_block").DropSelf();
        Logger.LogInformation("Registered custom block \"{Block}\" (id {Id}).", rubyBlock.Name, rubyBlock.Id);
    }

    public override void OnServerStarted(Server server) {
        server.MOTD = "A SharpMinerals server — now modded! §6[Sample]";

        // Greet each player with a tab-list header/footer — sent ONLY to the joining client via the audience
        // predicate (the same per-client filter any mod can use). Others keep the header from their own join.
        server.Events.Subscribe<PlayerJoined>(e =>
            server.SetTabListHeaderFooter(
                new TextComponent("SharpMinerals").SetColor(TextColor.Gold),
                new TextComponent("running the Sample mod").SetColor(TextColor.Gray),
                c => c.Id == e.Context.Client.Id));

        // /ruby — place the custom block on the ground under the player (vanilla clients see stone).
        server.CommandDispatcher.Register(l => l
            .Literal("ruby")
            .Requires(s => s.IsPlayer)
            .Executes(c => {
                if (!c.Source.TryGetEntity(out var world, out var entity)) {
                    c.Source.Reply("Only a player can use /ruby.");
                    return 0;
                }
                var t = world.Ecs.Get<TransformEntityComponent>(entity);
                var pos = new Vector3i((int)System.Math.Floor(t.X), (int)System.Math.Floor(t.Y) - 1, (int)System.Math.Floor(t.Z));
                world.SetBlock(pos, rubyBlock);
                c.Source.Server!.NetServer.Broadcast(new BlockUpdateS2C(pos, rubyBlock), conn => conn.InWorld);
                c.Source.Reply($"Placed {rubyBlock.Name} at {pos.X}, {pos.Y}, {pos.Z} (renders as stone on a vanilla client).");
                return 1;
            }));

        Logger.LogInformation("MOTD set and /ruby registered.");
    }
}
