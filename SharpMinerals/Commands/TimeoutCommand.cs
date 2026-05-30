namespace SharpMinerals.Commands;

/// <summary>
/// <c>/timeout &lt;ms&gt;</c> — awaits a delay, pausing only the current input stream
/// (the console reader or a running script); the server keeps ticking.
/// </summary>
public sealed class TimeoutCommand : ICommand {
    public string Name => "timeout";
    public string Description => "Delays the input stream (only input, not the server)";
    public string Usage => "/timeout <milliseconds>";

    public async Task ExecuteAsync(CommandContext ctx) {
        if (ctx.Args.Length >= 1 && int.TryParse(ctx.Args[0], out int ms))
            await Task.Delay(System.Math.Clamp(ms, 0, 600_000));
        else
            ctx.Reply($"usage: {Usage}");
    }
}
