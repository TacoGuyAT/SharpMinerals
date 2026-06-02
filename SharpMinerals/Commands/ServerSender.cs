using SharpMinerals.Chat;
using System.Reflection;

namespace SharpMinerals.Commands;

/// <summary>
/// A non-player command/chat participant (server console or test driver): issues commands and receives
/// messages back, but has no entity. Replies are observable via <see cref="MessageReceived"/>. Lives in core
/// so in-process tests can drive commands as a sender.
/// </summary>
public sealed class ServerSender : ISender {
    public Server Server => server;
    Server server;
    public string Name { get; }

    public ServerSender(Server server, string name = "Server") {
        Name = name;
        this.server = server;
    }

    /// <summary>Raised on whatever thread produced the message.</summary>
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
