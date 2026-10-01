using System;
using System.Linq;

namespace DaqLink.Core.Tests
{
    /// <summary>protocol.md 第 8 節的範例封包</summary>
    internal static class Vectors
    {
        public const string Start = "AA 55 00 10 3E 0F";
        public const string Stop = "AA 55 00 11 1F 1F";
        public const string SetRate100 = "AA 55 02 12 64 00 45 83";
        public const string SetWaveASine = "AA 55 08 13 00 01 0A 00 DC 05 00 08 62 66";
        public const string GetStatus = "AA 55 00 14 BA 4F";
        public const string AckStart = "AA 55 01 80 10 05 F2";
        public const string NakSetRateBusy = "AA 55 02 81 12 04 57 40";
        public const string Data = "AA 55 0E 01 01 00 00 08 00 04 FF 07 01 04 00 00 FF 0F 75 E3";

        // 以下由 tools/daq_monitor.py 的 build_frame 產生
        /// <summary>DATA seq = 0x55AA:payload 開頭就是 AA 55,用來測假 SOF</summary>
        public const string DataSofInPayload = "AA 55 0E 01 AA 55 00 08 00 04 FF 07 01 04 00 00 FF 0F A6 43";

        /// <summary>STATUS running=1 rate=100 rx_crc_err=3 tx_drop=0</summary>
        public const string Status = "AA 55 08 82 01 00 64 00 03 00 00 00 11 57";

        /// <summary>TYPE 0x7F、LEN 255、payload 全 0</summary>
        public static byte[] MaxLenFrame()
        {
            return Concat(Hex("AA 55 FF 7F"), new byte[255], Hex("B4 1F"));
        }

        public static byte[] Hex(string s)
        {
            return s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(h => Convert.ToByte(h, 16))
                    .ToArray();
        }

        public static string ToHex(byte[] b)
        {
            return BitConverter.ToString(b).Replace('-', ' ');
        }

        public static byte[] Slice(byte[] src, int offset, int count)
        {
            var dst = new byte[count];
            Array.Copy(src, offset, dst, 0, count);
            return dst;
        }

        public static byte[] Concat(params byte[][] parts)
        {
            return parts.SelectMany(p => p).ToArray();
        }
    }
}
