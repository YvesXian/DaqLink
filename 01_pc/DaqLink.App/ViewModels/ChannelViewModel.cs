using DaqLink.App.Mvvm;

namespace DaqLink.App.ViewModels
{
    /// <summary>即時數值表的一列:ADC 讀值與它應該等於的參考值</summary>
    public sealed class ChannelViewModel : ObservableObject
    {
        private int? _reference;
        private int? _adc;

        public ChannelViewModel(string name, string source)
        {
            Name = name;
            Source = source;
        }

        public string Name { get; }

        public string Source { get; }

        /// <summary>理想讀值:CH0/CH1 為當下的 DAC code,CH2 為 0,CH3 為 4095</summary>
        public int? Reference
        {
            get => _reference;
            private set => Set(ref _reference, value);
        }

        public int? Adc
        {
            get => _adc;
            private set => Set(ref _adc, value);
        }

        public int? Error => _adc - _reference;

        public void Update(int reference, int adc)
        {
            Reference = reference;
            Adc = adc;
            OnPropertyChanged(nameof(Error));
        }

        public void Clear()
        {
            Reference = null;
            Adc = null;
            OnPropertyChanged(nameof(Error));
        }
    }
}
