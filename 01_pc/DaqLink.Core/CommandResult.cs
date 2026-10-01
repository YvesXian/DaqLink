using DaqLink.Core.Protocol;

namespace DaqLink.Core
{
    /// <summary>命令的回應:ACK 或 NAK(含錯誤碼)</summary>
    public sealed class CommandResult
    {
        public static readonly CommandResult Ack = new CommandResult(NakError.None);

        private CommandResult(NakError error)
        {
            Error = error;
        }

        public bool IsAck => Error == NakError.None;

        public NakError Error { get; }

        public static CommandResult Nak(NakError error)
        {
            return new CommandResult(error);
        }

        public override string ToString()
        {
            return IsAck ? "ACK" : $"NAK 0x{(byte)Error:X2} ({Error})";
        }
    }
}
