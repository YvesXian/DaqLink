using System;
using DaqLink.Core.Analysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaqLink.Core.Tests
{
    [TestClass]
    public class RunningStatsTests
    {
        private const double Eps = 1e-9;

        private static RunningStats Of(params double[] xs)
        {
            var s = new RunningStats();
            foreach (var x in xs)
                s.Add(x);
            return s;
        }

        // ------------------------------------------------------------------
        // 邊界
        // ------------------------------------------------------------------
        [TestMethod]
        public void Empty_CountZero_AllNaN()
        {
            var s = new RunningStats();

            Assert.AreEqual(0, s.Count);
            Assert.IsTrue(double.IsNaN(s.Min));
            Assert.IsTrue(double.IsNaN(s.Max));
            Assert.IsTrue(double.IsNaN(s.Mean));
            Assert.IsTrue(double.IsNaN(s.StdDev));
        }

        [TestMethod]
        public void SingleValue_StdDevIsZero()
        {
            var s = Of(42);

            Assert.AreEqual(1, s.Count);
            Assert.AreEqual(42, s.Min);
            Assert.AreEqual(42, s.Max);
            Assert.AreEqual(42, s.Mean, Eps);
            Assert.AreEqual(0, s.StdDev, Eps);
        }

        [TestMethod]
        public void ConstantValues_StdDevIsExactlyZero()
        {
            // 浮點誤差可能讓變異數變成極小的負數,Math.Sqrt(負數) = NaN
            var s = Of(0.1, 0.1, 0.1, 0.1, 0.1, 0.1, 0.1);

            Assert.IsFalse(double.IsNaN(s.StdDev));
            Assert.AreEqual(0, s.StdDev, Eps);
        }

        // ------------------------------------------------------------------
        // 數值
        // ------------------------------------------------------------------
        [TestMethod]
        public void KnownSet_MatchesTextbookValues()
        {
            // 經典範例:平均 5,母體標準差 2
            var s = Of(2, 4, 4, 4, 5, 5, 7, 9);

            Assert.AreEqual(8, s.Count);
            Assert.AreEqual(2, s.Min);
            Assert.AreEqual(9, s.Max);
            Assert.AreEqual(5, s.Mean, Eps);
            Assert.AreEqual(2, s.StdDev, Eps);
        }

        [TestMethod]
        public void NegativeValues_MinMaxCorrect()
        {
            // 誤差(ADC − DAC)可以是負的;Min 的初始值不可以是 0
            var s = Of(-3, -10, -7);

            Assert.AreEqual(-10, s.Min);
            Assert.AreEqual(-3, s.Max);
            Assert.AreEqual(-20.0 / 3, s.Mean, Eps);
        }

        [TestMethod]
        public void LargeOffset_StdDevStillAccurate()
        {
            // {4, 7, 13, 16} 的母體標準差 = sqrt(22.5)。全部加上 1e9 後標準差不變。
            // E[x²] − E[x]² 在這裡會是兩個約 1e18 的數相減,double 只有約 16 位有效數字 → 結果錯誤。
            var s = Of(1e9 + 4, 1e9 + 7, 1e9 + 13, 1e9 + 16);

            Assert.AreEqual(1e9 + 10, s.Mean, 1e-6);
            Assert.AreEqual(Math.Sqrt(22.5), s.StdDev, 1e-6);
        }

        [TestMethod]
        public void ManySamples_MatchesTwoPassComputation()
        {
            // 模擬 200 Hz 跑 10 分鐘:12 萬筆約 ±2 LSB 的誤差
            var rnd = new Random(1234);
            var xs = new double[120000];
            for (int i = 0; i < xs.Length; i++)
                xs[i] = rnd.Next(-2, 3) - 5;

            var s = Of(xs);

            double mean = 0;
            foreach (var x in xs) mean += x;
            mean /= xs.Length;
            double ss = 0;
            foreach (var x in xs) ss += (x - mean) * (x - mean);

            Assert.AreEqual(xs.Length, s.Count);
            Assert.AreEqual(mean, s.Mean, 1e-9);
            Assert.AreEqual(Math.Sqrt(ss / xs.Length), s.StdDev, 1e-9);
        }

        // ------------------------------------------------------------------
        // Reset
        // ------------------------------------------------------------------
        [TestMethod]
        public void Reset_ReturnsToEmptyState()
        {
            var s = Of(1, 2, 3);

            s.Reset();

            Assert.AreEqual(0, s.Count);
            Assert.IsTrue(double.IsNaN(s.Mean));
            Assert.IsTrue(double.IsNaN(s.Min));
        }

        [TestMethod]
        public void Reset_ThenAdd_DoesNotRememberOldValues()
        {
            var s = Of(-100, 100);

            s.Reset();
            s.Add(5);
            s.Add(7);

            Assert.AreEqual(5, s.Min);
            Assert.AreEqual(7, s.Max);
            Assert.AreEqual(6, s.Mean, Eps);
            Assert.AreEqual(1, s.StdDev, Eps);
        }
    }
}
