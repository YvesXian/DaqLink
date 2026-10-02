using System;
using System.Globalization;
using System.IO;
using System.Threading;
using DaqLink.Core.Models;
using DaqLink.Core.Recording;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaqLink.Core.Tests
{
    [TestClass]
    public class CsvRecorderTests
    {
        private static DataSample Sample(ushort seq, ushort dacA = 2048, ushort adc0 = 2042)
        {
            return new DataSample(seq, dacA, 1024, adc0, 1020, 0, 4095);
        }

        private static string[] Lines(StringWriter w)
        {
            return w.ToString().Split(new[] { w.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        }

        [TestMethod]
        public void NewRecorder_WritesHeaderOnly()
        {
            var w = new StringWriter();

            using (new CsvRecorder(w, 100)) { }

            CollectionAssert.AreEqual(new[] { CsvRecorder.Header }, Lines(w));
        }

        [TestMethod]
        public void Row_ContainsAllFieldsAndErrors()
        {
            var w = new StringWriter();
            var rec = new CsvRecorder(w, 100);

            rec.Write(Sample(7));

            Assert.AreEqual("0,7,2048,1024,2042,1020,0,4095,-6,-4", Lines(w)[1]);
            Assert.AreEqual(1, rec.Rows);
        }

        [TestMethod]
        public void TimeColumn_FromSeq_IncludingGap()
        {
            var w = new StringWriter();
            var rec = new CsvRecorder(w, 200);

            rec.Write(Sample(0));
            rec.Write(Sample(1));
            rec.Write(Sample(5));       // 遺失 2、3、4

            var lines = Lines(w);
            Assert.AreEqual("0", lines[1].Split(',')[0]);
            Assert.AreEqual("0.005", lines[2].Split(',')[0]);
            Assert.AreEqual("0.025", lines[3].Split(',')[0]);
            Assert.AreEqual(3, rec.Rows);
            Assert.AreEqual(3, rec.Lost);
        }

        [TestMethod]
        public void UsesInvariantCulture_EvenUnderCommaDecimalLocale()
        {
            var saved = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");    // 小數點是逗號
                var w = new StringWriter();
                var rec = new CsvRecorder(w, 200);

                rec.Write(Sample(0));
                rec.Write(Sample(1));

                Assert.AreEqual(10, Lines(w)[2].Split(',').Length);
                StringAssert.StartsWith(Lines(w)[2], "0.005,");
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = saved;
            }
        }

        [TestMethod]
        public void Create_WritesFileThatCanBeReadBack()
        {
            var dir = Path.Combine(Path.GetTempPath(), "DaqLinkTests_" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(dir, "sub", "rec.csv");
            try
            {
                using (var rec = CsvRecorder.Create(path, 100))
                    rec.Write(Sample(0));

                var lines = File.ReadAllLines(path);
                Assert.AreEqual(2, lines.Length);
                Assert.AreEqual(CsvRecorder.Header, lines[0]);
                Assert.AreNotEqual(0xEF, File.ReadAllBytes(path)[0], "no UTF-8 BOM");
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
        }
    }
}
