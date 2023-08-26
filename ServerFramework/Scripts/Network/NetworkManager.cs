using System.Net;
using System.Net.Sockets;
using Google.Protobuf;

namespace ServerFramework.Scripts.Network;

public class MessageInfo
{
    public UInt16   MessageId;
    public IMessage Message;
}

public partial class NetworkManager
{
    public event Action              OnSelectTimeOut;
    public event Action<ClientState> OnCloseClient;

    public Dictionary<Socket, ClientState> Clients => _clients;

    private int _pingPongTimeOut = 30;

    private Socket _listenFd;

    private Dictionary<Socket, ClientState> _clients = new Dictionary<Socket, ClientState>();

    private Dictionary<UInt16, Action<ClientState, IMessage>> _messageHandlers =
        new Dictionary<ushort, Action<ClientState, IMessage>>();

    private List<Socket> _checkRead = new List<Socket>();

    public NetworkManager()
    {
        OnSelectTimeOut += HandleSelectTimeOut;
        OnCloseClient   += HandleCloseClient;
    }

    public void Start(string ip, int port)
    {
        InitState(ip, port);

        while (true)
        {
            ResetCheckRead();

            // 超過1秒(1000ms)沒有可讀的Socket，則不阻塞回傳空
            Socket.Select(_checkRead, null, null, 1000);

            for (int i = _checkRead.Count - 1; i >= 0; i--)
            {
                Socket socket = _checkRead[i];
                if (socket == _listenFd)
                {
                    // 處理Client連接
                    ReadListenFd(socket);
                }
                else
                {
                    // 接收訊息
                    ReadClientFd(socket);
                }
            }

            // 超時，當select出可讀的socket或閒置超過1秒(1000ms)時觸發
            OnSelectTimeOut?.Invoke();
        }
    }

    #region - Init -

    private void InitState(string ip, int port)
    {
        _listenFd = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        IPAddress  ipAddress  = IPAddress.Parse(ip);
        IPEndPoint ipEndPoint = new IPEndPoint(ipAddress, port);
        _listenFd.Bind(ipEndPoint);

        // 開始監聽，參數是最大連接數 0 代表不限制
        _listenFd.Listen(0);

        Console.WriteLine("Server started!");
    }

    #endregion

    #region - Other -

    private void Close(ClientState clientState)
    {
        OnCloseClient?.Invoke(clientState);

        // 關閉Socket
        clientState.Socket.Close();
        _clients.Remove(clientState.Socket);
    }

    private void ResetCheckRead()
    {
        _checkRead.Clear();
        _checkRead.Add(_listenFd);
        foreach (ClientState clientState in _clients.Values)
        {
            _checkRead.Add(clientState.Socket);
        }
    }

    #endregion

    #region - Accecpt Clients -

    private void ReadListenFd(Socket listenFd)
    {
        try
        {
            Socket clientFd = listenFd.Accept();
            Console.WriteLine("A Client Connected: " + clientFd.RemoteEndPoint.ToString());
            ClientState clientState = new ClientState();
            clientState.Socket       = clientFd;
            clientState.LastPingTime = GetTimeSpan();
            clientState.SendQueue    = new Queue<ByteArray>();
            _clients.Add(clientFd, clientState);
        }
        catch (SocketException ex)
        {
            Console.WriteLine("Connect Failed: " + ex.ToString());
        }
    }

    #endregion

    #region - Receive -

    private void ReadClientFd(Socket clientFd)
    {
        ClientState clientState = _clients[clientFd];
        ByteArray   readBuffer  = clientState.ReadBuffer;

        //缓冲区不够，清除，若依旧不够，只能返回
        //缓冲区长度只有1024，单条协议超过缓冲区长度时会发生错误，根据需要调整长度
        if (readBuffer.Remain <= 0)
        {
            ParseReceivedData(clientState);
            readBuffer.ReuseCapacity();
        }

        // 如果Buffer空間還是不夠
        if (readBuffer.Remain <= 0)
        {
            Console.WriteLine("Receive Failed: message's length may be over buffer capacity");
            Close(clientState);
            return;
        }

        //接收
        int count = 0;

        try
        {
            count = clientFd.Receive(readBuffer.Data, readBuffer.WriteIndex, readBuffer.Remain, 0);
        }
        catch (SocketException ex)
        {
            Console.WriteLine("Receive Failed: " + ex.ToString());
            Close(clientState);
            return;
        }

        // 客戶端發出FIN訊號(count==0)關閉連接
        if (count <= 0)
        {
            Console.WriteLine("Client Disconnected: " + clientFd.RemoteEndPoint.ToString());
            Close(clientState);
            return;
        }

        readBuffer.WriteIndex += count;

        ParseReceivedData(clientState);

        readBuffer.ReuseCapacity();
    }

    private void ParseReceivedData(ClientState clientState)
    {
        ByteArray readBuffer = clientState.ReadBuffer;
        int       readIndex  = readBuffer.ReadIndex;
        byte[]    data       = readBuffer.Data;

        // 連表示總長度的2 Byte都沒收到
        if (readBuffer.Length <= 2) return;

        UInt16 length = (UInt16)((data[readIndex + 1] << 8) | data[readIndex]);

        // 資料不完整
        if (readBuffer.Length < length) return;
        readBuffer.ReadIndex += 2;

        // 解析MessageId
        readIndex = readBuffer.ReadIndex;
        UInt16 messageId = (UInt16)((data[readIndex + 1] << 8) | data[readIndex]);
        readBuffer.ReadIndex += 2;

        if (!MessageUtils.IdToMessage.ContainsKey(messageId))
        {
            Console.WriteLine("Parse Message Failed, unknown MessageId");
            return;
        }

        // 協議Body長度 = 總長度 - 表示總長度的UInt16(2 Byte) - MessageId(2 Byte) 
        int bodyLength = length - 2 - 2;

        // 解析協議Body
        Type     type    = MessageUtils.IdToMessage[messageId];
        IMessage message = MessageUtils.Decode(type, readBuffer.Data, readBuffer.ReadIndex, bodyLength);

        if (message == null)
        {
            Console.WriteLine("Parse Message Failed, unknown Message Type");
            return;
        }

        readBuffer.ReadIndex += bodyLength;

        Console.WriteLine("Receive Message: " + message.GetType().Name);

        // Message Handler
        if (_messageHandlers.ContainsKey(messageId))
        {
            _messageHandlers[messageId]?.Invoke(clientState, message);
        }

        // 繼續解析
        if (readBuffer.Length > 2)
        {
            ParseReceivedData(clientState);
        }
    }

    #endregion

    #region - Send -

    public void Send(ClientState clientState, IMessage message)
    {
        if (clientState == null || clientState.Socket == null || !clientState.Socket.Connected)
        {
            Console.WriteLine("Send Failed, clientState is null or not connected");
            return;
        }

        var messageId = MessageUtils.MessageToId[message.GetType()];

        // 資料編碼
        byte[] bodyBytes = MessageUtils.Encode(message);

        // 拼接 (總長度 + MessageId + Body)
        byte[] sendBytes = new byte[2 + 2 + bodyBytes.Length];

        sendBytes[0] = (byte)(sendBytes.Length % 256);
        sendBytes[1] = (byte)(sendBytes.Length / 256);
        sendBytes[2] = (byte)(messageId % 256);
        sendBytes[3] = (byte)(messageId / 256);

        Array.Copy(bodyBytes, 0, sendBytes, 2 + 2, bodyBytes.Length);

        // 寫入MessageQueue
        ByteArray byteArray = new ByteArray(sendBytes);
        int       count     = 0; //writeQueue的长度
        lock (clientState.SendQueue)
        {
            clientState.SendQueue.Enqueue(byteArray);
            count = clientState.SendQueue.Count;
        }

        // 發送
        if (count == 1)
        {
            Console.WriteLine($"Send: {message.GetType().Name}");
            clientState.Socket.BeginSend(sendBytes, 0, sendBytes.Length, 0, SendCallback, clientState);
        }
    }

    private void SendCallback(IAsyncResult result)
    {
        ClientState clientState = (ClientState)result.AsyncState;

        if (clientState == null || clientState.Socket == null || !clientState.Socket.Connected)
        {
            Console.WriteLine("Send Failed, clientState is null or not connected");
            return;
        }

        int count = clientState.Socket.EndSend(result);

        ByteArray byteArray;
        lock (clientState.SendQueue)
        {
            byteArray = clientState.SendQueue.First();
        }

        // byteArray完整發送
        byteArray.ReadIndex += count;
        if (byteArray.Length == 0)
        {
            lock (clientState.SendQueue)
            {
                clientState.SendQueue.Dequeue();
                if (clientState.SendQueue.Count >= 1)
                {
                    byteArray = clientState.SendQueue.First();
                }
                else
                {
                    byteArray = null;
                }
            }
        }

        // 繼續發送
        if (byteArray != null)
        {
            clientState.Socket.BeginSend(byteArray.Data, byteArray.ReadIndex, byteArray.Length, 0, SendCallback,
                clientState);
        }
    }

    #endregion

    #region - Message Handler -

    public void AddMessageHandler(Type type, Action<ClientState, IMessage> handler)
    {
        UInt16 messageId = MessageUtils.MessageToId[type];

        if (_messageHandlers.ContainsKey(messageId))
        {
            _messageHandlers[messageId] += handler;
        }
        else
        {
            _messageHandlers[messageId] = handler;
        }
    }

    public void RemoveMessageHandler(Type type, Action<ClientState, IMessage> handler)
    {
        UInt16 messageId = MessageUtils.MessageToId[type];

        if (_messageHandlers.ContainsKey(messageId))
        {
            _messageHandlers[messageId] -= handler;
        }
    }

    #endregion

    #region - Time Span -

    public static long GetTimeSpan()
    {
        TimeSpan timeSpan = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0);
        return Convert.ToInt64(timeSpan.TotalSeconds);
    }

    #endregion
}