using System;

namespace DaqLink.Core.Models
{
    /// <summary>DATA 封包內容(protocol.md 第 4 節)</summary>
    public sealed class DataSample
    {
        public const int PayloadLength = 14;

        public DataSample(ushort seq, ushort dacA, ushort dacB, ushort adc0, ushort adc1, ushort adc2, ushort adc3)
        {
            Seq = seq;
            DacA = dacA;
            DacB = dacB;
            Adc0 = adc0;
            Adc1 = adc1;
            Adc2 = adc2;
            Adc3 = adc3;
        }

        public ushort Seq { get; }
        public ushort DacA { get; }
        public ushort DacB { get; }
        public ushort Adc0 { get; }     // CH0 ← VOUTA
        public ushort Adc1 { get; }     // CH1 ← VOUTB
        public ushort Adc2 { get; }     // CH2 ← GND,預期 ≈ 0
        public ushort Adc3 { get; }     // CH3 ← VREF,預期 ≈ 4095

        /// <summary>比例式量測下理想 adc0 = dac_a,差值即迴路誤差(LSB)</summary>
        public int ErrorA => Adc0 - DacA;
        public int ErrorB => Adc1 - DacB;

        public static DataSample FromPayload(byte[] p)
        {
            if (p == null || p.Length != PayloadLength)
                throw new ArgumentException($"DATA payload must be {PayloadLength} bytes", nameof(p));
            return new DataSample(U16(p, 0), U16(p, 2), U16(p, 4), U16(p, 6), U16(p, 8), U16(p, 10), U16(p, 12));
        }

        internal static ushort U16(byte[] p, int i)
        {
            return (ushort)(p[i] | (p[i + 1] << 8));
        }
    }
}
