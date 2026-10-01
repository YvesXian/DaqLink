using System;

namespace DaqLink.Core.Transport
{
    /// <summary>
    /// 位元組傳輸層。DaqClient 只依賴這個介面，單元測試可換成假的實作，不需要實機。
    /// </summary>
    public interface ISerialLink : IDisposable
    {
        bool IsOpen { get; }

        void Open(string portName);

        void Close();

        void Write(byte[] data);

        /// <summary>收到資料;在背景執行緒觸發，每次觸發的陣列都是新配置的，接收端可以保留。</summary>
        event EventHandler<byte[]> BytesReceived;

        /// <summary>連線異常(例如 USB 被拔掉);在背景執行緒觸發，觸發後連線已關閉。</summary>
        event EventHandler<Exception> Faulted;
    }
}
