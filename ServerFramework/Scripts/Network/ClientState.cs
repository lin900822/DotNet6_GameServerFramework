using System.Net.Sockets;

namespace ServerFramework.Scripts.Network;

public class ClientState
{
    public Socket           Socket;
    public ByteArray        ReadBuffer = new ByteArray();
    public Queue<ByteArray> SendQueue;

    public long LastPingTime = 0;
}