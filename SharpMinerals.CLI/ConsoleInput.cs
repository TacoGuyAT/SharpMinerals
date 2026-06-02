using Microsoft.Extensions.Logging;
using SharpMinerals.Commands;

namespace SharpMinerals.CLI;

/// <summary>
/// The console's host-side I/O: reads stdin lines on a background thread and submits each as the supplied
/// <see cref="ServerSender"/>, and logs whatever that sender receives. The sender lives in core; only this
/// byte-level I/O is CLI.
/// </summary>
public sealed class ConsoleInput {
    static readonly ILogger Log = Logging.For("Console");

    readonly ServerSender sender;

    public ConsoleInput(ServerSender sender) {
        this.sender = sender;
        // Log whatever the sender receives, rendered to ANSI. Stays in the log pipeline as one ordered stream.
        sender.MessageReceived += m => Log.LogInformation("{Message}", ChatAnsi.Render(m));
    }

    /// <summary>Starts reading stdin on a background thread, so it never keeps the process alive.</summary>
    public void Start() => new Thread(Run) { Name = "Console Input", IsBackground = true }.Start();

    void Run() {
        string? line;
        while ((line = Console.ReadLine()) is not null) {
            if(sender.Server is not { IsRunning: true }) break; // stop acting on input once shutting down

            if(line.StartsWith('/')) {
                try {
                    sender.RunCommandAsync(line[1..]).GetAwaiter().GetResult();
                } catch(Exception ex) {
                    Log.LogWarning("command error: {Message}", ex.Message);
                }
            } else {
                sender.SendMessage(line);
            }
        }
    }
}
