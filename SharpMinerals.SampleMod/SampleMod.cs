using Microsoft.Extensions.Logging;
using SharpMinerals.Blocks;
using SharpMinerals.Chat;
using SharpMinerals.Events;
using SharpMinerals.Modding;

namespace SharpMinerals.SampleMod;

[ModInfo("sample", "1.0.0", ["SharpMinerals"], TargetServerVersion = "0.1.0")]
public sealed class SampleMod : Mod {
    BlockType rubyBlock = null!;

    public override void OnInitialize() {
        // Content registration happens here.
        rubyBlock = BlockRegistry.Register("ruby_block").DropSelf();
    }

    public override void OnServerStarted(Server server) {
        server.MOTD = "A SharpMinerals server — now modded! §6[Sample]";

        // Greet each player with a tab-list header/footer — sent ONLY to the joining client via the audience
        // predicate (the same per-client filter any mod can use). Others keep the header from their own join.
        server.Events.Subscribe<PlayerJoined>(e =>
            server.SetTabListHeaderFooter(
                new TextComponent($"Hi, {e.Context.Player.Name}!").SetColor(TextColor.Gold),
                new TextComponent($"running SharpMinerals v{server.Version}").SetColor(TextColor.Gray),
                c => c.Id == e.Context.Client.Id));

        Logger.LogInformation("Loaded!");
    }
}
