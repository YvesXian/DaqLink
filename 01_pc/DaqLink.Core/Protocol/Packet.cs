using System;

namespace DaqLink.Core.Protocol
{
    /// <summary>已通過 CRC 驗證的封包(不含 SOF、LEN、CRC)</summary>
    public sealed class Packet
    {
        public Packet(byte type, byte[] payload)
        {
            Type = type;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }

        public byte Type { get; }

        public byte[] Payload { get; }

        public override string ToString()
        {
            return $"TYPE=0x{Type:X2} LEN={Payload.Length} [{BitConverter.ToString(Payload).Replace('-', ' ')}]";
        }
    }
}
