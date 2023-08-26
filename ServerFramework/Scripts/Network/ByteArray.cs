namespace ServerFramework.Scripts.Network
{
    public class ByteArray
    {
        #region - const -

        private const int DEFAULT_SIZE = 1024;

        #endregion

        #region - Public variables -

        public byte[] Data;

        public int ReadIndex  = 0;
        public int WriteIndex = 0;

        #endregion

        #region - Properties -

        public int Remain => _capacity - WriteIndex;
        public int Length => WriteIndex - ReadIndex;

        #endregion

        #region - Private variables -

        private int _capacity = 0;
        private int _initSize = 0;

        #endregion

        #region - Constructors-

        public ByteArray(int size = DEFAULT_SIZE)
        {
            Data       = new byte[size];
            _capacity  = size;
            _initSize  = size;
            ReadIndex  = 0;
            WriteIndex = 0;
        }

        public ByteArray(byte[] bytes)
        {
            Data       = bytes;
            _capacity  = bytes.Length;
            _initSize  = bytes.Length;
            ReadIndex  = 0;
            WriteIndex = bytes.Length;
        }

        #endregion

        #region - Methods -

        // 擴充容量
        public void ReSize(int size)
        {
            if (size < Length) return;
            if (size < _initSize) return;

            // 擴充為2的冪次 256 512 1024 2048...
            var newSize = 1;
            while (newSize < size)
            {
                newSize *= 2;
            }

            _capacity = newSize;
            var newData = new byte[_capacity];
            Array.Copy(Data, ReadIndex, newData, 0, Length);
            Data = newData;

            ReadIndex  = 0;
            WriteIndex = Length;
        }

        // 檢查與複用byte空間
        public void CheckAndReuseCapacity()
        {
            if (Length < 8) ReuseCapacity();
        }

        // 複用byte空間
        public void ReuseCapacity()
        {
            if (Length > 0)
            {
                Array.Copy(Data, ReadIndex, Data, 0, Length);
            }
            
            // 這裡順序要注意不能相反
            WriteIndex = Length;
            ReadIndex  = 0;
        }

        // 寫入資料
        public int Write(byte[] bytes, int offset, int count)
        {
            if (Remain < count)
            {
                ReSize(Length + count);
            }

            Array.Copy(bytes, offset, Data, WriteIndex, count);
            WriteIndex += count;
            return count;
        }

        // 讀取資料
        public int Read(byte[] bytes, int offset, int count)
        {
            count = Math.Min(count, Length);
            Array.Copy(Data, ReadIndex, bytes, offset, count);
            ReadIndex += count;
            CheckAndReuseCapacity();
            return count;
        }

        // 讀取UInt16
        public UInt16 ReadUInt16()
        {
            if (Length < 2) return 0;
            // 以小端方式讀取Int16
            UInt16 readUInt16 = (UInt16)((Data[ReadIndex + 1] << 8) | Data[ReadIndex]);
            ReadIndex += 2;
            CheckAndReuseCapacity();
            return readUInt16;
        }

        // 讀取UInt32
        public UInt32 ReadUInt32()
        {
            if (Length < 4) return 0;
            // 以小端方式讀取Int32
            UInt32 readUInt32 = (UInt32)((Data[ReadIndex + 3] << 24) |
                                        (Data[ReadIndex + 2] << 16) |
                                        (Data[ReadIndex + 1] << 8) |
                                        Data[ReadIndex]);
            ReadIndex += 4;
            CheckAndReuseCapacity();
            return readUInt32;
        }

        // 輸出緩衝區
        public override string ToString()
        {
            return BitConverter.ToString(Data, ReadIndex, Length);
        }

        // Debug
        public string Debug()
        {
            return string.Format("readIdx({0}) writeIdx({1}) bytes({2})",
                ReadIndex,
                WriteIndex,
                BitConverter.ToString(Data, 0, _capacity)
            );
        }

        #endregion
    }
}