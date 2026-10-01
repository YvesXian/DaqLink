using System;
using DaqLink.Core.Models;
using DaqLink.Core.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaqLink.Core.Tests
{
    [TestClass]
    public class PacketBuilderTests
    {
        private static void AssertFrame(string expectedHex, byte[] actual)
        {
            Assert.AreEqual(expectedHex, Vectors.ToHex(actual));
        }

        [TestMethod]
        public void Start() => AssertFrame(Vectors.Start, PacketBuilder.Start());

        [TestMethod]
        public void Stop() => AssertFrame(Vectors.Stop, PacketBuilder.Stop());

        [TestMethod]
        public void GetStatus() => AssertFrame(Vectors.GetStatus, PacketBuilder.GetStatus());

        [TestMethod]
        public void SetRate100() => AssertFrame(Vectors.SetRate100, PacketBuilder.SetRate(100));

        [TestMethod]
        public void SetWave_ChannelA_Sine_1Hz_Amp1500_Offset2048()
        {
            var wave = new WaveConfig(0, WaveType.Sine, 10, 1500, 2048);
            AssertFrame(Vectors.SetWaveASine, PacketBuilder.SetWave(wave));
        }

        [TestMethod]
        public void SetWave_LittleEndianFields()
        {
            var frame = PacketBuilder.SetWave(new WaveConfig(1, WaveType.Square, 0x1234, 0x0567, 0x0ABC));

            Assert.AreEqual(8 + Frame.Overhead, frame.Length);
            Assert.AreEqual(8, frame[2]);
            Assert.AreEqual(PacketType.SetWave, frame[3]);
            CollectionAssert.AreEqual(
                new byte[] { 0x01, 0x03, 0x34, 0x12, 0x67, 0x05, 0xBC, 0x0A },
                Vectors.Slice(frame, 4, 8));
        }

        [TestMethod]
        public void Build_AckAndNakAndData_MatchProtocolVectors()
        {
            AssertFrame(Vectors.AckStart, PacketBuilder.Build(PacketType.Ack, new byte[] { 0x10 }));
            AssertFrame(Vectors.NakSetRateBusy, PacketBuilder.Build(PacketType.Nak, new byte[] { 0x12, 0x04 }));

            var data = Vectors.Hex(Vectors.Data);
            AssertFrame(Vectors.Data, PacketBuilder.Build(PacketType.Data, Vectors.Slice(data, 4, 14)));
        }

        [TestMethod]
        public void Build_MaxPayload255()
        {
            var frame = PacketBuilder.Build(0x7F, new byte[255]);

            Assert.AreEqual(255 + Frame.Overhead, frame.Length);
            Assert.AreEqual(255, frame[2]);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Build_PayloadOver255_Throws() => PacketBuilder.Build(0x7F, new byte[256]);

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Build_NullPayload_Throws() => PacketBuilder.Build(0x10, null);
    }
}
