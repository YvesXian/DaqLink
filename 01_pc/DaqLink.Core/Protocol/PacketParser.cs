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
        public int CrcErrors { get; private set; }

        /// <summary>餵入新收到的 data[offset .. offset + count),回傳其中解析出的完整封包(0 個或多個)。</summary>
        public IList<Packet> Feed(byte[] data, int offset, int count)
        {
            // TODO(使用者實作)
            throw new NotImplementedException();
        }

        public IList<Packet> Feed(byte[] data)
        {
            return Feed(data, 0, data.Length);
        }

        /// <summary>清掉尚未解析完的 bytes(重新連線時使用);CrcErrors 不歸零。</summary>
        public void Reset()
        {
            // TODO(使用者實作)
            throw new NotImplementedException();
        }
    }
}
