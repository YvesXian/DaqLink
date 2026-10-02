using System;

namespace DaqLink.Core.Analysis
{
    /// <summary>
    /// 把 DATA 封包的 16-bit seq 換算成「從第一筆起算」的時間(秒)。
    ///
    /// 用 seq / rate 而不用 PC 收到的時間：FT232 latency timer(16 ms)讓封包成批到達，
    /// 接收時間不等於取樣時間。seq 在 65535 → 0 繞回時以 (ushort) 差值處理(protocol.md 4.2);
    /// 遺失的封包仍會推進時間，所以時間軸不會因掉包而被壓縮。
    /// </summary>
    public sealed class SeqTimeline
    {
        private int _lastSeq = -1;      /* -1 = 還沒有第一筆 */

        public SeqTimeline(double rateHz)
        {
            Reset(rateHz);
        }

        public double RateHz { get; private set; }

        /// <summary>目前這筆是從第一筆起算的第幾個取樣週期</summary>
        public long Index { get; private set; }

        public double Seconds => Index / RateHz;

        /// <summary>重新開始(START 時呼叫,MCU 會把 seq 歸零)</summary>
        public void Reset(double rateHz)
        {
            if (!(rateHz > 0))
                throw new ArgumentOutOfRangeException(nameof(rateHz), rateHz, "rate must be positive");
            RateHz = rateHz;
            Index = 0;
            _lastSeq = -1;
        }

        /// <summary>推進到這筆 seq;回傳和上一筆之間遺失的筆數(第一筆回傳 0)</summary>
        public int Advance(ushort seq)
        {
            int lost = 0;
            if (_lastSeq >= 0)
            {
                var diff = (ushort)(seq - _lastSeq);
                Index += diff;
                if (diff > 1)
                    lost = diff - 1;
            }
            _lastSeq = seq;
            return lost;
        }
    }
}
