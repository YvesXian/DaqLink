using System;
using DaqLink.Core.Analysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaqLink.Core.Tests
{
    [TestClass]
    public class SeqTimelineTests
    {
        private const double Eps = 1e-12;

        [TestMethod]
        public void FirstSample_IsTimeZero_RegardlessOfSeq()
        {
            var t = new SeqTimeline(100);

            Assert.AreEqual(0, t.Advance(500));
            Assert.AreEqual(0, t.Index);
            Assert.AreEqual(0, t.Seconds, Eps);
        }

        [TestMethod]
        public void ConsecutiveSeq_AdvancesOnePeriod()
        {
            var t = new SeqTimeline(200);

            t.Advance(0);
            t.Advance(1);
            t.Advance(2);

            Assert.AreEqual(2, t.Index);
            Assert.AreEqual(0.010, t.Seconds, Eps);
        }

        [TestMethod]
        public void Gap_ReportsLost_AndTimeKeepsMoving()
        {
            var t = new SeqTimeline(100);
            t.Advance(10);

            var lost = t.Advance(14);

            Assert.AreEqual(3, lost);
            Assert.AreEqual(4, t.Index);
            Assert.AreEqual(0.04, t.Seconds, Eps);
        }

        [TestMethod]
        public void WrapAround_IsContinuous()
        {
            var t = new SeqTimeline(100);

            t.Advance(65534);
            Assert.AreEqual(0, t.Advance(65535));
            Assert.AreEqual(0, t.Advance(0));
            Assert.AreEqual(0, t.Advance(1));

            Assert.AreEqual(3, t.Index);
        }

        [TestMethod]
        public void Reset_RestartsFromZero_WithNewRate()
        {
            var t = new SeqTimeline(100);
            t.Advance(0);
            t.Advance(50);

            t.Reset(10);
            t.Advance(0);
            t.Advance(1);

            Assert.AreEqual(1, t.Index);
            Assert.AreEqual(0.1, t.Seconds, Eps);
        }

        [TestMethod]
        public void NonPositiveRate_Throws()
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new SeqTimeline(0));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new SeqTimeline(double.NaN));
        }
    }
}
