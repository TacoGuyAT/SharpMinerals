using Brigadier.NET;
using Brigadier.NET.Builder;
using Brigadier.NET.Context;
using Microsoft.Extensions.Logging;
using SharpMinerals.Blocks;
using SharpMinerals.Chat;
using SharpMinerals.Commands;
using SharpMinerals.Events;
using SharpMinerals.Modding;
using SharpMinerals.Network.Messages;

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

        // Placeholder /weather command: drives the RainS2C intermediary, which the protocol lowers into the
        // matching Game Event packets. No weather simulation yet - this just pushes the state to the clients.
        //   /weather clear | /weather rain [0..1] | /weather thunder [0..1]
        server.CommandDispatcher.Register(l => l
            .Literal("weather")
            .Then(x => x.Literal("clear").Executes(c => SetWeather(c, RainType.None, 0f)))
            .Then(x => x.Literal("rain")
                .Executes(c => SetWeather(c, RainType.Rain, 1f))
                .Then(a => a.Argument("level", Arguments.Double(0, 1))
                    .Executes(c => SetWeather(c, RainType.Rain, (float)Arguments.GetDouble(c, "level")))))
            .Then(x => x.Literal("thunder")
                .Executes(c => SetWeather(c, RainType.Thunderstorm, 1f))
                .Then(a => a.Argument("level", Arguments.Double(0, 1))
                    .Executes(c => SetWeather(c, RainType.Thunderstorm, (float)Arguments.GetDouble(c, "level"))))));

        Logger.LogInformation("Loaded!");
    }

    // Weather is global in vanilla, so broadcast the state to every in-world client.
    static int SetWeather(CommandContext<SenderContext> c, RainType type, float level) {
        c.Source.Server.BroadcastMessage(new RainS2C(type, level), conn => conn.InWorld);
        string desc = type switch {
            RainType.None => "clear",
            RainType.Rain => $"rain ({level:0.##})",
            _ => $"thunderstorm ({level:0.##})",
        };
        c.Source.Reply($"Weather set to {desc}.");
        return 1;
    }
}
