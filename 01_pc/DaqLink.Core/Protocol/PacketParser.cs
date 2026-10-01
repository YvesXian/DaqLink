using System;
using System.Collections.Generic;

namespace DaqLink.Core.Protocol
{
    /// <summary>
    /// 位元組串流 → 封包(protocol.md 2.2)。
    /// - 封包可能被切成好幾段收到，不完整的部分要保留到下一次 Feed。
    /// - CRC 錯誤:CrcErrors + 1,從該封包 SOF 的「下一個 byte」重新搜尋 AA 55(不可整包丟棄)。
    /// 非執行緒安全：同一時間只能由一個執行緒呼叫 Feed。
    /// </summary>
    public sealed class PacketParser
    {
        private readonly List<byte> _buf = new List<byte>();

        public int CrcErrors { get; private set; }

        /// <summary>餵入新收到的 data[offset .. offset + count),回傳其中解析出的完整封包(0 個或多個)。</summary>
        public IList<Packet> Feed(byte[] data, int offset, int count)
        {
            var result = new List<Packet>();

            /* 1. 新資料接到緩衝區尾端 */
            for (int i = offset; i < offset + count; i++)
                this._buf.Add(data[i]);

            /* 2. 從緩衝區開頭盡量切出完整封包 */
            while (true)
            {
                int sof = this.FindSof();
                if (sof < 0)
                {
                    /* 整個緩衝區都沒有 AA 55,全部是垃圾;但最後一個 byte 若是 AA,
                       可能是下一包 SOF 的前半，要留著 */
                    bool keepLast = this._buf.Count > 0 && this._buf[this._buf.Count - 1] == Frame.Sof1;
                    this._buf.Clear();
                    if (keepLast)
                        this._buf.Add(Frame.Sof1);
                    break;
                }
                this._buf.RemoveRange(0, sof);

                if (this._buf.Count < 4)
                    break;

                int len = this._buf[2];
                int total = len + Frame.Overhead;
                if (this._buf.Count < total)
                    break;

                var frame = this._buf.GetRange(0, total).ToArray();
                ushort crc = (ushort)(frame[total - 2] | (frame[total - 1] << 8));
                if (Crc16.Compute(frame, 2, len + 2) != crc)
                {
                    this.CrcErrors++;
                    this._buf.RemoveAt(0);
                    continue;
                }

                var payload = new byte[len];
                Array.Copy(frame, 4, payload, 0, len);
                result.Add(new Packet(frame[3], payload));
                this._buf.RemoveRange(0, total);
            }

            return result;
        }

        public IList<Packet> Feed(byte[] data)
        {
            return Feed(data, 0, data.Length);
        }

        /// <summary>清掉尚未解析完的 bytes(重新連線時使用);CrcErrors 不歸零。</summary>
        public void Reset()
        {
            this._buf.Clear();
        }

        /// <summary>在緩衝區裡找 AA 55, 回傳 AA 的位置; 找不到回傳 -1</summary>
        private int FindSof()
        {
            for (int i = 0; i + 1 < this._buf.Count; i++)
            {
                if (this._buf[i] == Frame.Sof1 && this._buf[i + 1] == Frame.Sof2)
                    return i;
            }

            return -1;
        }
    }
}
