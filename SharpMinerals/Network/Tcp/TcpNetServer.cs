using System.Net;
using System.Net.Sockets;

namespace SharpMinerals.Network.Tcp;

public class TcpNetServer : NetServer<TcpNetClient> {
    TcpListener listener;
    public TcpNetServer(IPEndPoint endpoint, Protocol protocol) : base(endpoint, protocol) {
        listener = new(endpoint);
    }

    ~TcpNetServer() {
        listener.Stop();
    }

    public override async void Start() {
        while(Server.IsRunning) {
            TcpNetClient client = new(await listener.AcceptTcpClientAsync());
            
        }
    }

    public override void Send(ulong client, IMessage message) {
        throw new NotImplementedException();
    }
}
