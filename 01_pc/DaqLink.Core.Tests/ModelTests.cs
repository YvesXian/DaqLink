using System;
using DaqLink.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaqLink.Core.Tests
{
    [TestClass]
    public class ModelTests
    {
        [TestMethod]
        public void DataSample_FromProtocolExample()
        {
            var s = DataSample.FromPayload(Vectors.Slice(Vectors.Hex(Vectors.Data), 4, 14));

            Assert.AreEqual(1, s.Seq);
            Assert.AreEqual(2048, s.DacA);
            Assert.AreEqual(1024, s.DacB);
            Assert.AreEqual(2047, s.Adc0);
            Assert.AreEqual(1025, s.Adc1);
            Assert.AreEqual(0, s.Adc2);
            Assert.AreEqual(4095, s.Adc3);
            Assert.AreEqual(-1, s.ErrorA);
            Assert.AreEqual(1, s.ErrorB);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void DataSample_WrongLength_Throws() => DataSample.FromPayload(new byte[13]);

        [TestMethod]
        public void DeviceStatus_FromPayload()
        {
            var st = DeviceStatus.FromPayload(Vectors.Slice(Vectors.Hex(Vectors.Status), 4, 8));

            Assert.IsTrue(st.Running);
            Assert.AreEqual(100, st.RateHz);
            Assert.AreEqual(3, st.RxCrcErr);
            Assert.AreEqual(0, st.TxDrop);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void DeviceStatus_WrongLength_Throws() => DeviceStatus.FromPayload(new byte[7]);
    }
}
