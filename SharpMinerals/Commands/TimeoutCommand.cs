using Brigadier.NET;
using Brigadier.NET.Builder;

namespace SharpMinerals.Commands;

/// <summary><c>/timeout &lt;ms&gt;</c> — pauses the issuing input stream (the console reader or a <c>/run</c>
/// script) for up to 10 minutes; the server keeps ticking. It blocks the calling thread, which is exactly the
/// old await-delay behaviour for those single-threaded input streams. Out-of-range values are rejected by the
/// argument bounds.</summary>
public static class TimeoutCommand {
    public static CommandDispatcher RegisterTimeout(this CommandDispatcher d) => d.Register(l => l
        .Literal("timeout")
        .Then(a => a.Argument("ms", Arguments.Integer(0, 600_000)).Executes(c => {
            Thread.Sleep(Arguments.GetInteger(c, "ms"));
            return 1;
        })));
}
