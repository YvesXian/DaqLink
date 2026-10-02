using System;

namespace DaqLink.Core.Analysis
{
    /// <summary>
    /// 累加式統計：每次 Add 一個值，隨時可取 Count / Min / Max / Mean / StdDev。
    /// 不保存樣本，記憶體用量固定(O(1)),可以跑任意長的時間。
    ///
    /// 規則:
    /// - StdDev 為母體標準差(除以 n),與 tools/dc_sweep.py 的 mean_std 一致。
    /// - 沒有樣本時 Min / Max / Mean / StdDev 皆為 double.NaN;只有一個樣本時 StdDev = 0。
    /// - 變異數必須用 Welford 演算法累加，不可用 E[x²] − E[x]²(大偏移量時會發生災難性抵銷)。
    ///
    /// 非執行緒安全：只在單一執行緒(UI tick)使用。
    /// </summary>
    public sealed class RunningStats
    {
        private long _count;
        private double _min;
        private double _max;
        private double _mean;
        private double _m2;

        public RunningStats()
        {
            Reset();
        }

        public long Count
        {
            get { return _count; }
        }

        public double Min
        {
            get { return _count == 0 ? double.NaN : _min; }
        }

        public double Max
        {
            get { return _count == 0 ? double.NaN : _max; }
        }

        public double Mean
        {
            get { return _count == 0 ? double.NaN : _mean; }
        }

        /// <summary>母體標準差 sqrt(Σ(x − mean)² / n)</summary>
        public double StdDev
        {
            get { return _count == 0 ? double.NaN : Math.Sqrt(Math.Max(0, _m2 / _count)); }
        }

        public void Add(double x)
        {
            _count++;

            if (x < _min)
                _min = x;
            if (x > _max)
                _max = x;

            double delta = x - _mean;
            _mean += delta / _count;
            _m2 += delta * (x - _mean);
        }

        /// <summary>回到沒有樣本的狀態</summary>
        public void Reset()
        {
            _min = double.PositiveInfinity;
            _max = double.NegativeInfinity;
            _count = 0;
            _mean = 0;
            _m2 = 0;
        }
    }
}
