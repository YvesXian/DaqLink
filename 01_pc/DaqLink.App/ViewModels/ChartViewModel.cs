using DaqLink.Core.Models;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;

namespace DaqLink.App.ViewModels
{
    /// <summary>
    /// 即時圖表(只在 UI 執行緒使用):
    /// - 上圖：理論波形(DAC code,虛線)與實測波形(ADC,實線)疊圖。
    /// - 下圖：迴路誤差 ADC − DAC(LSB),比例式量測下理想為 0。
    /// 時間軸用 seq / rate 推算，不用 PC 收到的時間 —— FT232 latency timer(16 ms)
    /// 會讓封包成批到達，用接收時間畫圖會變成鋸齒。遺失的封包在線上留下斷點。
    /// 只保留最近 WindowSeconds 秒。
    /// </summary>
    public sealed class ChartViewModel
    {
        public const double WindowSeconds = 5.0;

        private static readonly OxyColor ColorA = OxyColor.FromRgb(0x1F, 0x77, 0xB4);
        private static readonly OxyColor ColorB = OxyColor.FromRgb(0xE3, 0x6C, 0x0A);

        private readonly LineSeries _dacA = Line("DAC A", ColorA, LineStyle.Dash);
        private readonly LineSeries _adc0 = Line("ADC CH0", ColorA, LineStyle.Solid);
        private readonly LineSeries _dacB = Line("DAC B", ColorB, LineStyle.Dash);
        private readonly LineSeries _adc1 = Line("ADC CH1", ColorB, LineStyle.Solid);
        private readonly LineSeries _errA = Line("CH0 − A", ColorA, LineStyle.Solid);
        private readonly LineSeries _errB = Line("CH1 − B", ColorB, LineStyle.Solid);
        private readonly LinearAxis _waveX;
        private readonly LinearAxis _errX;

        private double _rate = 100;
        private long _index;            /* 從 Reset 起累計的樣本序號(seq 是 16-bit,會繞回) */
        private int _lastSeq = -1;

        public ChartViewModel()
        {
            _waveX = TimeAxis();
            WaveModel = new PlotModel();
            WaveModel.Axes.Add(_waveX);
            WaveModel.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "code",
                Minimum = 0,
                Maximum = 4096,
                MajorStep = 1024,
                MajorGridlineStyle = LineStyle.Dot,
                IsZoomEnabled = false,
                IsPanEnabled = false,
            });
            AddSeries(WaveModel, _dacA, _adc0, _dacB, _adc1);

            _errX = TimeAxis();
            ErrorModel = new PlotModel();
            ErrorModel.Axes.Add(_errX);
            ErrorModel.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "error (LSB)",
                MinimumRange = 20,      // 自動縮放，但至少 ±10 LSB,避免雜訊被放大成滿版
                MajorGridlineStyle = LineStyle.Dot,
                IsZoomEnabled = false,
                IsPanEnabled = false,
            });
            AddSeries(ErrorModel, _errA, _errB);

            Reset(_rate);
        }

        public PlotModel WaveModel { get; }

        public PlotModel ErrorModel { get; }

        /// <summary>清空圖表，時間軸從 0 重新開始(START 時呼叫)</summary>
        public void Reset(double rateHz)
        {
            _rate = rateHz > 0 ? rateHz : 100;
            _index = 0;
            _lastSeq = -1;
            foreach (var s in AllSeries())
                s.Points.Clear();
            SetWindow(0);
            Invalidate();
        }

        public void Add(DataSample s)
        {
            if (_lastSeq >= 0)
            {
                var diff = (ushort)(s.Seq - _lastSeq);
                if (diff > 1)
                {
                    foreach (var series in AllSeries())
                        series.Points.Add(DataPoint.Undefined);     // 遺失封包 → 斷線
                }
                _index += diff;
            }
            _lastSeq = s.Seq;

            var t = _index / _rate;
            _dacA.Points.Add(new DataPoint(t, s.DacA));
            _adc0.Points.Add(new DataPoint(t, s.Adc0));
            _dacB.Points.Add(new DataPoint(t, s.DacB));
            _adc1.Points.Add(new DataPoint(t, s.Adc1));
            _errA.Points.Add(new DataPoint(t, s.ErrorA));
            _errB.Points.Add(new DataPoint(t, s.ErrorB));
        }

        /// <summary>丟掉視窗外的點、捲動時間軸、重畫;每個 UI tick 呼叫一次</summary>
        public void Flush()
        {
            var now = _index / _rate;
            var start = now - WindowSeconds;
            foreach (var s in AllSeries())
                Trim(s, start);
            SetWindow(now);
            Invalidate();
        }

        private void SetWindow(double now)
        {
            var min = now < WindowSeconds ? 0 : now - WindowSeconds;
            _waveX.Minimum = _errX.Minimum = min;
            _waveX.Maximum = _errX.Maximum = min + WindowSeconds;
        }

        private void Invalidate()
        {
            WaveModel.InvalidatePlot(true);
            ErrorModel.InvalidatePlot(true);
        }

        private LineSeries[] AllSeries()
        {
            return new[] { _dacA, _adc0, _dacB, _adc1, _errA, _errB };
        }

        private static void Trim(LineSeries s, double start)
        {
            int n = 0;
            while (n < s.Points.Count && (double.IsNaN(s.Points[n].X) || s.Points[n].X < start))
                n++;
            if (n > 0)
                s.Points.RemoveRange(0, n);
        }

        private static LinearAxis TimeAxis()
        {
            return new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Title = "t (s)",
                MajorGridlineStyle = LineStyle.Dot,
                IsZoomEnabled = false,
                IsPanEnabled = false,
            };
        }

        private static LineSeries Line(string title, OxyColor color, LineStyle style)
        {
            return new LineSeries
            {
                Title = title,
                Color = color,
                LineStyle = style,
                StrokeThickness = style == LineStyle.Dash ? 1.5 : 1.2,
                CanTrackerInterpolatePoints = false,
            };
        }

        private static void AddSeries(PlotModel model, params LineSeries[] series)
        {
            model.Legends.Add(new Legend
            {
                LegendPosition = LegendPosition.RightTop,
                LegendPlacement = LegendPlacement.Inside,
                LegendBackground = OxyColor.FromAColor(200, OxyColors.White),
            });
            foreach (var s in series)
                model.Series.Add(s);
        }
    }
}
