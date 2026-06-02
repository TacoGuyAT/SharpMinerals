using Brigadier.NET;
using Brigadier.NET.Builder;

namespace SharpMinerals.Commands;

/// <summary><c>/help [command]</c> — lists command usages (or one command's), filtered to what the source
/// is allowed to run.</summary>
public static class HelpCommand {
    public static CommandDispatcher RegisterHelp(this CommandDispatcher d) => d.Register(l => l
        .Literal("help")
        .Then(a => a.Argument("command", Arguments.Word()).Executes(c => {
            var name = Arguments.GetString(c, "command");
            var matches = d.Brigadier.GetAllUsage(d.Brigadier.GetRoot(), c.Source, restricted: true)
                .Where(u => u == name || u.StartsWith(name + " "))
                .ToArray();
            if (matches.Length == 0)
                c.Source.Reply($"Unknown command: {name}");
            else
                foreach (var usage in matches) c.Source.Reply($"/{usage}");
            return 1;
        }))
        .Executes(c => {
            c.Source.Reply("Commands:");
            foreach (var usage in d.Brigadier.GetAllUsage(d.Brigadier.GetRoot(), c.Source, restricted: true))
                c.Source.Reply($"/{usage}");
            return 1;
        }));
}
