using Brigadier.NET;
using Brigadier.NET.Builder;

namespace SharpMinerals.Commands;

/// <summary><c>/timeout &lt;ms&gt;</c> (0–10 min) — blocks the issuing input stream (console reader or
/// <c>/run</c> script); the server keeps ticking.</summary>
public static class TimeoutCommand {
    public static CommandDispatcher RegisterTimeout(this CommandDispatcher d) => d.Register(l => l
        .Literal("timeout")
        .Then(a => a.Argument("ms", Arguments.Integer(0, 600_000)).Executes(c => {
            Thread.Sleep(Arguments.GetInteger(c, "ms"));
            return 1;
        })));
}
