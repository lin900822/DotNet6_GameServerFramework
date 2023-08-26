namespace ServerFramework.Scripts.Network;

public partial class NetworkManager
{
    private void HandleSelectTimeOut()
    {
        long timeNow = GetTimeSpan();

        foreach (ClientState clientState in _clients.Values)
        {
            if (timeNow - clientState.LastPingTime > _pingPongTimeOut)
            {
                Console.WriteLine(clientState.Socket.RemoteEndPoint.ToString() + " Time Out, Closing...");
                Close(clientState);
            }
        }
    }

    private void HandleCloseClient(ClientState clientState)
    {
        
    }
}