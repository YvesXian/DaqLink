using System;
using System.Threading;
using System.Threading.Tasks;
using DaqLink.Core.Models;
using DaqLink.Core.Protocol;
using DaqLink.Core.Transport;

namespace DaqLink.Core
{
    /// <summary>
    /// DaqLink 裝置的協定層用戶端：送命令、等回應、分派資料封包。
    ///
    /// 規則(protocol.md 第 7 節):
    /// - Stop-and-wait:同一時間只有一個命令在等回應(多個呼叫端同時呼叫時要排隊)。
    /// - 等待 CommandTimeout 沒有回應就重送，最多重試 MaxRetries 次;仍失敗丟 TimeoutException。
    /// - ACK/NAK 的 payload[0] 是被回應的命令 TYPE,要和目前等待中的命令比對;GET_STATUS 的回應是 STATUS。
    /// - DATA 封包隨時可能到達(包含等待回應期間),一律觸發 SampleReceived。
    /// - seq 檢查(protocol.md 4.2):diff = (ushort)(seq − prevSeq),diff &gt; 1 時 LostCount += diff − 1。
    ///   收到 START 的 ACK 時重設，下一筆 DATA 視為新的開始(MCU 會把 seq 歸零)。
    ///
    /// 執行緒：封包由 ISerialLink 的背景執行緒送進來，事件也在該執行緒觸發，UI 端要自行切回 UI 執行緒。
    /// 本類別不負責開關 ISerialLink。
    /// </summary>
    public sealed class DaqClient : IDisposable
    {
        private readonly ISerialLink _link;
        private readonly PacketParser _parser = new PacketParser();
        private int _prevSeq = -1;              /* -1 = 沒有上一筆(剛建立或剛 START) */
        private long _sampleCount;
        private long _lostCount;

        public DaqClient(ISerialLink link)
        {
            this._link = link ?? throw new ArgumentNullException(nameof(link));
            this._link.BytesReceived += OnBytesReceived;
            this._link.Faulted += OnFaulted;
        }

        public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromMilliseconds(200);

        public int MaxRetries { get; set; } = 3;

        /// <summary>收到 DATA 封包(背景執行緒)</summary>
        public event EventHandler<DataSample> SampleReceived;

        /// <summary>底層連線異常(背景執行緒);命令逾時不觸發這個事件，而是讓命令丟 TimeoutException</summary>
        public event EventHandler<Exception> ConnectionLost;

        /// <summary>收到的 DATA 封包總數</summary>
        public long SampleCount => Interlocked.Read(ref _sampleCount);

        /// <summary>依 seq 推算遺失的 DATA 封包數</summary>
        public long LostCount => Interlocked.Read(ref _lostCount);

        /// <summary>PC 端解析器的 CRC 錯誤數</summary>
        public int CrcErrors => _parser.CrcErrors;

        public async Task<CommandResult> StartAsync()
        {
            return ToResult(await SendCommandAsync(PacketBuilder.Start(), PacketType.Start).ConfigureAwait(false));
        }

        public async Task<CommandResult> StopAsync()
        {
            return ToResult(await SendCommandAsync(PacketBuilder.Stop(), PacketType.Stop).ConfigureAwait(false));
        }

        public async Task<CommandResult> SetRateAsync(ushort rateHz)
        {
            return ToResult(await SendCommandAsync(PacketBuilder.SetRate(rateHz), PacketType.SetRate).ConfigureAwait(false));
        }

        public async Task<CommandResult> SetWaveAsync(WaveConfig wave)
        {
            return ToResult(await SendCommandAsync(PacketBuilder.SetWave(wave), PacketType.SetWave).ConfigureAwait(false));
        }

        /// <summary>MCU 只回 STATUS、不回 ACK(protocol.md 5.5)</summary>
        public async Task<DeviceStatus> GetStatusAsync()
        {
            var p = await SendCommandAsync(PacketBuilder.GetStatus(), PacketType.GetStatus).ConfigureAwait(false);

            return DeviceStatus.FromPayload(p.Payload);
        }

        public void Dispose()
        {
            _link.BytesReceived -= OnBytesReceived;
            _link.Faulted -= OnFaulted;
        }

        private void OnBytesReceived(object sender, byte[] data)
        {
            foreach (var p in this._parser.Feed(data))
            {
                if (p.Type == PacketType.Data)
                    HandleData(p);
                else
                    HandleResponse(p);
            }
        }

        private void HandleData(Packet p)
        {
            if (p.Payload.Length != DataSample.PayloadLength)
                return;

            var sample = DataSample.FromPayload(p.Payload);
            if (_prevSeq >= 0)
            {
                var diff = (ushort)(sample.Seq - _prevSeq);
                if(diff > 1)
                    Interlocked.Add(ref _lostCount, diff - 1);
            }
            _prevSeq = sample.Seq;
            Interlocked.Increment(ref _sampleCount);
            SampleReceived?.Invoke(this, sample);
        }

        private void HandleResponse(Packet p)
        {
            var pending = _pending;
            if (pending == null)
                return;

            bool match;
            if (pending.Cmd == PacketType.GetStatus)
                match = p.Type == PacketType.Status && p.Payload.Length == DeviceStatus.PayloadLength;
            else if (p.Type == PacketType.Ack)
                match = p.Payload.Length == 1 && p.Payload[0] == pending.Cmd;
            else if (p.Type == PacketType.Nak)
                match = p.Payload.Length == 2 && p.Payload[0] == pending.Cmd;
            else
                match = false;

            if (!match)
                return;

            if (p.Type == PacketType.Ack && pending.Cmd == PacketType.Start)
                _prevSeq = -1;

            pending.Tcs.TrySetResult(p);
        }

        private void OnFaulted(object sender, Exception ex)
        {
            ConnectionLost?.Invoke(this, ex);
        }

        private readonly SemaphoreSlim _cmdLock = new SemaphoreSlim(1, 1);
        private volatile Pending _pending;

        private sealed class Pending
        {
            public byte Cmd { get; }

            public Pending(byte cmd)
            {
                this.Cmd = cmd;
            }

            public TaskCompletionSource<Packet> Tcs { get; } =
                new TaskCompletionSource<Packet>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private async Task<Packet> SendCommandAsync(byte[] frame, byte cmd)
        {
            await _cmdLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var pending = new Pending(cmd);
                _pending = pending;

                for (int attempt = 0; attempt <= MaxRetries; attempt++)
                {
                    _link.Write(frame);
                    var done = await Task.WhenAny(pending.Tcs.Task, Task.Delay(CommandTimeout)).ConfigureAwait(false);
                    if (done == pending.Tcs.Task)
                        return await pending.Tcs.Task.ConfigureAwait(false);
                }

                throw new TimeoutException($"No response to command 0x{cmd:X2} after {MaxRetries + 1} attempts");
            }
            finally
            {
                _pending = null;
                _cmdLock.Release();
            }
        }

        private static CommandResult ToResult(Packet p)
        {
            return p.Type == PacketType.Ack ? CommandResult.Ack : CommandResult.Nak((NakError)p.Payload[1]);
        }
    }
}
