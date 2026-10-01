using System.Collections.Generic;
using System.Linq;
using DaqLink.Core.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaqLink.Core.Tests
{
    [TestClass]
    public class PacketParserTests
    {
        private static byte[] Corrupt(byte[] frame)
        {
            var bad = (byte[])frame.Clone();
            bad[bad.Length - 1] ^= 0xFF;
            return bad;
        }

        private static List<Packet> FeedAll(PacketParser parser, params byte[][] chunks)
        {
            var result = new List<Packet>();
            foreach (var c in chunks)
                result.AddRange(parser.Feed(c));
            return result;
        }

        [TestMethod]
        public void SingleDataPacket()
        {
            var parser = new PacketParser();
            var packets = parser.Feed(Vectors.Hex(Vectors.Data));

            Assert.AreEqual(1, packets.Count);
            Assert.AreEqual(PacketType.Data, packets[0].Type);
            CollectionAssert.AreEqual(Vectors.Slice(Vectors.Hex(Vectors.Data), 4, 14), packets[0].Payload);
            Assert.AreEqual(0, parser.CrcErrors);
        }

        [DataTestMethod]
        [DataRow(Vectors.Start, PacketType.Start, 0)]
        [DataRow(Vectors.SetRate100, PacketType.SetRate, 2)]
        [DataRow(Vectors.SetWaveASine, PacketType.SetWave, 8)]
        [DataRow(Vectors.AckStart, PacketType.Ack, 1)]
        [DataRow(Vectors.NakSetRateBusy, PacketType.Nak, 2)]
        [DataRow(Vectors.Status, PacketType.Status, 8)]
        public void ProtocolVectors_ParseToTypeAndLength(string hex, byte type, int len)
        {
            var packets = new PacketParser().Feed(Vectors.Hex(hex));

            Assert.AreEqual(1, packets.Count);
            Assert.AreEqual(type, packets[0].Type);
            Assert.AreEqual(len, packets[0].Payload.Length);
        }

        [TestMethod]
        public void ByteByByte_SameResult()
        {
            var parser = new PacketParser();
            var stream = Vectors.Concat(Vectors.Hex(Vectors.Data), Vectors.Hex(Vectors.AckStart));

            var packets = FeedAll(parser, stream.Select(b => new[] { b }).ToArray());

            Assert.AreEqual(2, packets.Count);
            Assert.AreEqual(PacketType.Data, packets[0].Type);
            Assert.AreEqual(PacketType.Ack, packets[1].Type);
        }

        [TestMethod]
        public void PartialPacket_KeptUntilRestArrives()
        {
            var parser = new PacketParser();
            var frame = Vectors.Hex(Vectors.Data);

            Assert.AreEqual(0, parser.Feed(Vectors.Slice(frame, 0, 5)).Count);
            Assert.AreEqual(1, parser.Feed(Vectors.Slice(frame, 5, frame.Length - 5)).Count);
        }

        [TestMethod]
        public void BackToBackPackets_InOneChunk()
        {
            var d = Vectors.Hex(Vectors.Data);
            var packets = new PacketParser().Feed(Vectors.Concat(d, d, d));

            Assert.AreEqual(3, packets.Count);
        }

        [TestMethod]
        public void GarbageBeforePacket_Skipped()
        {
            var parser = new PacketParser();
            var packets = parser.Feed(Vectors.Concat(Vectors.Hex("00 13 AA 13 55 AA"), Vectors.Hex(Vectors.Data)));

            Assert.AreEqual(1, packets.Count);
            Assert.AreEqual(0, parser.CrcErrors);
        }

        [TestMethod]
        public void DoubleAA_BeforeSof()
        {
            var packets = new PacketParser().Feed(Vectors.Concat(Vectors.Hex("AA"), Vectors.Hex(Vectors.Data)));

            Assert.AreEqual(1, packets.Count);
        }

        [TestMethod]
        public void SofSplitAcrossChunks()
        {
            var parser = new PacketParser();
            var frame = Vectors.Hex(Vectors.Data);

            Assert.AreEqual(0, parser.Feed(Vectors.Hex("00 AA")).Count);        // AA 是下一包 SOF 的第一個 byte
            Assert.AreEqual(1, parser.Feed(Vectors.Slice(frame, 1, frame.Length - 1)).Count);
        }

        [TestMethod]
        public void BadCrc_CountedAndFollowingPacketRecovered()
        {
            var parser = new PacketParser();
            var good = Vectors.Hex(Vectors.Data);

            var packets = parser.Feed(Vectors.Concat(Corrupt(good), good));

            Assert.AreEqual(1, packets.Count);
            Assert.AreEqual(1, parser.CrcErrors);
        }

        [TestMethod]
        public void SofInsidePayload_ParsedNormally()
        {
            var parser = new PacketParser();
            var packets = parser.Feed(Vectors.Hex(Vectors.DataSofInPayload));

            Assert.AreEqual(1, packets.Count);
            Assert.AreEqual(0xAA, packets[0].Payload[0]);
            Assert.AreEqual(0x55, packets[0].Payload[1]);
            Assert.AreEqual(0, parser.CrcErrors);
        }

        [TestMethod]
        public void BadPacketWithSofInPayload_ResyncFromNextByte()
        {
            // 壞掉的封包 payload 內含 AA 55:重新同步時會先撞到這個假 SOF,
            // 解析器必須能從假 SOF 中恢復，仍然找到後面的正確封包。
            var parser = new PacketParser();
            var bad = Corrupt(Vectors.Hex(Vectors.DataSofInPayload));
            var good = Vectors.Hex(Vectors.Data);

            var packets = parser.Feed(Vectors.Concat(bad, good, good));

            Assert.AreEqual(2, packets.Count);
            Assert.IsTrue(packets.All(p => p.Type == PacketType.Data && p.Payload[0] == 0x01));
            Assert.IsTrue(parser.CrcErrors >= 1);
        }

        [TestMethod]
        public void TruncatedPacket_DoesNotSwallowNextOne()
        {
            // 封包中途斷掉(只收到前 10 byte),緊接著一包完整封包。
            // 截斷的封包會把後面的 bytes 當成自己的 payload → CRC 錯 → 從下一個 byte 重新同步。
            var parser = new PacketParser();
            var good = Vectors.Hex(Vectors.Data);

            var packets = parser.Feed(Vectors.Concat(Vectors.Slice(good, 0, 10), good, good));

            Assert.AreEqual(2, packets.Count);
            Assert.AreEqual(1, parser.CrcErrors);
        }

        [TestMethod]
        public void MaxLen255_Parsed()
        {
            var packets = new PacketParser().Feed(Vectors.MaxLenFrame());

            Assert.AreEqual(1, packets.Count);
            Assert.AreEqual(255, packets[0].Payload.Length);
        }

        [TestMethod]
        public void FeedWithOffsetAndCount()
        {
            var padded = Vectors.Concat(Vectors.Hex("11 22"), Vectors.Hex(Vectors.Data), Vectors.Hex("AA 55 0E"));
            var parser = new PacketParser();

            var packets = parser.Feed(padded, 2, 20);

            Assert.AreEqual(1, packets.Count);
            Assert.AreEqual(0, parser.CrcErrors);
        }

        [TestMethod]
        public void Reset_DropsPartialData()
        {
            var parser = new PacketParser();
            var frame = Vectors.Hex(Vectors.Data);

            parser.Feed(Vectors.Slice(frame, 0, 8));
            parser.Reset();

            Assert.AreEqual(1, parser.Feed(frame).Count);
            Assert.AreEqual(0, parser.CrcErrors);
        }

        [TestMethod]
        public void EmptyFeed_ReturnsEmptyList()
        {
            var packets = new PacketParser().Feed(new byte[0]);

            Assert.IsNotNull(packets);
            Assert.AreEqual(0, packets.Count);
        }
    }
}
