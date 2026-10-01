using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using DaqLink.App.Mvvm;
using DaqLink.Core;
using DaqLink.Core.Models;
using DaqLink.Core.Transport;

namespace DaqLink.App.ViewModels
{
    /// <summary>
    /// 主畫面。執行緒模型:
    /// - DaqClient 的事件在 SerialLink 的讀取執行緒觸發 → DATA 只放進 ConcurrentQueue,不碰 UI。
    /// - UI 執行緒每 50 ms(DispatcherTimer)把 queue 一次取完再更新畫面。
    ///   200 Hz 下每次約 10 筆，避免每一筆都 Dispatcher.Invoke 造成 UI 塞車。
    /// - 命令都在 UI 執行緒 await,回來時已經回到 UI 執行緒，可以直接改屬性。
    /// </summary>
    public sealed class MainViewModel : ObservableObject, IDisposable
    {
        private static readonly TimeSpan UiInterval = TimeSpan.FromMilliseconds(50);
        private static readonly TimeSpan NoDataTimeout = TimeSpan.FromSeconds(1);
        private const int MaxLogLines = 300;

        private readonly Dispatcher _ui;
        private readonly DispatcherTimer _timer;
        private readonly ConcurrentQueue<DataSample> _queue = new ConcurrentQueue<DataSample>();
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private SerialLink _link;
        private DaqClient _client;

        private string _selectedPort;
        private bool _isConnected;
        private bool _isRunning;
        private int _selectedRate = 100;
        private long _sampleCount;
        private long _lostCount;
        private int _crcErrors;
        private int _packetsPerSecond;
        private string _mcuStatus = "-";

        private TimeSpan _lastSampleAt;
        private bool _noDataWarned;
        private TimeSpan _rateWindowStart;
        private int _rateWindowCount;

        public MainViewModel()
        {
            _ui = Dispatcher.CurrentDispatcher;
            _timer = new DispatcherTimer(UiInterval, DispatcherPriority.Background, (s, e) => OnUiTick(), _ui);
            _timer.Stop();

            WaveA = new WaveSettingsViewModel(0, WaveType.Sine, 1.0, 1500, 2048, ApplyWaveAsync, () => IsConnected);
            WaveB = new WaveSettingsViewModel(1, WaveType.Triangle, 1.0, 1500, 2048, ApplyWaveAsync, () => IsConnected);

            Channels.Add(new ChannelViewModel("CH0", "VOUTA (DAC A)"));
            Channels.Add(new ChannelViewModel("CH1", "VOUTB (DAC B)"));
            Channels.Add(new ChannelViewModel("CH2", "GND"));
            Channels.Add(new ChannelViewModel("CH3", "VREF"));

            RefreshPortsCommand = new RelayCommand(RefreshPorts, () => !IsConnected);
            ConnectCommand = new AsyncRelayCommand(ToggleConnectionAsync, () => IsConnected || !string.IsNullOrEmpty(SelectedPort));
            StartCommand = new AsyncRelayCommand(StartAsync, () => IsConnected && !IsRunning);
            StopCommand = new AsyncRelayCommand(StopAsync, () => IsConnected);
            ApplyRateCommand = new AsyncRelayCommand(ApplyRateAsync, () => CanChangeRate);
            GetStatusCommand = new AsyncRelayCommand(GetStatusAsync, () => IsConnected);
            ClearLogCommand = new RelayCommand(() => Log.Clear());

            RefreshPorts();
        }

        // ------------------------------------------------------------------
        // 繫結屬性
        // ------------------------------------------------------------------
        public ObservableCollection<string> Ports { get; } = new ObservableCollection<string>();

        public string SelectedPort
        {
            get => _selectedPort;
            set => Set(ref _selectedPort, value);
        }

        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (Set(ref _isConnected, value))
                    StateChanged();
            }
        }

        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (Set(ref _isRunning, value))
                    StateChanged();
            }
        }

        public bool CanSelectPort => !IsConnected;

        public string ConnectButtonText => IsConnected ? "Disconnect" : "Connect";

        public string ConnectionText => !IsConnected ? "Disconnected" : IsRunning ? $"{SelectedPort} · Running" : $"{SelectedPort} · Stopped";

        /// <summary>SET_RATE 僅限停止狀態(protocol.md 5.3)</summary>
        public bool CanChangeRate => IsConnected && !IsRunning;

        public int[] Rates { get; } = { 10, 20, 50, 100, 200 };

        public int SelectedRate
        {
            get => _selectedRate;
            set => Set(ref _selectedRate, value);
        }

        public WaveSettingsViewModel WaveA { get; }

        public WaveSettingsViewModel WaveB { get; }

        public ObservableCollection<ChannelViewModel> Channels { get; } = new ObservableCollection<ChannelViewModel>();

        public long SampleCount
        {
            get => _sampleCount;
            private set => Set(ref _sampleCount, value);
        }

        public long LostCount
        {
            get => _lostCount;
            private set => Set(ref _lostCount, value);
        }

        public int CrcErrors
        {
            get => _crcErrors;
            private set => Set(ref _crcErrors, value);
        }

        public int PacketsPerSecond
        {
            get => _packetsPerSecond;
            private set => Set(ref _packetsPerSecond, value);
        }

        public string McuStatus
        {
            get => _mcuStatus;
            private set => Set(ref _mcuStatus, value);
        }

        /// <summary>最新的在最上面</summary>
        public ObservableCollection<string> Log { get; } = new ObservableCollection<string>();

        public ICommand RefreshPortsCommand { get; }
        public ICommand ConnectCommand { get; }
        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand ApplyRateCommand { get; }
        public ICommand GetStatusCommand { get; }
        public ICommand ClearLogCommand { get; }

        // ------------------------------------------------------------------
        // 連線
        // ------------------------------------------------------------------
        private void RefreshPorts()
        {
            var keep = SelectedPort;
            Ports.Clear();
            foreach (var name in SerialLink.GetPortNames())
                Ports.Add(name);
            SelectedPort = Ports.Contains(keep) ? keep : (Ports.Count > 0 ? Ports[Ports.Count - 1] : null);
        }

        private Task ToggleConnectionAsync()
        {
            return IsConnected ? DisconnectAsync() : ConnectAsync();
        }

        private async Task ConnectAsync()
        {
            var link = new SerialLink();
            try
            {
                link.Open(SelectedPort);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                link.Dispose();
                Write($"open {SelectedPort} failed: {ex.Message}");
                return;
            }

            _link = link;
            _client = new DaqClient(link);
            _client.SampleReceived += OnSampleReceived;
            _client.ConnectionLost += OnConnectionLost;
            IsConnected = true;
            Write($"opened {SelectedPort}");

            // 重新連線時 MCU 可能還在採樣中，先問目前狀態(protocol.md 5.5)
            if (await GetStatusAsync())
            {
                ResetCounters();
                _timer.Start();
            }
        }

        private async Task DisconnectAsync()
        {
            if (IsRunning)
            {
                try
                {
                    await _client.StopAsync();
                }
                catch (Exception ex) when (ex is TimeoutException || ex is IOException)
                {
                    // 要斷線了，STOP 沒送到也沒關係
                }
            }
            TearDown("disconnected");
        }

        private void TearDown(string reason)
        {
            if (_link == null)
                return;

            _timer.Stop();
            _client.SampleReceived -= OnSampleReceived;
            _client.ConnectionLost -= OnConnectionLost;
            _client.Dispose();
            _link.Dispose();
            _client = null;
            _link = null;

            while (_queue.TryDequeue(out _)) { }
            IsRunning = false;
            IsConnected = false;
            PacketsPerSecond = 0;
            Write(reason);
        }

        private void OnConnectionLost(object sender, Exception ex)
        {
            // 讀取執行緒 → 切回 UI 執行緒
            _ui.BeginInvoke(new Action(() => TearDown($"connection lost: {ex.Message}")));
        }

        // ------------------------------------------------------------------
        // 命令
        // ------------------------------------------------------------------
        private Task StartAsync()
        {
            return RunAsync("START", () => _client.StartAsync(), () =>
            {
                ResetCounters();
                IsRunning = true;
            });
        }

        private Task StopAsync()
        {
            return RunAsync("STOP", () => _client.StopAsync(), () => IsRunning = false);
        }

        private Task ApplyRateAsync()
        {
            var rate = SelectedRate;
            return RunAsync($"SET_RATE {rate} Hz", () => _client.SetRateAsync((ushort)rate), null);
        }

        private Task ApplyWaveAsync(WaveSettingsViewModel wave)
        {
            if (!wave.TryGetConfig(out var config, out var error))
            {
                Write($"SET_WAVE: {error}");
                return Task.CompletedTask;
            }
            return RunAsync($"SET_WAVE {config}", () => _client.SetWaveAsync(config), null);
        }

        private async Task<bool> GetStatusAsync()
        {
            try
            {
                var st = await _client.GetStatusAsync();
                IsRunning = st.Running;
                SelectedRate = st.RateHz;
                McuStatus = $"rate {st.RateHz} Hz · rx_crc_err {st.RxCrcErr} · tx_drop {st.TxDrop}";
                Write($"STATUS {st}");
                return true;
            }
            catch (Exception ex) when (ex is TimeoutException || ex is IOException || ex is InvalidOperationException)
            {
                TearDown($"GET_STATUS: no response ({ex.Message})");
                return false;
            }
        }

        /// <summary>送出命令並記錄結果;逾時或傳輸錯誤視為斷線</summary>
        private async Task RunAsync(string name, Func<Task<CommandResult>> send, Action onAck)
        {
            try
            {
                var result = await send();
                Write($"{name} → {result}");
                if (result.IsAck)
                    onAck?.Invoke();
            }
            catch (Exception ex) when (ex is TimeoutException || ex is IOException || ex is InvalidOperationException)
            {
                TearDown($"{name}: no response ({ex.Message})");
            }
        }

        // ------------------------------------------------------------------
        // 資料
        // ------------------------------------------------------------------
        private void OnSampleReceived(object sender, DataSample sample)
        {
            _queue.Enqueue(sample);         // 讀取執行緒：只排隊，不碰 UI
        }

        private void OnUiTick()
        {
            if (_client == null)
                return;

            var now = _clock.Elapsed;
            DataSample last = null;
            int n = 0;
            while (_queue.TryDequeue(out var s))
            {
                last = s;
                n++;
            }

            if (last != null)
            {
                Channels[0].Update(last.DacA, last.Adc0);
                Channels[1].Update(last.DacB, last.Adc1);
                Channels[2].Update(0, last.Adc2);
                Channels[3].Update(4095, last.Adc3);
                _lastSampleAt = now;
                _noDataWarned = false;
            }

            _rateWindowCount += n;
            if (now - _rateWindowStart >= TimeSpan.FromSeconds(1))
            {
                PacketsPerSecond = (int)Math.Round(_rateWindowCount / (now - _rateWindowStart).TotalSeconds);
                _rateWindowStart = now;
                _rateWindowCount = 0;
            }

            SampleCount = _client.SampleCount;
            LostCount = _client.LostCount;
            CrcErrors = _client.CrcErrors;

            // 採樣中卻超過 1 秒沒有資料：線還在但 MCU 可能重開機或卡住
            if (IsRunning && !_noDataWarned && now - _lastSampleAt > NoDataTimeout)
            {
                _noDataWarned = true;
                Write($"warning: no DATA for {NoDataTimeout.TotalSeconds:0} s");
            }
        }

        private void ResetCounters()
        {
            var now = _clock.Elapsed;
            _lastSampleAt = now;
            _noDataWarned = false;
            _rateWindowStart = now;
            _rateWindowCount = 0;
            foreach (var ch in Channels)
                ch.Clear();
        }

        // ------------------------------------------------------------------
        private void StateChanged()
        {
            OnPropertyChanged(nameof(CanSelectPort));
            OnPropertyChanged(nameof(ConnectButtonText));
            OnPropertyChanged(nameof(ConnectionText));
            OnPropertyChanged(nameof(CanChangeRate));
            CommandManager.InvalidateRequerySuggested();
        }

        private void Write(string message)
        {
            Log.Insert(0, $"{DateTime.Now:HH:mm:ss.fff}  {message}");
            while (Log.Count > MaxLogLines)
                Log.RemoveAt(Log.Count - 1);
        }

        public void Dispose()
        {
            TearDown("closed");
        }
    }
}
