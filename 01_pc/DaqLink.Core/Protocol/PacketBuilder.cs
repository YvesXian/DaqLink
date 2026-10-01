using System;
using DaqLink.Core.Models;

namespace DaqLink.Core.Protocol
{
    /// <summary>組出要送給 MCU 的完整封包(protocol.md 第 2、5 節)</summary>
    public static class PacketBuilder
    {
        /// <summary>
        /// SOF(AA 55) + LEN + TYPE + PAYLOAD + CRC16(little-endian,計算範圍 LEN~PAYLOAD)。
        /// payload 可為空陣列;null 丟 ArgumentNullException,長度超過 255 丟 ArgumentException。
        /// </summary>
        public static byte[] Build(byte type, byte[] payload)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));
            
            if (payload.Length > Frame.MaxPayload)
                throw new ArgumentException($"payload must be <= {Frame.MaxPayload} bytes", nameof(payload));

            int n = payload.Length;
            var frame = new byte[n + Frame.Overhead];

            frame[0] = Frame.Sof1;
            frame[1] = Frame.Sof2;
            frame[2] = (byte)payload.Length;
            frame[3] = type;
            Array.Copy(payload, 0, frame, 4, n);
            ushort crc = Crc16.Compute(frame, 2, n + 2);
            frame[4 + n] = (byte)crc;
            frame[5 + n] = (byte)(crc >> 8);

            return frame;
        }

        public static byte[] Start()
        {
            return Build(PacketType.Start, new byte[0]);
        }

        public static byte[] Stop()
        {
            return Build(PacketType.Stop, new byte[0]);
        }

        public static byte[] GetStatus()
        {
            return Build(PacketType.GetStatus, new byte[0]);
        }

        /// <summary>SET_RATE:payload = rate_hz(u16 LE)。範圍檢查交給 MCU(回 NAK 0x03)。</summary>
        public static byte[] SetRate(ushort rateHz)
        {
            var payload = new byte[2];
            PutU16(payload, 0, rateHz);

            return Build(PacketType.SetRate, payload);
        }

        /// <summary>SET_WAVE:payload = ch(u8) wave(u8) freq_x10(u16) amp(u16) offset(u16),皆 LE。</summary>
        public static byte[] SetWave(WaveConfig wave)
        {
            if (wave == null)
                throw new ArgumentNullException(nameof(wave));

            var payload = new byte[8];
            payload[0] = wave.Channel;
            payload[1] = (byte)wave.Type;
            PutU16(payload, 2, wave.FreqX10);
            PutU16(payload, 4, wave.Amp);
            PutU16(payload, 6, wave.Offset);

            return Build(PacketType.SetWave, payload);
        }

        /// <summary>寫入 u16 little-endian,對應韌體的 put_u16_le()</summary>
        private static void PutU16(byte[] p, int i, ushort v)
        {
            p[i] = (byte)v;
            p[i + 1] = (byte)(v >> 8);
        }
    }
}
