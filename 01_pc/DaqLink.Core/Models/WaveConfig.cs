namespace DaqLink.Core.Models
{
    public enum WaveType : byte
    {
        Dc = 0,
        Sine = 1,
        Triangle = 2,
        Square = 3,
    }

    /// <summary>SET_WAVE 參數(protocol.md 5.4)。範圍檢查交給 MCU(回 NAK 0x03)。</summary>
    public sealed class WaveConfig
    {
        public WaveConfig(byte channel, WaveType type, ushort freqX10, ushort amp, ushort offset)
        {
            Channel = channel;
            Type = type;
            FreqX10 = freqX10;
            Amp = amp;
            Offset = offset;
        }

        public byte Channel { get; }        // 0 = A、1 = B
        public WaveType Type { get; }
        public ushort FreqX10 { get; }      // 0.1 Hz 為單位
        public ushort Amp { get; }          // 波峰到中心(code)
        public ushort Offset { get; }       // 中心值(code)

        public override string ToString()
        {
            return $"ch={"AB"[Channel & 1]} {Type} {FreqX10 / 10.0:0.0} Hz amp={Amp} offset={Offset}";
        }
    }
}
