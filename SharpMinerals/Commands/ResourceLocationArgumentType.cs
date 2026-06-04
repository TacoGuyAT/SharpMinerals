using Brigadier.NET;
using Brigadier.NET.ArgumentTypes;

namespace SharpMinerals.Commands;

/// <summary>
/// A command argument that reads a namespaced id token (e.g. <c>minecraft:stone</c>, <c>sample:ruby_block</c>) -
/// like Brigadier's <c>word()</c> but also allowing <c>:</c> and <c>/</c>, which a resource location needs.
/// <c>word()</c> stops at the colon, so <c>/give minecraft:stone</c> would otherwise fail to parse. A bare path
/// (<c>stone</c>) still parses and resolves to the <c>minecraft</c> namespace downstream.
/// </summary>
public sealed class ResourceLocationArgumentType : ArgumentType<string> {
    public static ResourceLocationArgumentType ResourceLocation() => new();

    public override string Parse(IStringReader reader) {
        var r = (Brigadier.NET.StringReader)reader; // always the concrete reader; exposes CanRead/Peek/String
        int start = r.Cursor;
        while (r.CanRead() && IsAllowed(r.Peek()))
            r.Skip();
        return r.String.Substring(start, r.Cursor - start);
    }

    static bool IsAllowed(char c) =>
        c is >= '0' and <= '9' or >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '_' or '-' or '.' or ':' or '/';
}
