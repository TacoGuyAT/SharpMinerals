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
        rubyBlock = BlockRegistry.Register("ruby_block").DropSelf();
    }

    public override void OnServerStarted(Server server) {
        server.MOTD = "A SharpMinerals server - now modded! §6[Sample]";

        // Greet each joiner with a tab-list header/footer, sent only to them via the audience predicate.
        server.Events.Subscribe<PlayerJoined>(e =>
            server.SetTabListHeaderFooter(
                new TextComponent($"Hi, {e.Context.Player.Name}!").SetColor(TextColor.Gold),
                new TextComponent($"running SharpMinerals v{server.Version}").SetColor(TextColor.Gray),
                c => c.Id == e.Context.Client.Id));

        Logger.LogInformation("Loaded!");
    }
}
