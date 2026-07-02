using Microsoft.Extensions.Logging;
using SharpMinerals.Blocks;
using SharpMinerals.Chat;
using SharpMinerals.Events;
using SharpMinerals.Modding;

namespace SharpMinerals.SampleMod;

[ModInfo("sample", "1.0.0", ["SharpMinerals"], TargetServerVersion = "0.1.0")]
public sealed class SampleMod : Mod {
    BlockType rubyBlock = null!;
    BlockType battery = null!;

    public override void OnInitialize() {
        rubyBlock = BlockType.Register("ruby_block").DropSelf();
        // A battery: a data-only block entity carrying an EnergyComponent. Right-click to read its charge on the
        // action bar; right-click with redstone dust to add energy. The component persists via the bag (sample:energy_component).
        battery = BlockType.Register("battery").DropSelf().Add(new EnergyBlockDescriptor(maxEnergy: 1000));
    }

    public override void OnServerStarted(Server server) {
        server.MOTD = ChatComponent.Text("A SharpMinerals server - now modded! ").With(ChatComponent.Text("[Sample]").SetColor(TextColor.Gold));

        // Greet each joiner with a tab-list header/footer, sent only to them via the audience predicate.
        server.Events.Subscribe<PlayerJoined>(e =>
            server.SetTabListHeaderFooter(
                new TextComponent($"Hi, {e.Client.Name}!").SetColor(TextColor.Gold),
                new TextComponent($" Running SharpMinerals v{server.Version} ").SetColor(TextColor.Gray),
                c => c.Id == e.Context.Client.Id));

        Logger.LogInformation("Loaded!");
    }
}
