using System;
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

        public DaqClient(ISerialLink link)
        {
            _link = link ?? throw new ArgumentNullException(nameof(link));
            // TODO(使用者實作):訂閱 _link.BytesReceived / _link.Faulted
        }

        public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromMilliseconds(200);

        public int MaxRetries { get; set; } = 3;

        /// <summary>收到 DATA 封包(背景執行緒)</summary>
        public event EventHandler<DataSample> SampleReceived;

        /// <summary>底層連線異常(背景執行緒);命令逾時不觸發這個事件，而是讓命令丟 TimeoutException</summary>
        public event EventHandler<Exception> ConnectionLost;

        /// <summary>收到的 DATA 封包總數</summary>
        public long SampleCount { get; private set; }

        /// <summary>依 seq 推算遺失的 DATA 封包數</summary>
        public long LostCount { get; private set; }

        /// <summary>PC 端解析器的 CRC 錯誤數</summary>
        public int CrcErrors
        {
            get
            {
                // TODO(使用者實作)
                throw new NotImplementedException();
            }
        }

        public Task<CommandResult> StartAsync()
        {
            // TODO(使用者實作)
            throw new NotImplementedException();
        }

        public Task<CommandResult> StopAsync()
        {
            // TODO(使用者實作)
            throw new NotImplementedException();
        }

        public Task<CommandResult> SetRateAsync(ushort rateHz)
        {
            // TODO(使用者實作)
            throw new NotImplementedException();
        }

        public Task<CommandResult> SetWaveAsync(WaveConfig wave)
        {
            // TODO(使用者實作)
            throw new NotImplementedException();
        }

        /// <summary>MCU 只回 STATUS、不回 ACK(protocol.md 5.5)</summary>
        public Task<DeviceStatus> GetStatusAsync()
        {
            // TODO(使用者實作)
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            // TODO(使用者實作):取消訂閱 _link 的事件
        }
    }
}
