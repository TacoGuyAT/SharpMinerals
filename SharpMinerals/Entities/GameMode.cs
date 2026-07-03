using System.Diagnostics.CodeAnalysis;

namespace SharpMinerals.Entities;
public class GameMode(Identifier identifier, PlayerFlags flags) {
    public static IReadOnlyList<GameMode> All => Registry.All;
    public static readonly Registry<GameMode> Registry = new();
    public static GameMode Register(string name, PlayerFlags flags) => Registry.Register(name, (id, identifier) => new GameMode(identifier, flags));
    public static bool TryFromPath(string path, [MaybeNullWhen(false)] out GameMode result) => Registry.TryFromPath(path, out result);

    // TODO: Decouple from core
    public byte IntoId() {
        byte gamemode;

        if(Flags.HasFlag(PlayerFlags.CreativeMode)) {
            gamemode = 1;
        } else if(Flags.HasFlag(PlayerFlags.CanBreakBlocks) || Flags.HasFlag(PlayerFlags.CanPlaceBlocks)) {
            gamemode = 0;
        } else if(Flags.HasFlag(PlayerFlags.NoClip)) {
            gamemode = 3;
        } else {
            gamemode = 2;
        }

        return gamemode;
    }

    public readonly Identifier Identifier = identifier;
    public readonly PlayerFlags Flags = flags;
}
