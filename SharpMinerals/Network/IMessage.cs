namespace SharpMinerals.Network;
public interface IMessage {
    public ReadOnlySpan<byte> Into<T>(T protocol) where T : Protocol;
}
