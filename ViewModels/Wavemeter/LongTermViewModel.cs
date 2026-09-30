using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLC_PRO.Core.Wlm;
using DLC_PRO.Models;
using DLC_PRO.Models.Plot;
using DLC_PRO.Services;

namespace DLC_PRO.ViewModels.Wavemeter {
    /// <summary>
    /// "WLM LongTerm graph" 창 ViewModel (HighFinesse LongTerm 프로그램 기능을 따름, 매뉴얼 3.4).
    /// - 가로축: 측정 번호 또는 시간, 페이지당 N개(초), Fixed = 오른쪽 끝에 닿으면 반 페이지씩 이동
    /// - 통계: 최근 N개(Floating) 또는 리셋 이후 전체 — 평균/표준편차/최소/최대
    /// - 세로축 고정(Min/Max), 단위(WLM 표시 단위 또는 선택), Δf [MHz] 표시
    /// - 기록 시작/정지/지우기, TSV(.lta 형식과 비슷한 탭 구분) 저장 — 오류 값도 그대로 저장
    /// 창을 닫아도 기록은 계속된다 (다시 열면 이어서 표시).
    /// </summary>
    public partial class LongTermViewModel : ViewModelBase {
        public const int MaxPoints = 1_000_000;
        private readonly WavemeterService _wlm;
        private readonly LogService _log;
        private readonly WavemeterSettings _s;
        private readonly List<WlmSample> _data = new List<WlmSample>();
        private readonly DispatcherTimer _timer;
        private readonly PlotSeries _series;
        private long _dropped;              // 용량 초과로 버린 앞쪽 점 수 (측정 번호 유지용)
        private bool _dirty;
        private int _statStart;             // 리셋 이후 통계 시작 (데이터 인덱스)
        private double _refFreq = double.NaN;
        private double _t0 = double.NaN;
        private bool _loading;

        public LongTermViewModel(WavemeterService wlm, LogService log) {
            _wlm = wlm;
            _log = log;
            _s = wlm.Settings;
            _loading = true;
            _unitIndex = _s.LtUnit + 1;
            _deltaMode = _s.LtDelta;
            _timeAxis = _s.LtTimeAxis;
            _perPage = _s.LtPerPage;
            _fixedX = _s.LtFixedX;
            _statCount = _s.LtStatCount;
            _statModeIndex = _s.LtStatSinceReset ? 1 : 0;
            _fixedY = _s.LtFixedY;
            _yMin = _s.LtYMin;
            _yMax = _s.LtYMax;
            _topmost = _s.LtTopmost;
            _loading = false;

            Plot = new PlotModel { XLabel = "Number", MinYSpan = 0 };
            _series = Plot.AddSeries("", PlotColors.Trace1, false, 1.2f);
            UpdateTitle();

            wlm.SamplesReceived += OnSamples;
            wlm.PropertyChanged += (_, e) => {
                if (e.PropertyName == nameof(WavemeterService.Status)) {
                    if (UnitIndex == 0) { UpdateTitle(); _dirty = true; }
                    OnPropertyChanged(nameof(DeviceText));
                }
            };
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) => Refresh());
            _timer.Start();
        }

        public PlotModel Plot { get; }

        /// <summary>창이 열려 있는지 (WindowService가 설정). 닫혀 있으면 그래프를 다시 그리지 않는다.</summary>
        [ObservableProperty] private bool _isWindowOpen;

        /// <summary>창에서 저장 파일을 고르는 함수 (WindowService가 창의 StorageProvider로 설정).</summary>
        public Func<string, Task<string?>>? SaveFilePicker { get; set; }

        public string DeviceText {
            get {
                WlmStatus st = _wlm.Status;
                return st.ServerRunning ? "WS/" + st.WlmType + "-" + st.WlmVersion : "WLM";
            }
        }

        // ------------------------------------------------------------------
        // 설정 (창 상단 막대)
        // ------------------------------------------------------------------

        public IReadOnlyList<string> UnitChoices { get; } = new[] {
            "WLM 표시 단위 따름", "Wavelength, vac. [nm]", "Wavelength, air [nm]", "Frequency [THz]", "Wavenumber [1/cm]", "Photon energy [eV]",
        };

        public IReadOnlyList<string> StatModes { get; } = new[] { "Floating (최근 N개)", "리셋 이후 전체" };

        [ObservableProperty] private int _unitIndex;
        [ObservableProperty] private bool _deltaMode;
        [ObservableProperty] private bool _timeAxis;
        [ObservableProperty] private double? _perPage;
        [ObservableProperty] private bool _fixedX;
        [ObservableProperty] private double? _statCount;
        [ObservableProperty] private int _statModeIndex;
        [ObservableProperty] private bool _fixedY;
        [ObservableProperty] private double? _yMin;
        [ObservableProperty] private double? _yMax;
        [ObservableProperty] private bool _topmost;
        [ObservableProperty] private bool _isRecording = true;

        public string PerPageUnit => TimeAxis ? "s / page" : "per page";
        public string RecordButtonText => IsRecording ? "기록 정지" : "기록 시작";
        public string RecordIcon => IsRecording ? "" : "";   // pause / play
        public string YFormat => "F" + (DeltaMode ? 3 : WlmUnits.Decimals(Unit)).ToString(CultureInfo.InvariantCulture);
        public double YIncrement => DeltaMode ? 1 : Math.Pow(10, -Math.Min(WlmUnits.Decimals(Unit), 6));

        /// <summary>실제 표시 단위 (0~4).</summary>
        public int Unit {
            get {
                int u = UnitIndex - 1;
                if (u < 0) u = _wlm.Status.ServerRunning ? _wlm.Status.ResultMode : 0;
                return u < 0 || u > 4 ? 0 : u;
            }
        }

        public string YTitle => DeltaMode ? "Δf [MHz]" : WlmUnits.Title(Unit);

        partial void OnUnitIndexChanged(int value) => SettingChanged();
        partial void OnDeltaModeChanged(bool value) { SettingChanged(); OnPropertyChanged(nameof(YFormat)); OnPropertyChanged(nameof(YIncrement)); }
        partial void OnTimeAxisChanged(bool value) { SettingChanged(); OnPropertyChanged(nameof(PerPageUnit)); }
        partial void OnPerPageChanged(double? value) => SettingChanged();
        partial void OnFixedXChanged(bool value) => SettingChanged();
        partial void OnStatCountChanged(double? value) => SettingChanged();
        partial void OnStatModeIndexChanged(int value) => SettingChanged();
        partial void OnYMinChanged(double? value) => SettingChanged();
        partial void OnYMaxChanged(double? value) => SettingChanged();
        partial void OnTopmostChanged(bool value) => SettingChanged();
        partial void OnIsRecordingChanged(bool value) {
            OnPropertyChanged(nameof(RecordButtonText));
            OnPropertyChanged(nameof(RecordIcon));
        }

        partial void OnFixedYChanged(bool value) {
            // 처음 고정할 때 범위가 비어 있으면 현재 보이는 범위로 채운다
            if (value && !_loading && (!(YMin.HasValue && YMax.HasValue) || YMax <= YMin) && _visibleMin < _visibleMax) {
                double pad = (_visibleMax - _visibleMin) * 0.1;
                YMin = _visibleMin - pad;
                YMax = _visibleMax + pad;
            }
            SettingChanged();
        }

        private void SettingChanged() {
            if (_loading) return;
            _s.LtUnit = UnitIndex - 1;
            _s.LtDelta = DeltaMode;
            _s.LtTimeAxis = TimeAxis;
            _s.LtPerPage = (int)Math.Round(PerPage ?? 10000);
            _s.LtFixedX = FixedX;
            _s.LtStatCount = (int)Math.Round(StatCount ?? 10);
            _s.LtStatSinceReset = StatModeIndex == 1;
            _s.LtFixedY = FixedY;
            _s.LtYMin = YMin ?? 0;
            _s.LtYMax = YMax ?? 0;
            _s.LtTopmost = Topmost;
            _s.Sanitize();
            _wlm.SaveSettings();
            UpdateTitle();
            OnPropertyChanged(nameof(YTitle));
            OnPropertyChanged(nameof(YFormat));
            OnPropertyChanged(nameof(YIncrement));
            _dirty = true;
            Refresh();
        }

        private void UpdateTitle() {
            Plot.Title = DeviceText + ": Signal 1 " + (DeltaMode ? "Δf (기준 " + RefText() + ")" : WlmUnits.Name(Unit));
            Plot.YLabel = YTitle;
            Plot.XLabel = TimeAxis ? "시간 [s]" : "Number";
        }

        private string RefText() => WlmUnits.IsValid(_refFreq) ? _refFreq.ToString("F6", CultureInfo.InvariantCulture) + " THz" : "첫 측정값";

        // ------------------------------------------------------------------
        // 통계 표시
        // ------------------------------------------------------------------

        [ObservableProperty] private string _currentText = "—";
        [ObservableProperty] private string _currentError = "";
        [ObservableProperty] private string _meanText = "—";
        [ObservableProperty] private string _stdText = "—";
        [ObservableProperty] private string _minText = "—";
        [ObservableProperty] private string _maxText = "—";
        [ObservableProperty] private string _pvText = "—";
        [ObservableProperty] private string _stdMhzText = "—";
        [ObservableProperty] private string _pvMhzText = "—";
        [ObservableProperty] private string _countText = "0";
        [ObservableProperty] private string _statInfo = "";

        private double _visibleMin, _visibleMax;

        // ------------------------------------------------------------------
        // 데이터
        // ------------------------------------------------------------------

        public int Count => _data.Count;
        public long TotalCount => _dropped + _data.Count;

        private void OnSamples(IReadOnlyList<WlmSample> samples) {
            if (!IsRecording) return;
            foreach (WlmSample s in samples) {
                if (double.IsNaN(_t0)) _t0 = s.Seconds;
                if (double.IsNaN(_refFreq) && WlmUnits.IsValid(s.VacuumNm)) _refFreq = WlmUnits.SpeedOfLight / s.VacuumNm;
                _data.Add(s);
            }
            if (_data.Count > MaxPoints) {
                int drop = _data.Count - MaxPoints + MaxPoints / 10;
                _data.RemoveRange(0, drop);
                _dropped += drop;
                _statStart = Math.Max(0, _statStart - drop);
            }
            _dirty = true;
        }

        /// <summary>측정값 하나를 표시 단위 값으로 (오류 값이면 NaN).</summary>
        private double ToDisplay(double vac) {
            if (!WlmUnits.IsValid(vac)) return double.NaN;
            if (DeltaMode) return (WlmUnits.SpeedOfLight / vac - _refFreq) * 1e6;
            return WlmUnits.FromVacuum(vac, Unit, _wlm.Status.AirRatio);
        }

        private void Refresh() {
            if (!_dirty) return;
            _dirty = false;
            UpdateStatistics();
            if (IsWindowOpen) UpdatePlot();
            OnPropertyChanged(nameof(TotalCount));
        }

        private void UpdatePlot() {
            int n = _data.Count;
            double perPage = Math.Max(1, PerPage ?? 10000);
            double xMin, xMax;
            int i0, i1;   // 보이는 데이터 인덱스 [i0, i1)
            if (!TimeAxis) {
                double total = TotalCount;
                if (FixedX) {
                    double half = perPage / 2;
                    xMin = total <= perPage ? 0 : Math.Ceiling((total - perPage) / half) * half;
                    xMax = xMin + perPage;
                }
                else {
                    xMin = Math.Max(0, total - perPage);
                    xMax = Math.Max(perPage, total);
                }
                i0 = (int)Math.Clamp(xMin - _dropped, 0, n);
                i1 = n;
            }
            else {
                double tNow = n > 0 ? _data[n - 1].Seconds - _t0 : 0;
                if (FixedX) {
                    double half = perPage / 2;
                    xMin = tNow <= perPage ? 0 : Math.Ceiling((tNow - perPage) / half) * half;
                    xMax = xMin + perPage;
                }
                else {
                    xMin = Math.Max(0, tNow - perPage);
                    xMax = Math.Max(perPage, tNow);
                }
                i0 = LowerBound(xMin + _t0);
                i1 = n;
            }

            List<double> xs = new List<double>(Math.Max(0, i1 - i0));
            List<double> ys = new List<double>(Math.Max(0, i1 - i0));
            double lo = double.MaxValue, hi = double.MinValue;
            for (int i = i0; i < i1; i++) {
                double y = ToDisplay(_data[i].VacuumNm);
                if (double.IsNaN(y)) continue;   // 오류 값은 건너뛰고 앞뒤 점을 잇는다 (LongTerm 기본 설정)
                xs.Add(TimeAxis ? _data[i].Seconds - _t0 : _dropped + i + 1);
                ys.Add(y);
                if (y < lo) lo = y;
                if (y > hi) hi = y;
            }
            _visibleMin = lo;
            _visibleMax = hi;
            _series.XD = xs.ToArray();
            _series.YD = ys.ToArray();
            _series.Points = xs.Count < 3;
            Plot.XMin = xMin;
            Plot.XMax = xMax;
            if (FixedY && YMin.HasValue && YMax.HasValue && YMax > YMin) {
                Plot.YMin = YMin;
                Plot.YMax = YMax;
            }
            else {
                Plot.YMin = Plot.YMax = null;
                // 값이 거의 같을 때 축 눈금이 한 점으로 모이지 않도록 최소 폭
                Plot.MinYSpan = DeltaMode ? 1 : Math.Pow(10, -WlmUnits.Decimals(Unit)) * 10;
            }
            UpdateTitle();
            Plot.Overlay = n == 0 ? (IsRecording ? "측정값을 기다리는 중… (파장계 페이지에서 Start)" : "기록 정지됨") : "";
            Plot.Invalidate();
        }

        private int LowerBound(double seconds) {
            int lo = 0, hi = _data.Count;
            while (lo < hi) {
                int mid = (lo + hi) >> 1;
                if (_data[mid].Seconds < seconds) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        private void UpdateStatistics() {
            int n = _data.Count;
            CountText = TotalCount.ToString("N0", CultureInfo.InvariantCulture);
            if (n == 0) {
                CurrentText = MeanText = StdText = MinText = MaxText = PvText = StdMhzText = PvMhzText = "—";
                CurrentError = "";
                StatInfo = "";
                return;
            }
            WlmSample last = _data[n - 1];
            string fmt = DeltaMode ? "F3" : "F" + WlmUnits.Decimals(Unit).ToString(CultureInfo.InvariantCulture);
            string unit = DeltaMode ? "MHz" : WlmUnits.Unit(Unit);
            double cur = ToDisplay(last.VacuumNm);
            CurrentText = double.IsNaN(cur) ? "—" : cur.ToString(fmt, CultureInfo.InvariantCulture) + " " + unit;
            CurrentError = WlmUnits.ErrorText(last.VacuumNm);

            int start = StatModeIndex == 1 ? _statStart : 0;
            int want = StatModeIndex == 1 ? int.MaxValue : (int)Math.Max(2, StatCount ?? 10);
            // 뒤에서부터 유효한 값 want개 (또는 리셋 이후 전체)
            double sum = 0, min = double.MaxValue, max = double.MinValue;
            double fsum = 0, fmin = double.MaxValue, fmax = double.MinValue;
            int k = 0;
            int first = n;
            for (int i = n - 1; i >= start && k < want; i--) {
                double vac = _data[i].VacuumNm;
                if (!WlmUnits.IsValid(vac)) continue;
                double y = ToDisplay(vac);
                double f = WlmUnits.SpeedOfLight / vac;
                sum += y; fsum += f; k++;
                if (y < min) min = y;
                if (y > max) max = y;
                if (f < fmin) fmin = f;
                if (f > fmax) fmax = f;
                first = i;
            }
            if (k == 0) {
                MeanText = StdText = MinText = MaxText = PvText = StdMhzText = PvMhzText = "—";
                StatInfo = "유효한 측정값 없음";
                return;
            }
            double mean = sum / k, fmean = fsum / k;
            double ss = 0, fss = 0;
            for (int i = n - 1, c = 0; i >= first && c < k; i--) {
                double vac = _data[i].VacuumNm;
                if (!WlmUnits.IsValid(vac)) continue;
                double d = ToDisplay(vac) - mean;
                double fd = WlmUnits.SpeedOfLight / vac - fmean;
                ss += d * d; fss += fd * fd; c++;
            }
            double std = k > 1 ? Math.Sqrt(ss / (k - 1)) : 0;
            double fstd = k > 1 ? Math.Sqrt(fss / (k - 1)) : 0;
            string sfmt = DeltaMode ? "F3" : "G4";
            MeanText = mean.ToString(fmt, CultureInfo.InvariantCulture);
            MinText = min.ToString(fmt, CultureInfo.InvariantCulture);
            MaxText = max.ToString(fmt, CultureInfo.InvariantCulture);
            StdText = std.ToString(sfmt, CultureInfo.InvariantCulture);
            PvText = (max - min).ToString(sfmt, CultureInfo.InvariantCulture);
            StdMhzText = (fstd * 1e6).ToString("F3", CultureInfo.InvariantCulture);
            PvMhzText = ((fmax - fmin) * 1e6).ToString("F3", CultureInfo.InvariantCulture);
            StatInfo = (StatModeIndex == 1 ? "리셋 이후 " : "최근 ") + k.ToString("N0", CultureInfo.InvariantCulture) + "개 유효 측정값 · 단위 " + unit;
        }

        // ------------------------------------------------------------------
        // 명령
        // ------------------------------------------------------------------

        [RelayCommand]
        private void ToggleRecording() {
            IsRecording = !IsRecording;
            _log.Info("[WLM] LongTerm 기록 " + (IsRecording ? "시작" : "정지"));
            _dirty = true;
            Refresh();
        }

        [RelayCommand]
        private void Clear() {
            _data.Clear();
            _dropped = 0;
            _statStart = 0;
            _t0 = double.NaN;
            _refFreq = double.NaN;
            Plot.ResetScale();
            _dirty = true;
            Refresh();
        }

        /// <summary>통계 리셋 (Reset now): 리셋 이후 통계 시작점과 Δf 기준을 현재로.</summary>
        [RelayCommand]
        private void ResetStatistics() {
            _statStart = _data.Count;
            _dirty = true;
            Refresh();
        }

        /// <summary>Δf 기준을 현재 평균 주파수로.</summary>
        [RelayCommand]
        private void SetReferenceToMean() {
            double fsum = 0;
            int k = 0;
            int want = (int)Math.Max(2, StatCount ?? 10);
            for (int i = _data.Count - 1; i >= 0 && k < want; i--) {
                double vac = _data[i].VacuumNm;
                if (!WlmUnits.IsValid(vac)) continue;
                fsum += WlmUnits.SpeedOfLight / vac;
                k++;
            }
            if (k > 0) _refFreq = fsum / k;
            _dirty = true;
            Refresh();
        }

        [RelayCommand]
        private void AutoScale() {
            FixedY = false;
            Plot.ResetScale();
        }

        [RelayCommand]
        private async Task SaveAsync() {
            if (_data.Count == 0) {
                _log.SetStatus("[WLM] 저장할 LongTerm 데이터가 없습니다.");
                return;
            }
            Func<string, Task<string?>>? pick = SaveFilePicker;
            if (pick == null) return;
            string? path = await pick("wlm_longterm_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".lta.txt");
            if (path == null) return;
            try {
                SaveTsv(path);
                _log.Info("[WLM] LongTerm 데이터 저장: " + path + " (" + _data.Count + "점)");
            }
            catch (Exception ex) {
                _log.Error("[WLM] LongTerm 저장 실패: " + ex.Message);
                _log.SetStatus("[WLM] LongTerm 저장 실패: " + ex.Message);
            }
        }

        /// <summary>탭 구분 저장. 오류 값(예: -4 = Overexposed)도 LongTerm .lta처럼 그대로 기록한다.</summary>
        public void SaveTsv(string path) {
            CultureInfo inv = CultureInfo.InvariantCulture;
            using StreamWriter w = new StreamWriter(path, false, new UTF8Encoding(false));
            w.WriteLine("# DLC_PRO WLM LongTerm export");
            w.WriteLine("# Device\t" + DeviceText);
            w.WriteLine("# Saved\t" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", inv));
            w.WriteLine("# Values <= 0 are WLM error codes (0 no value, -1 no signal, -2 bad signal, -3 underexposed, -4 overexposed)");
            int u = Unit;
            w.WriteLine("Time\tElapsed [s]\tWLM timestamp [ms]\tNumber\tWavelength, vac. [nm]\tFrequency [THz]\t" + WlmUnits.Title(u));
            for (int i = 0; i < _data.Count; i++) {
                WlmSample s = _data[i];
                bool ok = WlmUnits.IsValid(s.VacuumNm);
                double f = ok ? WlmUnits.SpeedOfLight / s.VacuumNm : s.VacuumNm;
                double v = ok ? WlmUnits.FromVacuum(s.VacuumNm, u, _wlm.Status.AirRatio) : s.VacuumNm;
                w.Write(s.Time.ToString("yyyy-MM-dd HH:mm:ss.fff", inv)); w.Write('\t');
                w.Write((s.Seconds - _t0).ToString("F3", inv)); w.Write('\t');
                w.Write(s.WlmTimestampMs.ToString(inv)); w.Write('\t');
                w.Write((_dropped + i + 1).ToString(inv)); w.Write('\t');
                w.Write(s.VacuumNm.ToString("R", inv)); w.Write('\t');
                w.Write(f.ToString("R", inv)); w.Write('\t');
                w.WriteLine(v.ToString("R", inv));
            }
        }

        /// <summary>창 위치/크기 저장 (WindowService가 창을 닫을 때 호출).</summary>
        public void SaveWindowBounds(double x, double y, double width, double height) {
            _s.LtX = x;
            _s.LtY = y;
            _s.LtWidth = width;
            _s.LtHeight = height;
            _s.Sanitize();
            _wlm.SaveSettings();
        }

        public (double X, double Y, double Width, double Height) WindowBounds => (_s.LtX, _s.LtY, _s.LtWidth, _s.LtHeight);

        /// <summary>테스트용: 즉시 다시 그리기.</summary>
        public void ForceRefresh() {
            _dirty = true;
            Refresh();
        }
    }
}
