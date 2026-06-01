using SharpMinerals.Chat;
using SharpMinerals.Commands;

namespace SharpMinerals;
public static class Extensions {
    public static void SendMessage(this ISender sender, Server server, string message) {
        server.BroadcastChatMessage(new TextComponent($"<{sender.Name}> {message}"));
    }

    public static void RunCommand(this ISender sender, Server server, string command) {
        sender.RunCommand(server.CommandDispatcher, command);
    }

    public static void RunCommand(this ISender sender, CommandDispatcher dispatcher, string command) {
        _ = dispatcher.ExecuteAsync(sender, command);
    }

    public static async Task RunCommandAsync(this ISender sender, Server server, string command) {
        await sender.RunCommandAsync(server.CommandDispatcher, command);
    }

    public static async Task RunCommandAsync(this ISender sender, CommandDispatcher dispatcher, string command) {
        await dispatcher.ExecuteAsync(sender, command);
    }
}
