using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DaqLink.Core.Transport;

namespace DaqLink.Core.Tests
{
    /// <summary>
    /// 假的傳輸層：記錄所有寫出的 bytes;Responder 可依收到的命令決定要回什麼。
    /// 回應和 Inject 的資料都在背景執行緒送出，模擬真實 SerialLink 的讀取執行緒。
    /// </summary>
    internal sealed class FakeSerialLink : ISerialLink
    {
        private readonly object _gate = new object();
        private readonly List<byte[]> _writes = new List<byte[]>();

        public event EventHandler<byte[]> BytesReceived;
        public event EventHandler<Exception> Faulted;

        /// <summary>收到第 n 次(從 1 起算)寫入時要回的 bytes;回傳 null 表示不回應。</summary>
        public Func<byte[], int, byte[]> Responder { get; set; }

        /// <summary>回應延遲，模擬 UART + FT232 latency</summary>
        public int ResponseDelayMs { get; set; } = 5;

        public bool IsOpen { get; private set; } = true;

        public IReadOnlyList<byte[]> Writes
        {
            get { lock (_gate) return _writes.ToArray(); }
        }

        public void Open(string portName) => IsOpen = true;

        public void Close() => IsOpen = false;

        public void Dispose() => Close();

        public void Write(byte[] data)
        {
            int n;
            lock (_gate)
            {
                _writes.Add((byte[])data.Clone());
                n = _writes.Count;
            }

            var reply = Responder?.Invoke(data, n);
            if (reply != null)
                InjectLater(reply, ResponseDelayMs);
        }

        /// <summary>在背景執行緒送出 bytes,並等它處理完</summary>
        public void Inject(byte[] data)
        {
            Task.Run(() => BytesReceived?.Invoke(this, data)).Wait();
        }

        public void InjectLater(byte[] data, int delayMs)
        {
            Task.Run(async () =>
            {
                await Task.Delay(delayMs).ConfigureAwait(false);
                BytesReceived?.Invoke(this, data);
            });
        }

        public void RaiseFault(Exception ex)
        {
            IsOpen = false;
            ThreadPool.QueueUserWorkItem(_ => Faulted?.Invoke(this, ex));
        }
    }
}
