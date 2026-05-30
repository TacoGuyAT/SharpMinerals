using System.Net.Sockets;

namespace SharpMinerals.Network.Tcp;
public class TcpNetClient : NetClient {
    public TcpClient Client;
    public NetworkStream Stream;
    public TcpNetClient(TcpClient client) {
        Client = client;
        Stream = client.GetStream();
    }
}
