using System.Reflection;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace ServerFramework.Scripts.Network
{
    public static class MessageUtils
    {
        public static readonly Dictionary<UInt16, Type> IdToMessage;
        public static readonly Dictionary<Type, UInt16> MessageToId;
        
        private static Dictionary<Type, MessageParser> _typeParsers;

        static MessageUtils()
        {
            _typeParsers = new Dictionary<Type, MessageParser>();
            
            IdToMessage = new Dictionary<UInt16, Type>();
            MessageToId = new Dictionary<Type, ushort>();

            Assembly assembly = Assembly.GetExecutingAssembly();

            Type[] types = assembly.GetTypes();

            foreach (var type in types)
            {
                GetParsers(type);
                ConstructMessageTable(type);
            }
        }

        private static void GetParsers(Type type)
        {
            if (type.IsSubclassOf(typeof(IMessage))) return;
            if (type.IsAbstract) return;

            PropertyInfo propertyInfo = type.GetProperty("Parser", BindingFlags.Public | BindingFlags.Static);
            if (propertyInfo == null) return;

            object instance = Activator.CreateInstance(type);
            if (instance == null) return;

            MessageParser messageParser = (MessageParser)propertyInfo.GetValue(instance);

            if (messageParser == null) return;
            _typeParsers[type] = messageParser;
        }
        
        private static void ConstructMessageTable(Type type)
        {
            if (type.IsSubclassOf(typeof(IMessage))) return;
            if (type.IsAbstract) return;

            PropertyInfo? propertyInfo = type.GetProperty("Descriptor", BindingFlags.Public | BindingFlags.Static);
            if (propertyInfo == null) return;

            object? instance = Activator.CreateInstance(type);
            if (instance == null) return;

            MessageDescriptor? messageDescriptor = (MessageDescriptor)propertyInfo.GetValue(instance);
            if (messageDescriptor == null) return;

            var messageId = (UInt16)messageDescriptor.GetOptions().GetExtension(OptionsExtensions.Msgid);
            IdToMessage[messageId] = type;
            MessageToId[type]      = messageId;
        }

        public static byte[] Encode(IMessage message)
        {
            using (var memoryStream = new MemoryStream())
            {
                message.WriteTo(memoryStream);
                return memoryStream.ToArray();
            }
        }

        public static IMessage Decode(Type type, byte[] data, int offset, int length)
        {
            using (var memoryStream = new MemoryStream(data, offset, length))
            {
                return _typeParsers[type].ParseFrom(memoryStream.ToArray());
            }
        }
    }
}