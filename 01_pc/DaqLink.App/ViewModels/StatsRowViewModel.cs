using System;
using System.Globalization;
using DaqLink.App.Mvvm;
using DaqLink.Core.Analysis;
using DaqLink.Core.Models;

namespace DaqLink.App.ViewModels
{
    /// <summary>
    /// 統計表的一列。每筆樣本都 Add(UI tick 內),但畫面只在 Refresh 時更新一次,
    /// 避免 200 Hz 下每筆都觸發 PropertyChanged。
    /// </summary>
    public sealed class StatsRowViewModel : ObservableObject
    {
        private readonly RunningStats _stats = new RunningStats();
        private readonly Func<DataSample, int> _select;

        public StatsRowViewModel(string name, string meaning, Func<DataSample, int> select)
        {
            Name = name;
            Meaning = meaning;
            _select = select;
        }

        public string Name { get; }

        /// <summary>這一列的平均/標準差代表什麼(例如 offset / noise)</summary>
        public string Meaning { get; }

        public long Count => _stats.Count;
        public string Min => Format(_stats.Min, "0");
        public string Max => Format(_stats.Max, "0");
        public string Mean => Format(_stats.Mean, "0.00");
        public string StdDev => Format(_stats.StdDev, "0.00");

        public void Add(DataSample s)
        {
            _stats.Add(_select(s));
        }

        public void Reset()
        {
            _stats.Reset();
            Refresh();
        }

        public void Refresh()
        {
            OnPropertyChanged(string.Empty);    // 空字串 = 所有屬性都變了
        }

        private static string Format(double v, string format)
        {
            return double.IsNaN(v) ? "-" : v.ToString(format, CultureInfo.InvariantCulture);
        }
    }
}
