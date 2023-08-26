using Google.Protobuf;
using Protocol;
using ServerFramework.Scripts.Network;

namespace ServerFramework.Scripts.Logic;

public partial class MessageHandler
{
    public void HandlePing(ClientState clientState, IMessage message)
    {
        clientState.LastPingTime = NetworkManager.GetTimeSpan();
        Pong pong = new Pong();
        _networkManager.Send(clientState, pong);
    }
    
    
}