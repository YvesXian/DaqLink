using System;

namespace DaqLink.Core.Protocol
{
    /// <summary>
    /// CRC-16/CCITT-FALSE(protocol.md 2.1):多項式 0x1021、初始值 0xFFFF、不反射、最終 XOR 0x0000。
    /// 檢查值:ASCII "123456789" → 0x29B1。
    /// </summary>
    public static class Crc16
    {
        private const ushort Poly = 0x1021;
        private const ushort Init = 0xFFFF;

        /// <summary>計算 data[offset .. offset + count) 的 CRC。</summary>
        public static ushort Compute(byte[] data, int offset, int count)
        {
            ushort crc = Init;

            for (int i = offset; i < offset + count; i++)
                crc = Update(crc, data[i]);

            return crc;
        }

        public static ushort Compute(byte[] data)
        {
            return Compute(data, 0, data.Length);
        }

        private static ushort Update(ushort crc, byte b)
        {
            crc ^= (ushort)(b << 8);
            for (int i = 0; i < 8; i++)
            {
                if ((crc & 0x8000) != 0)
                    crc = (ushort)((crc << 1) ^ Poly);
                else
                    crc = (ushort)(crc << 1);
            }
            return crc;
        }
    }
}
