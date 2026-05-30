using System.Net;

namespace SharpMinerals.Network;

public abstract class NetServer<T> : INetServer
    where T : NetClient 
{
    protected Protocol Protocol;

    public NetServer(IPEndPoint endpoint, Protocol protocol) {
        Protocol = protocol;
    }

    public abstract void Start();

    public abstract void Send(ulong client, IMessage message);
}

public interface INetServer {
    public abstract void Start();
    public abstract void Send(ulong client, IMessage message);
}