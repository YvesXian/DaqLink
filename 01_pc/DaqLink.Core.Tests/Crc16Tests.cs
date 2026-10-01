using System.Text;
using DaqLink.Core.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaqLink.Core.Tests
{
    [TestClass]
    public class Crc16Tests
    {
        [TestMethod]
        public void CheckValue_123456789_Is0x29B1()
        {
            Assert.AreEqual((ushort)0x29B1, Crc16.Compute(Encoding.ASCII.GetBytes("123456789")));
        }

        [TestMethod]
        public void EmptyInput_ReturnsInitValue()
        {
            Assert.AreEqual((ushort)0xFFFF, Crc16.Compute(new byte[0]));
        }

        [DataTestMethod]
        [DataRow(Vectors.Start)]
        [DataRow(Vectors.Stop)]
        [DataRow(Vectors.SetRate100)]
        [DataRow(Vectors.SetWaveASine)]
        [DataRow(Vectors.GetStatus)]
        [DataRow(Vectors.AckStart)]
        [DataRow(Vectors.NakSetRateBusy)]
        [DataRow(Vectors.Data)]
        public void ProtocolVectors_CrcMatchesLastTwoBytes(string hex)
        {
            var frame = Vectors.Hex(hex);
            var expected = (ushort)(frame[frame.Length - 2] | (frame[frame.Length - 1] << 8));

            // CRC 範圍：從 LEN 到 PAYLOAD 結尾(不含 SOF、不含 CRC 本身)
            Assert.AreEqual(expected, Crc16.Compute(frame, 2, frame.Length - 4));
        }

        [TestMethod]
        public void OffsetAndCount_OnlyCoverGivenRange()
        {
            var padded = Encoding.ASCII.GetBytes("xx123456789yy");
            Assert.AreEqual((ushort)0x29B1, Crc16.Compute(padded, 2, 9));
        }
    }
}
