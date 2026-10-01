using System;

namespace DaqLink.Core.Models
{
    /// <summary>STATUS 封包內容(protocol.md 6.3)</summary>
    public sealed class DeviceStatus
    {
        public const int PayloadLength = 8;

        public DeviceStatus(bool running, ushort rateHz, ushort rxCrcErr, ushort txDrop)
        {
            Running = running;
            RateHz = rateHz;
            RxCrcErr = rxCrcErr;
            TxDrop = txDrop;
        }

        public bool Running { get; }
        public ushort RateHz { get; }
        public ushort RxCrcErr { get; }     // MCU 收到的 CRC 錯誤累計
        public ushort TxDrop { get; }       // MCU TX buffer 滿而丟棄的資料封包累計

        public static DeviceStatus FromPayload(byte[] p)
        {
            if (p == null || p.Length != PayloadLength)
                throw new ArgumentException($"STATUS payload must be {PayloadLength} bytes", nameof(p));
            return new DeviceStatus(p[0] != 0, DataSample.U16(p, 2), DataSample.U16(p, 4), DataSample.U16(p, 6));
        }

        public override string ToString()
        {
            return $"running={(Running ? 1 : 0)} rate={RateHz} rx_crc_err={RxCrcErr} tx_drop={TxDrop}";
        }
    }
}
