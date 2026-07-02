namespace SharpMinerals.Entities;
public class GameMode(Identifier identifier, PlayerFlags flags) {
    public readonly Identifier Identifier = identifier;
    public readonly PlayerFlags Flags = flags;
}
