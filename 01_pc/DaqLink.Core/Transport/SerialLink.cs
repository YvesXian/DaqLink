using System;
using System.IO;
using System.IO.Ports;
using System.Threading;

namespace DaqLink.Core.Transport
{
    /// <summary>
    /// COM Port 實作(115200 8N1,無流量控制)。
    /// 不用 SerialPort.DataReceived 事件，改用專屬讀取執行緒阻塞讀取：
    /// USB 拔除時 Read 會丟 IOException / UnauthorizedAccessException,可以明確偵測斷線。
    /// </summary>
    public sealed class SerialLink : ISerialLink
    {
        public const int BaudRate = 115200;

        private readonly object _gate = new object();
        private SerialPort _port;
        private Thread _reader;
        private volatile bool _closing;

        public event EventHandler<byte[]> BytesReceived;
        public event EventHandler<Exception> Faulted;

        public bool IsOpen
        {
            get { lock (_gate) return _port != null && _port.IsOpen; }
        }

        public static string[] GetPortNames()
        {
            var names = SerialPort.GetPortNames();
            Array.Sort(names, StringComparer.OrdinalIgnoreCase);
            return names;
        }

        public void Open(string portName)
        {
            lock (_gate)
            {
                if (_port != null)
                    throw new InvalidOperationException("already open");

                var port = new SerialPort(portName, BaudRate, Parity.None, 8, StopBits.One)
                {
                    Handshake = Handshake.None,
                    ReadTimeout = 100,      // 讓讀取執行緒定期檢查 _closing
                    WriteTimeout = 500,
                };
                port.Open();
                port.DiscardInBuffer();

                _closing = false;
                _port = port;
                _reader = new Thread(ReadLoop) { IsBackground = true, Name = "SerialLink.Read" };
                _reader.Start(port);
            }
        }

        public void Close()
        {
            Thread reader;
            lock (_gate)
            {
                if (_port == null)
                    return;
                _closing = true;
                reader = _reader;
                SafeClose(_port);
                _port = null;
                _reader = null;
            }
            if (reader != null && reader != Thread.CurrentThread)
                reader.Join(500);
        }

        public void Write(byte[] data)
        {
            SerialPort port;
            lock (_gate)
                port = _port;
            if (port == null)
                throw new InvalidOperationException("not open");

            try
            {
                port.Write(data, 0, data.Length);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                Fault(port, ex);
                throw new IOException("serial write failed", ex);
            }
        }

        public void Dispose()
        {
            Close();
        }

        private void ReadLoop(object state)
        {
            var port = (SerialPort)state;
            var buf = new byte[4096];
            while (!_closing)
            {
                int n;
                try
                {
                    n = port.Read(buf, 0, buf.Length);
                }
                catch (TimeoutException)
                {
                    continue;
                }
                catch (Exception ex)
                {
                    if (!_closing)
                        Fault(port, ex);
                    return;
                }

                if (n > 0)
                {
                    var chunk = new byte[n];
                    Buffer.BlockCopy(buf, 0, chunk, 0, n);
                    BytesReceived?.Invoke(this, chunk);
                }
            }
        }

        private void Fault(SerialPort port, Exception ex)
        {
            lock (_gate)
            {
                if (_port != port)
                    return;             // 已經關閉或換了新連線
                _closing = true;
                SafeClose(port);
                _port = null;
                _reader = null;
            }
            Faulted?.Invoke(this, ex);
        }

        private static void SafeClose(SerialPort port)
        {
            try
            {
                port.Close();
            }
            catch (Exception)
            {
                // USB 已拔除時 Close 也可能丟例外，此時連線本來就不存在了
            }
        }
    }
}
