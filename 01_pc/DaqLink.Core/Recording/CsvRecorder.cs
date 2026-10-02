using System;
using System.Globalization;
using System.IO;
using System.Text;
using DaqLink.Core.Analysis;
using DaqLink.Core.Models;

namespace DaqLink.Core.Recording
{
    /// <summary>
    /// 把 DATA 樣本逐筆寫成 CSV。一個檔案對應一段連續採樣(START → STOP),
    /// 時間欄 t_s 由 seq / rate 推算(見 <see cref="SeqTimeline"/>),從 0 開始。
    ///
    /// 數字一律用 InvariantCulture,避免在小數點是逗號的系統語系下寫出壞掉的 CSV。
    /// 非執行緒安全。
    /// </summary>
    public sealed class CsvRecorder : IDisposable
    {
        public const string Header = "t_s,seq,dac_a,dac_b,adc0,adc1,adc2,adc3,err_a,err_b";

        private readonly TextWriter _writer;
        private readonly SeqTimeline _timeline;

        public CsvRecorder(TextWriter writer, double rateHz)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _timeline = new SeqTimeline(rateHz);
            _writer.WriteLine(Header);
        }

        /// <summary>建立檔案(必要時建立資料夾),UTF-8 無 BOM</summary>
        public static CsvRecorder Create(string path, double rateHz)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            return new CsvRecorder(new StreamWriter(path, false, new UTF8Encoding(false)), rateHz);
        }

        /// <summary>已寫入的資料列數(不含標頭)</summary>
        public long Rows { get; private set; }

        /// <summary>依 seq 推算遺失的筆數</summary>
        public long Lost { get; private set; }

        public void Write(DataSample s)
        {
            Lost += _timeline.Advance(s.Seq);
            _writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0:0.######},{1},{2},{3},{4},{5},{6},{7},{8},{9}",
                _timeline.Seconds, s.Seq, s.DacA, s.DacB, s.Adc0, s.Adc1, s.Adc2, s.Adc3, s.ErrorA, s.ErrorB));
            Rows++;
        }

        public void Flush()
        {
            _writer.Flush();
        }

        public void Dispose()
        {
            _writer.Dispose();
        }
    }
}
