using SharpMinerals.Chat;
using System.Reflection;

namespace SharpMinerals.Commands;

/// <summary>
/// A non-player participant in the command/chat system — the server console, or a test driver. Like a player
/// it issues commands (through a <see cref="CommandDispatcher"/>) and receives messages back; unlike a player
/// it has no entity. The receive side is observable via <see cref="MessageReceived"/> so a host can log it and
/// a test can assert on it — the previous design wrote straight to the log, so callers could never read the
/// replies. Lives in core (not the CLI) precisely so in-process tests can drive commands as a sender.
/// </summary>
public sealed class ServerSender : ISender {
    public Server Server => server;
    Server server;
    public string Name { get; }

    public ServerSender(Server server, string name = "Server") {
        Name = name;
        this.server = server;
    }

    /// <summary>Raised for every message delivered to this sender — command feedback, chat, errors. The
    /// console host logs it; a test collects it. Fired on whatever thread produced the message.</summary>
    public event Action<ChatComponent>? MessageReceived;

    public void ReceiveMessage(ChatComponent message) => MessageReceived?.Invoke(message);

    public void SendMessage(string message) {
        server.BroadcastChatMessage(ChatComponent.Text("<")
            .AddExtra(
                ChatComponent.Text("Server").SetColor(TextColor.DarkPurple),
                ChatComponent.Text($"> {message}")
            )
       );
    }

    public void RunCommand(string command) {
        _ = server.CommandDispatcher.ExecuteAsync(this, command);
    }

    public async Task RunCommandAsync(string command) {
        await server.CommandDispatcher.ExecuteAsync(this, command);
    }
}
