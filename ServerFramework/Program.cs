using Protocol;
using ServerFramework.Scripts.Logic;
using ServerFramework.Scripts.Network;

NetworkManager networkManager = new NetworkManager();
MessageHandler messageHandler = new MessageHandler(networkManager);

networkManager.AddMessageHandler(typeof(Ping), messageHandler.HandlePing);
networkManager.AddMessageHandler(typeof(Move), (clientState, message) =>
{
    Move move = (Move)message;
    Console.WriteLine($"{move.X} {move.Y}");
    move.X += 100;
    move.Y =  9999999;
    networkManager.Send(clientState, move);
});

networkManager.Start("127.0.0.1", 8888);

Console.ReadKey();