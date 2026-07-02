using System.Diagnostics.CodeAnalysis;

namespace SharpMinerals.Entities;
public class GameMode(Identifier identifier, PlayerFlags flags) {
    public static IReadOnlyList<GameMode> All => Registry.All;
    public static readonly Registry<GameMode> Registry = new();
    public static GameMode Register(string name, PlayerFlags flags) => Registry.Register(name, (id, identifier) => new GameMode(identifier, flags));
    public static bool TryFromPath(string path, [MaybeNullWhen(false)] out GameMode result) => Registry.TryFromPath(path, out result);

    public readonly Identifier Identifier = identifier;
    public readonly PlayerFlags Flags = flags;
}
