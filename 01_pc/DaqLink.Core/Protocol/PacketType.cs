namespace DaqLink.Core.Protocol
{
    /// <summary>封包 TYPE(protocol.md 第 3 節)</summary>
    public static class PacketType
    {
        // MCU → PC 資料
        public const byte Data = 0x01;

        // PC → MCU 命令
        public const byte Start = 0x10;
        public const byte Stop = 0x11;
        public const byte SetRate = 0x12;
        public const byte SetWave = 0x13;
        public const byte GetStatus = 0x14;

        // MCU → PC 命令回應
        public const byte Ack = 0x80;
        public const byte Nak = 0x81;
        public const byte Status = 0x82;
    }

    /// <summary>封包框架常數(protocol.md 第 2 節)</summary>
    public static class Frame
    {
        public const byte Sof1 = 0xAA;
        public const byte Sof2 = 0x55;

        /// <summary>SOF(2) + LEN(1) + TYPE(1) + CRC(2):封包總長度 = LEN + Overhead</summary>
        public const int Overhead = 6;

        public const int MaxPayload = 255;
    }

    /// <summary>NAK 錯誤碼(protocol.md 6.2)</summary>
    public enum NakError : byte
    {
        None = 0x00,
        UnknownCommand = 0x01,
        BadLength = 0x02,
        OutOfRange = 0x03,
        Busy = 0x04,
    }
}
