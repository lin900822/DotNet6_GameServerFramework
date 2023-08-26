using ServerFramework.Scripts.Network;

namespace ServerFramework.Scripts.Logic;

public partial class MessageHandler
{
    private NetworkManager _networkManager;
    
    public MessageHandler(NetworkManager networkManager)
    {
        _networkManager = networkManager;
    }
}