using System;
using System.Threading.Tasks;
using System.Windows.Input;
using DaqLink.App.Mvvm;
using DaqLink.Core.Models;

namespace DaqLink.App.ViewModels
{
    /// <summary>單一 DAC 通道的波形設定</summary>
    public sealed class WaveSettingsViewModel : ObservableObject
    {
        private readonly byte _channel;
        private WaveType _type;
        private double _freqHz;
        private int _amp;
        private int _offset;

        public WaveSettingsViewModel(byte channel, WaveType type, double freqHz, int amp, int offset,
                                     Func<WaveSettingsViewModel, Task> apply, Func<bool> canApply)
        {
            _channel = channel;
            _type = type;
            _freqHz = freqHz;
            _amp = amp;
            _offset = offset;
            ApplyCommand = new AsyncRelayCommand(() => apply(this), canApply);
        }

        public string Title => $"Channel {"AB"[_channel]}  (DAC {"AB"[_channel]} → ADC CH{_channel})";

        public WaveType[] Types { get; } = (WaveType[])Enum.GetValues(typeof(WaveType));

        public WaveType Type
        {
            get => _type;
            set
            {
                if (Set(ref _type, value))
                    OnPropertyChanged(nameof(IsPeriodic));
            }
        }

        /// <summary>DC 時頻率與振幅沒有意義，UI 停用這兩欄</summary>
        public bool IsPeriodic => _type != WaveType.Dc;

        public double FreqHz
        {
            get => _freqHz;
            set => Set(ref _freqHz, value);
        }

        public int Amp
        {
            get => _amp;
            set => Set(ref _amp, value);
        }

        public int Offset
        {
            get => _offset;
            set => Set(ref _offset, value);
        }

        public ICommand ApplyCommand { get; }

        /// <summary>
        /// 只檢查能不能放進協定欄位(u16);offset ± amp、Nyquist 等規則由 MCU 檢查，
        /// 不符合時 MCU 回 NAK 0x03,PC 端不重複實作一份規則。
        /// </summary>
        public bool TryGetConfig(out WaveConfig config, out string error)
        {
            config = null;
            var freqX10 = Math.Round(_freqHz * 10);
            if (freqX10 < 0 || freqX10 > ushort.MaxValue)
                error = "frequency out of range";
            else if (_amp < 0 || _amp > ushort.MaxValue)
                error = "amp out of range";
            else if (_offset < 0 || _offset > ushort.MaxValue)
                error = "offset out of range";
            else
            {
                error = null;
                config = new WaveConfig(_channel, _type, (ushort)freqX10, (ushort)_amp, (ushort)_offset);
            }
            return config != null;
        }
    }
}
