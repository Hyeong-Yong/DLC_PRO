using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLC_PRO.Core.Wlm;
using DLC_PRO.Data;
using DLC_PRO.Interfaces;
using DLC_PRO.Models;
using DLC_PRO.Models.Plot;
using DLC_PRO.Services;
using DLC_PRO.ViewModels.Dialogs;
using DLC_PRO.ViewModels.Wavemeter;

namespace DLC_PRO.ViewModels.Pages {
    /// <summary>
    /// 파장계 페이지 — HighFinesse "Wavelength Meter WS/6 VisIR" 주 화면의 조작부를 옮긴 것.
    /// Result unit / Range / Pulse / Precision / Autocalibration / Exposure / Interval / Average / Start.
    /// Start를 누르면 측정을 시작하고 "WLM LongTerm graph" 창(별도 창)을 연다.
    /// DLC pro 연결과 관계없이 사용할 수 있다.
    /// </summary>
    public partial class WavemeterPageViewModel : PageViewModel {
        private readonly WavemeterService _wlm;
        private readonly LongTermViewModel _longTerm;
        private readonly IWindowService _windows;
        private readonly DialogService _dialogs;
        private readonly LogService _log;
        private readonly PlotSeries _pattern;
        private bool _sync;
        private int _rateCount;
        private DateTime _rateStart = DateTime.Now;

        public WavemeterPageViewModel(DeviceService dev, WavemeterService wlm, LongTermViewModel longTerm, IWindowService windows,
                                      DialogService dialogs, LogService log) : base(ApplicationPageNames.Wavemeter, dev) {
            _wlm = wlm;
            _longTerm = longTerm;
            _windows = windows;
            _dialogs = dialogs;
            _log = log;
            RangeNames = wlm.Settings.RangeNameList;
            _openLongTermOnStart = wlm.Settings.OpenLongTermOnStart;
            _autoConnect = wlm.Settings.AutoConnect;
            _showSignal = wlm.Settings.ShowSignal;

            PatternPlot = new PlotModel { Title = "", XLabel = "Pixel", YLabel = "Intensity", MinYSpan = 100 };
            _pattern = PatternPlot.AddSeries("", PlotColors.Trace1, false, 1.0f);

            wlm.PropertyChanged += OnServicePropertyChanged;
            wlm.SamplesReceived += s => _rateCount += s.Count;
            wlm.PatternReceived += OnPattern;
            if (_showSignal) _ = wlm.SetPatternExportAsync(true);
        }

        public override string Title => "Wavemeter";
        public override string Subtitle => "HighFinesse WS/6 파장계 (wlmData.dll) · Start를 누르면 WLM LongTerm graph 창이 별도 창으로 열립니다.";

        public WavemeterService Wlm => _wlm;
        public LongTermViewModel LongTerm => _longTerm;
        public PlotModel PatternPlot { get; }

        public IReadOnlyList<string> RangeNames { get; }
        public IReadOnlyList<string> AutoCalUnits { get; } = new[] { "측정 시작 시 1회", "Measurements", "Days", "Hours", "Minutes" };
        public IReadOnlyList<string> AverageModes { get; } = new[] { "Floating", "Succeeding" };

        /// <summary>앱 시작 시 자동 연결 (MainViewModel이 호출).</summary>
        public async Task AutoConnectIfEnabledAsync() {
            if (!_wlm.Settings.AutoConnect || _wlm.IsConnected) return;
            string? err = await _wlm.ConnectAsync();
            if (err != null) _log.SetStatus("[WLM] 자동 연결 실패: " + err.Split('\n')[0]);
        }

        // ------------------------------------------------------------------
        // 연결 / 측정
        // ------------------------------------------------------------------

        public bool IsConnected => _wlm.IsConnected;
        public bool IsMeasuring => _wlm.IsMeasuring;
        public bool CanEdit => _wlm.IsConnected && _wlm.Status.ServerRunning && !_wlm.IsBusy;
        public string ConnectButtonText => _wlm.IsBusy ? "처리 중…" : _wlm.IsConnected ? "연결 해제" : "연결";
        public string StartButtonText => IsMeasuring ? "Stop" : "Start";
        public string StartIcon => IsMeasuring ? "" : "";

        private void OnServicePropertyChanged(object? sender, PropertyChangedEventArgs e) {
            switch (e.PropertyName) {
                case nameof(WavemeterService.Status):
                    ApplyStatus(_wlm.Status);
                    goto case nameof(WavemeterService.IsConnected);
                case nameof(WavemeterService.IsConnected):
                case nameof(WavemeterService.IsBusy):
                    OnPropertyChanged(nameof(IsConnected));
                    OnPropertyChanged(nameof(IsMeasuring));
                    OnPropertyChanged(nameof(CanEdit));
                    OnPropertyChanged(nameof(ConnectButtonText));
                    OnPropertyChanged(nameof(StartButtonText));
                    OnPropertyChanged(nameof(StartIcon));
                    ToggleConnectCommand.NotifyCanExecuteChanged();
                    break;
            }
        }

        private bool CanToggleConnect() => !_wlm.IsBusy;

        [RelayCommand(CanExecute = nameof(CanToggleConnect))]
        private async Task ToggleConnectAsync() {
            if (_wlm.IsConnected) {
                await _wlm.DisconnectAsync();
                return;
            }
            string? err = await _wlm.ConnectAsync();
            if (err != null) await _dialogs.AlertAsync("파장계 연결", err, DialogKind.Warning);
        }

        [RelayCommand]
        private async Task StartServerAsync() {
            if (_wlm.IsConnected || _wlm.IsBusy) return;
            string? err = await _wlm.StartServerAndConnectAsync();
            if (err != null) await _dialogs.AlertAsync("WLM 프로그램 실행", err, DialogKind.Warning);
        }

        /// <summary>Start/Stop (WLM 화면의 Start 버튼). 연결 전이면 먼저 연결한다.</summary>
        [RelayCommand]
        private async Task StartStopAsync() {
            if (!_wlm.IsConnected) {
                string? err = await _wlm.ConnectAsync();
                if (err != null) {
                    await _dialogs.AlertAsync("파장계 연결", err, DialogKind.Warning);
                    return;
                }
            }
            if (_wlm.IsMeasuring) {
                await _wlm.StopMeasurementAsync();
                return;
            }
            if (await _wlm.StartMeasurementAsync() && OpenLongTermOnStart) {
                _longTerm.IsRecording = true;
                _windows.ShowLongTerm(_longTerm);
            }
        }

        [RelayCommand]
        private void OpenLongTerm() => _windows.ShowLongTerm(_longTerm);

        [ObservableProperty] private bool _openLongTermOnStart;
        [ObservableProperty] private bool _autoConnect;
        [ObservableProperty] private bool _showSignal;

        partial void OnOpenLongTermOnStartChanged(bool value) {
            _wlm.Settings.OpenLongTermOnStart = value;
            _wlm.SaveSettings();
        }

        partial void OnAutoConnectChanged(bool value) {
            _wlm.Settings.AutoConnect = value;
            _wlm.SaveSettings();
        }

        partial void OnShowSignalChanged(bool value) {
            _wlm.Settings.ShowSignal = value;
            _wlm.SaveSettings();
            _ = _wlm.SetPatternExportAsync(value);
            if (!value) {
                _pattern.XD = _pattern.YD = null;
                PatternPlot.Invalidate();
            }
        }

        // ------------------------------------------------------------------
        // WLM 설정 (값 변경 → 장비 쓰기, 장비 상태 → 화면은 _sync 중에만)
        // ------------------------------------------------------------------

        [ObservableProperty] private int _resultMode;
        [ObservableProperty] private int _rangeIndex = -1;
        [ObservableProperty] private bool _rangeAvailable;
        [ObservableProperty] private int _pulseMode;
        [ObservableProperty] private int _wideMode;
        [ObservableProperty] private bool _fastMode;
        [ObservableProperty] private bool _autoCal;
        [ObservableProperty] private double? _autoCalPeriod;
        [ObservableProperty] private int _autoCalUnit;
        [ObservableProperty] private double? _exposure;
        [ObservableProperty] private bool _exposureAuto;
        [ObservableProperty] private decimal _exposureMin = 1;
        [ObservableProperty] private decimal _exposureMax = 9999;
        [ObservableProperty] private bool _intervalMode;
        [ObservableProperty] private double? _interval;
        [ObservableProperty] private double? _averageCount;
        [ObservableProperty] private int _averageModeIndex;
        [ObservableProperty] private bool _averagePattern;

        private void ApplyStatus(WlmStatus s) {
            _sync = true;
            try {
                if (!s.ServerRunning) return;
                ResultMode = s.ResultMode;
                RangeAvailable = s.Range >= 0 && s.Range < RangeNames.Count;
                RangeIndex = RangeAvailable ? s.Range : -1;
                PulseMode = s.PulseMode;
                WideMode = s.WideMode;
                FastMode = s.FastMode;
                AutoCal = s.AutoCal;
                AutoCalPeriod = s.AutoCalPeriod;
                AutoCalUnit = s.AutoCalUnit >= 0 && s.AutoCalUnit <= 4 ? s.AutoCalUnit : 0;
                if (s.ExposureMax > s.ExposureMin) {
                    ExposureMin = s.ExposureMin;
                    ExposureMax = s.ExposureMax;
                }
                Exposure = s.Exposure >= 0 ? s.Exposure : null;
                ExposureAuto = s.ExposureAuto;
                IntervalMode = s.IntervalMode;
                Interval = s.Interval >= 0 ? s.Interval : null;
                AverageCount = s.AverageCount > 0 ? s.AverageCount : null;
                AverageModeIndex = s.AverageMode == WlmConst.cAvrgSucceeding ? 1 : 0;
                AveragePattern = s.AverageType == WlmConst.cAvrgPattern;
            }
            finally { _sync = false; }
        }

        private void Apply(string what, Func<IWlmBackend, int> set) {
            if (_sync) return;
            _ = _wlm.ApplyAsync(what, set);
        }

        partial void OnResultModeChanged(int value) => Apply("Result unit", b => b.SetResultMode(value));
        partial void OnRangeIndexChanged(int value) { if (value >= 0) Apply("Range", b => b.SetRange(value)); }
        partial void OnPulseModeChanged(int value) => Apply("Pulse", b => b.SetPulseMode(value));
        partial void OnWideModeChanged(int value) => Apply("Precision", b => b.SetWideMode(value));
        partial void OnFastModeChanged(bool value) => Apply("Fast", b => b.SetFastMode(value));
        partial void OnAutoCalChanged(bool value) => Apply("Autocalibration", b => b.SetAutoCalMode(value ? 1 : 0));
        partial void OnAutoCalPeriodChanged(double? value) {
            if (value.HasValue) Apply("AutoCal 주기", b => b.SetAutoCalSetting(WlmConst.cmiAutoCalPeriod, (int)Math.Round(value.Value)));
        }
        partial void OnAutoCalUnitChanged(int value) => Apply("AutoCal 단위", b => b.SetAutoCalSetting(WlmConst.cmiAutoCalUnit, value));
        partial void OnExposureChanged(double? value) {
            if (value.HasValue) Apply("Exposure", b => b.SetExposureNum(1, 1, (int)Math.Round(value.Value)));
        }
        partial void OnExposureAutoChanged(bool value) => Apply("Exposure Automatic", b => b.SetExposureModeNum(1, value));
        partial void OnIntervalModeChanged(bool value) => Apply("Interval 사용", b => b.SetIntervalMode(value));
        partial void OnIntervalChanged(double? value) {
            if (value.HasValue) Apply("Interval", b => b.SetInterval((int)Math.Round(value.Value)));
        }
        partial void OnAverageCountChanged(double? value) {
            if (value.HasValue) Apply("Average Cnt", b => b.SetAveragingSettingNum(1, WlmConst.cmiAveragingCount, (int)Math.Round(value.Value)));
        }
        partial void OnAverageModeIndexChanged(int value) =>
            Apply("Average 방식", b => b.SetAveragingSettingNum(1, WlmConst.cmiAveragingMode, value == 1 ? WlmConst.cAvrgSucceeding : WlmConst.cAvrgFloating));
        partial void OnAveragePatternChanged(bool value) =>
            Apply("Average Pattern", b => b.SetAveragingSettingNum(1, WlmConst.cmiAveragingType, value ? WlmConst.cAvrgPattern : WlmConst.cAvrgSimple));

        // ------------------------------------------------------------------
        // 측정 결과 표시 (100 ms)
        // ------------------------------------------------------------------

        [ObservableProperty] private string _resultName = "Wavelength, vac.";
        [ObservableProperty] private string _resultText = "—";
        [ObservableProperty] private string _resultUnit = "nm";
        [ObservableProperty] private string _resultError = "";
        [ObservableProperty] private string _vacText = "—";
        [ObservableProperty] private string _airText = "—";
        [ObservableProperty] private string _freqText = "—";
        [ObservableProperty] private string _waveNumberText = "—";
        [ObservableProperty] private string _energyText = "—";
        [ObservableProperty] private string _temperatureText = "T = —";
        [ObservableProperty] private string _pressureText = "p = —";
        [ObservableProperty] private string _linkText = "Link —";
        [ObservableProperty] private string _operationText = "연결 안 됨";
        [ObservableProperty] private string _rateText = "";
        [ObservableProperty] private LedState _measureLed;

        protected override void OnTick() {
            WlmStatus s = _wlm.Status;
            double vac = _wlm.LastVacuumNm;
            bool on = _wlm.IsConnected && s.ServerRunning;
            int unit = s.ResultMode >= 0 && s.ResultMode <= 4 ? s.ResultMode : 0;
            ResultName = WlmUnits.Name(unit);
            ResultUnit = WlmUnits.Unit(unit);
            bool valid = on && WlmUnits.IsValid(vac);
            ResultText = valid ? WlmUnits.Format(WlmUnits.FromVacuum(vac, unit, s.AirRatio), unit) : "—";
            ResultError = on && !double.IsNaN(vac) && !valid ? WlmUnits.ErrorText(vac) : "";
            VacText = valid ? WlmUnits.Format(vac, 0) : "—";
            AirText = valid && s.AirRatio > 1 ? WlmUnits.Format(vac / s.AirRatio, 1) : "—";
            FreqText = valid ? WlmUnits.Format(WlmUnits.FromVacuum(vac, 2, 0), 2) : "—";
            WaveNumberText = valid ? WlmUnits.Format(WlmUnits.FromVacuum(vac, 3, 0), 3) : "—";
            EnergyText = valid ? WlmUnits.Format(WlmUnits.FromVacuum(vac, 4, 0), 4) : "—";
            CultureInfo inv = CultureInfo.InvariantCulture;
            TemperatureText = on && s.Temperature > -273 ? "T = " + s.Temperature.ToString("F1", inv) + " °C" : "T = —";
            PressureText = on && s.Pressure > 0 ? "p = " + s.Pressure.ToString("F0", inv) + " mbar" : "p = —";
            LinkText = on ? (s.Link ? "Link On" : "Link Off") : "Link —";
            OperationText = !_wlm.IsConnected ? "연결 안 됨" : !s.ServerRunning ? "WLM 서버 없음"
                : s.OperationState == WlmConst.cMeasurement ? "measuring" : s.OperationState == WlmConst.cAdjustment ? "adjusting" : "paused";
            MeasureLed = !on ? LedState.Off : s.OperationState == WlmConst.cMeasurement ? (valid ? LedState.On : LedState.Warn) : LedState.Info;

            double el = (DateTime.Now - _rateStart).TotalSeconds;
            if (el >= 1) {
                RateText = on && s.OperationState == WlmConst.cMeasurement ? (_rateCount / el).ToString("F1", inv) + " 측정/s" : "";
                _rateCount = 0;
                _rateStart = DateTime.Now;
            }
        }

        private void OnPattern(double[] data, int count) {
            if (!ShowSignal) return;
            double[] x = new double[count];
            for (int i = 0; i < count; i++) x[i] = i;
            _pattern.XD = x;
            _pattern.YD = data;
            if (IsActive) PatternPlot.Invalidate();
        }
    }
}
