using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaqLink.Core.Models;
using DaqLink.Core.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaqLink.Core.Tests
{
    /// <summary>
    /// DaqClient 行為測試(不需要實機)。封包用 PacketBuilder 產生，
    /// 所以要先讓 Crc16Tests / PacketBuilderTests / PacketParserTests 通過。
    /// </summary>
    [TestClass]
    public class DaqClientTests
    {
        private static byte[] Ack(byte cmd) => PacketBuilder.Build(PacketType.Ack, new[] { cmd });

        private static byte[] Nak(byte cmd, NakError err) => PacketBuilder.Build(PacketType.Nak, new[] { cmd, (byte)err });

        private static byte[] Data(ushort seq)
        {
            var p = new byte[DataSample.PayloadLength];
            p[0] = (byte)seq;
            p[1] = (byte)(seq >> 8);
            return PacketBuilder.Build(PacketType.Data, p);
        }

        /// <summary>每個命令都回 ACK(GET_STATUS 回 STATUS)</summary>
        private static byte[] AckEverything(byte[] cmd, int n)
        {
            return cmd[3] == PacketType.GetStatus ? Vectors.Hex(Vectors.Status) : Ack(cmd[3]);
        }

        private static DaqClient Create(FakeSerialLink link, int timeoutMs = 50)
        {
            return new DaqClient(link) { CommandTimeout = TimeSpan.FromMilliseconds(timeoutMs) };
        }

        // ------------------------------------------------------------------
        // 命令與回應
        // ------------------------------------------------------------------
        [TestMethod]
        public async Task StartAsync_SendsStartFrame_ReturnsAck()
        {
            var link = new FakeSerialLink { Responder = AckEverything };
            var client = Create(link);

            var result = await client.StartAsync();

            Assert.IsTrue(result.IsAck);
            Assert.AreEqual(1, link.Writes.Count);
            Assert.AreEqual(Vectors.Start, Vectors.ToHex(link.Writes[0]));
        }

        [TestMethod]
        public async Task AllCommands_SendExpectedFrames()
        {
            var link = new FakeSerialLink { Responder = AckEverything };
            var client = Create(link);
            var wave = new WaveConfig(0, WaveType.Sine, 10, 1500, 2048);

            Assert.IsTrue((await client.StopAsync()).IsAck);
            Assert.IsTrue((await client.SetRateAsync(100)).IsAck);
            Assert.IsTrue((await client.SetWaveAsync(wave)).IsAck);

            Assert.AreEqual(Vectors.Stop, Vectors.ToHex(link.Writes[0]));
            Assert.AreEqual(Vectors.SetRate100, Vectors.ToHex(link.Writes[1]));
            Assert.AreEqual(Vectors.SetWaveASine, Vectors.ToHex(link.Writes[2]));
        }

        [TestMethod]
        public async Task Nak_ReturnedAsResultWithErrorCode()
        {
            var link = new FakeSerialLink { Responder = (cmd, n) => Nak(cmd[3], NakError.Busy) };
            var client = Create(link);

            var result = await client.SetRateAsync(100);

            Assert.IsFalse(result.IsAck);
            Assert.AreEqual(NakError.Busy, result.Error);
            Assert.AreEqual(1, link.Writes.Count, "NAK is a valid response, must not retry");
        }

        [TestMethod]
        public async Task GetStatusAsync_ReturnsParsedStatus()
        {
            var link = new FakeSerialLink { Responder = AckEverything };
            var client = Create(link);

            var st = await client.GetStatusAsync();

            Assert.AreEqual(Vectors.GetStatus, Vectors.ToHex(link.Writes[0]));
            Assert.IsTrue(st.Running);
            Assert.AreEqual(100, st.RateHz);
            Assert.AreEqual(3, st.RxCrcErr);
        }

        // ------------------------------------------------------------------
        // 逾時與重送(protocol.md 第 7 節)
        // ------------------------------------------------------------------
        [TestMethod]
        public async Task NoResponse_RetriesMaxRetriesTimes_ThenThrowsTimeout()
        {
            var link = new FakeSerialLink();          // 永遠不回應
            var client = Create(link);
            client.MaxRetries = 3;

            await Assert.ThrowsExceptionAsync<TimeoutException>(() => client.StartAsync());

            Assert.AreEqual(4, link.Writes.Count, "1 send + 3 retries");
            Assert.IsTrue(link.Writes.All(w => Vectors.ToHex(w) == Vectors.Start));
        }

        [TestMethod]
        public async Task ResponseOnSecondTry_ReturnsAck()
        {
            var link = new FakeSerialLink { Responder = (cmd, n) => n == 2 ? Ack(cmd[3]) : null };
            var client = Create(link);

            var result = await client.StartAsync();

            Assert.IsTrue(result.IsAck);
            Assert.AreEqual(2, link.Writes.Count);
        }

        [TestMethod]
        public async Task AckForAnotherCommand_IsIgnored()
        {
            var link = new FakeSerialLink { Responder = (cmd, n) => Ack(PacketType.Stop) };
            var client = Create(link);
            client.MaxRetries = 0;

            await Assert.ThrowsExceptionAsync<TimeoutException>(() => client.StartAsync());
        }

        [TestMethod]
        public async Task GetStatus_IgnoresAck_WaitsForStatus()
        {
            var link = new FakeSerialLink { Responder = (cmd, n) => Ack(cmd[3]) };
            var client = Create(link);
            client.MaxRetries = 0;

            await Assert.ThrowsExceptionAsync<TimeoutException>(() => client.GetStatusAsync());
        }

        [TestMethod]
        public async Task ConcurrentCommands_AreSerialized()
        {
            // Stop-and-wait:前一個命令還沒收到回應時，不可以送出下一個命令
            var link = new FakeSerialLink();
            int outstanding = 0;
            bool overlapped = false;
            link.Responder = (cmd, n) =>
            {
                if (Interlocked.Exchange(ref outstanding, 1) == 1)
                    overlapped = true;
                var reply = Ack(cmd[3]);
                Task.Run(async () =>
                {
                    await Task.Delay(20);
                    Volatile.Write(ref outstanding, 0);
                    link.Inject(reply);
                });
                return null;
            };
            var client = Create(link, timeoutMs: 500);

            var results = await Task.WhenAll(client.StartAsync(), client.SetRateAsync(100), client.StopAsync());

            Assert.IsFalse(overlapped, "a command was sent while another was still waiting");
            Assert.IsTrue(results.All(r => r.IsAck));
            CollectionAssert.AreEquivalent(
                new[] { PacketType.Start, PacketType.SetRate, PacketType.Stop },
                link.Writes.Select(w => w[3]).ToArray());
        }

        [TestMethod]
        public async Task TimeoutOnFirstCommand_DoesNotBlockNextCommand()
        {
            var link = new FakeSerialLink { Responder = (cmd, n) => cmd[3] == PacketType.Stop ? Ack(cmd[3]) : null };
            var client = Create(link);
            client.MaxRetries = 0;

            await Assert.ThrowsExceptionAsync<TimeoutException>(() => client.StartAsync());
            var result = await client.StopAsync();

            Assert.IsTrue(result.IsAck);
        }

        // ------------------------------------------------------------------
        // 資料封包
        // ------------------------------------------------------------------
        [TestMethod]
        public void SampleReceived_ProvidesDecodedSample()
        {
            var link = new FakeSerialLink();
            var client = Create(link);
            DataSample got = null;
            client.SampleReceived += (s, e) => got = e;

            link.Inject(Vectors.Hex(Vectors.Data));

            Assert.IsNotNull(got);
            Assert.AreEqual(1, got.Seq);
            Assert.AreEqual(2048, got.DacA);
            Assert.AreEqual(4095, got.Adc3);
            Assert.AreEqual(1, client.SampleCount);
        }

        [TestMethod]
        public void SeqGap_CountsLostPackets()
        {
            var link = new FakeSerialLink();
            var client = Create(link);

            link.Inject(Vectors.Concat(Data(0), Data(1), Data(2), Data(5)));

            Assert.AreEqual(4, client.SampleCount);
            Assert.AreEqual(2, client.LostCount);
        }

        [TestMethod]
        public void SeqWrapAround_IsNotLoss()
        {
            var link = new FakeSerialLink();
            var client = Create(link);

            link.Inject(Vectors.Concat(Data(65534), Data(65535), Data(0), Data(1)));

            Assert.AreEqual(0, client.LostCount);
        }

        [TestMethod]
        public async Task StartAck_ResetsSeqTracking()
        {
            var link = new FakeSerialLink { Responder = AckEverything };
            var client = Create(link);

            link.Inject(Vectors.Concat(Data(100), Data(101)));
            await client.StartAsync();
            link.Inject(Vectors.Concat(Data(0), Data(1)));

            Assert.AreEqual(4, client.SampleCount);
            Assert.AreEqual(0, client.LostCount);
        }

        [TestMethod]
        public async Task DataWhileWaitingForAck_StillDelivered()
        {
            var link = new FakeSerialLink { Responder = (cmd, n) => Vectors.Concat(Data(7), Data(8), Ack(cmd[3])) };
            var client = Create(link);
            int samples = 0;
            client.SampleReceived += (s, e) => Interlocked.Increment(ref samples);

            var result = await client.StopAsync();

            Assert.IsTrue(result.IsAck);
            Assert.AreEqual(2, samples);
        }

        [TestMethod]
        public void BadCrc_CountedInCrcErrors()
        {
            var link = new FakeSerialLink();
            var client = Create(link);
            var bad = Data(1);
            bad[bad.Length - 1] ^= 0xFF;

            link.Inject(Vectors.Concat(bad, Data(2)));

            Assert.AreEqual(1, client.CrcErrors);
            Assert.AreEqual(1, client.SampleCount);
        }

        // ------------------------------------------------------------------
        // 連線
        // ------------------------------------------------------------------
        [TestMethod]
        public void LinkFault_RaisesConnectionLost()
        {
            var link = new FakeSerialLink();
            var client = Create(link);
            var lost = new ManualResetEventSlim();
            Exception reason = null;
            client.ConnectionLost += (s, e) => { reason = e; lost.Set(); };

            link.RaiseFault(new IOException("unplugged"));

            Assert.IsTrue(lost.Wait(1000));
            Assert.IsInstanceOfType(reason, typeof(IOException));
        }

        [TestMethod]
        public void Dispose_UnsubscribesFromLink()
        {
            var link = new FakeSerialLink();
            var client = Create(link);
            int samples = 0;
            client.SampleReceived += (s, e) => samples++;

            client.Dispose();
            link.Inject(Data(0));

            Assert.AreEqual(0, samples);
        }
    }
}
