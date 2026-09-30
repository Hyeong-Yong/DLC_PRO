using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLC_PRO.Core;
using DLC_PRO.Core.Calibration;
using DLC_PRO.Data;
using DLC_PRO.Models;
using DLC_PRO.Models.Plot;
using DLC_PRO.Services;
using DLC_PRO.ViewModels.Dialogs;
using DLC_PRO.ViewModels.Params;

namespace DLC_PRO.ViewModels.Pages {
    /// <summary>
    /// Scan &amp; Lock 페이지 — 스코프 트레이스 + click-and-lock (TOPAS의 Scan&amp;Lock 화면).
    /// 사용법: 스캔 ON → 스펙트럼에서 원하는 피크/엣지를 클릭 → 락 포인트 선택 → [LOCK]
    /// (WinForms ScanLockTab 로직 이식: 그래프 데이터는 PlotModel, 클릭은 SelectLockpointCommand)
    /// </summary>
    public partial class ScanLockPageViewModel : PageViewModel {
        private readonly DialogService _dialogs;
        private readonly LogService _log;
        private readonly PlotSeries _bg, _ch1, _ch2;
        private volatile ScopeFrame? _latest;
        private ScopeFrame? _shown;
        private bool _updatingMhz;
        private bool _clickBusy;
        /// <summary>현재 그래프에 적용 중인 V→MHz 변환 (null이면 원래 단위).</summary>
        private FrequencyTransform? _xf;

        public ScanLockPageViewModel(DeviceService dev, DialogService dialogs, LogService log) : base(ApplicationPageNames.ScanLock, dev) {
            _dialogs = dialogs;
            _log = log;
            DlcDevice d = dev.Device;

            // ---------------- 그래프 ----------------
            Plot = new PlotModel();
            _bg = Plot.AddSeries("", PlotColors.Background, false, 1f, PlotDash.Dot);
            _ch1 = Plot.AddSeries("CH1", PlotColors.Trace1, false, 1.4f);
            _ch2 = Plot.AddSeries("CH2", PlotColors.Trace2, true, 1.2f);

            // ---------------- 툴바 ----------------
            ScopeVariant = Track(ChoiceParamViewModel.Enum(dev, P.ScopeVariant, "모드", "0=XY (스펙트럼)", "1=Scope (시간)", "2=Spectrum (FFT)"));
            ScopeCh1 = Track(ChoiceParamViewModel.Channels(dev, P.ScopeCh1Signal, "CH1", SignalChannels.Display));
            ScopeCh2 = Track(ChoiceParamViewModel.Channels(dev, P.ScopeCh2Signal, "CH2", SignalChannels.Display));
            ScopeX = Track(ChoiceParamViewModel.Channels(dev, P.ScopeXSignal, "X", SignalChannels.Display));
            ScopeRate = Track(new NumberParamViewModel(dev, P.ScopeUpdateRate, "갱신", "Hz", 0, 1));
            _showMhz = dev.Settings.Freq.ShowFrequency;

            // ---------------- Lock ----------------
            LockType = Track(ChoiceParamViewModel.Enum(dev, P.LockType, "Lock Type", "1=Top of Fringe", "2=Side of Fringe", "3=Top of Fringe PDH"));
            LockInput = Track(ChoiceParamViewModel.Channels(dev, P.LockSpectrumInput, "Input Channel", SignalChannels.Inputs));
            PidSelection = Track(ChoiceParamViewModel.Enum(dev, P.LockPidSelection, "PID Selection", "0=없음", "1=PID1", "2=PID2", "3=PID1 + PID2"));
            WithoutLockpoint = Track(new CheckParamViewModel(dev, P.LockWithoutLockpoint, "Lock without lockpoint (스캔 중앙에서 락)"));
            LockingDelay = Track(new NumberParamViewModel(dev, P.LockLockingDelay, "Locking Delay", "ms", 0, 10));
            Setpoint = Track(new NumberParamViewModel(dev, P.LockSetpoint, "Setpoint", "V", 4, 0.001));
            Lockpoint = Track(new ReadoutParamViewModel(dev, P.LockLockpointPosition, "Lockpoint (x / y)", null, raw => {
                List<string> t = DecofValue.Tuple(raw);
                if (t.Count >= 2 && DecofValue.TryDouble(t[0], out double x) && DecofValue.TryDouble(t[1], out double y))
                    return x.ToString("F3", CultureInfo.InvariantCulture) + " / " + y.ToString("F3", CultureInfo.InvariantCulture);
                return raw;
            }, 300));
            LockpointMhz = Track(new ComputedRowViewModel(dev, "Lockpoint (MHz)", () => {
                FrequencyTransform? xf = _xf;
                double v = LockpointVolt();
                if (!xf.HasValue || double.IsNaN(v)) return "-";
                return xf.Value.ToMHz(v).ToString("F2", CultureInfo.InvariantCulture);
            }, "MHz"));
            LockpointType = Track(new ReadoutParamViewModel(dev, P.LockLockpointType, "Lockpoint Type", null, null, 300));
            LockStateRow = Track(new ReadoutParamViewModel(dev, P.LockStateTxt, "State", null, null, 200));
            LockHold = Track(new ToggleParamViewModel(dev, P.LockHold, "Hold (PID 일시정지)"));

            // ---------------- Scan ----------------
            ScanEnable = Track(new ToggleParamViewModel(dev, P.ScanEnabled));
            ScanHold = Track(new CheckParamViewModel(dev, P.ScanHold, "Hold (스캔 정지 유지)"));
            ScanSignalType = Track(ChoiceParamViewModel.Enum(dev, P.ScanSignalType, "Signal Type", "0=Sine", "1=Triangle", "2=Triangle rounded"));
            ScanOutput = Track(ChoiceParamViewModel.Channels(dev, P.ScanOutputChannel, "Output Channel", SignalChannels.Outputs));
            ScanFrequency = Track(new NumberParamViewModel(dev, P.ScanFrequency, "Frequency", "Hz", 2, 1));
            ScanAmplitude = Track(new NumberParamViewModel(dev, P.ScanAmplitude, "Amplitude (p-p)", "V", 3, 0.5) { UnitParam = P.ScanUnit });
            ScanOffset = Track(new NumberParamViewModel(dev, P.ScanOffset, "Offset", "V", 3, 0.1) { UnitParam = P.ScanUnit });
            ScanStart = Track(new NumberParamViewModel(dev, P.ScanStart, "Start", "V", 3, 0.1) { UnitParam = P.ScanUnit });
            ScanEnd = Track(new NumberParamViewModel(dev, P.ScanEnd, "End", "V", 3, 0.1) { UnitParam = P.ScanUnit });
            ScopeTimescale = Track(new NumberParamViewModel(dev, P.ScopeXTimescale, "Time Scale (Scope 모드)", "ms", 3, 1));
            ScopeSpectrumRange = Track(new NumberParamViewModel(dev, P.ScopeXSpectrumRange, "Freq. Range (FFT 모드)", "kHz", 3, 1));
            d.Watch(P.ScanUnit, 1000);

            // ---------------- MHz 축 ----------------
            FreqAxis = new FrequencyAxisViewModel(dev, log, ScanCenterVolt, "스캔 중심(Scan Offset)", LockpointVolt);
            Calibration = new TuningCalibrationViewModel(dev, log, dialogs, FreqAxis, CurrentScanData);

            // ---------------- PID ----------------
            Pid1 = new PidViewModel(dev, 1);
            Pid2 = new PidViewModel(dev, 2);
            foreach (ParamRowViewModel r in Pid1.Rows) Track(r);
            foreach (ParamRowViewModel r in Pid2.Rows) Track(r);

            // ---------------- Lock-In ----------------
            LockinModEnabled = Track(new CheckParamViewModel(dev, P.LockinModEnabled, "Modulation 사용"));
            LockinModOutput = Track(ChoiceParamViewModel.Channels(dev, P.LockinModOutput, "Modulation Output", SignalChannels.ModulationOutputs));
            LockinFrequency = Track(new NumberParamViewModel(dev, P.LockinFrequency, "Frequency", "Hz", 1, 100));
            LockinAmplitude = Track(new NumberParamViewModel(dev, P.LockinAmplitude, "Amplitude", null, 4, 0.01));
            LockinPhase = Track(new NumberParamViewModel(dev, P.LockinPhase, "Phase Shift", "°", 2, 1));
            LockinLockLevel = Track(new NumberParamViewModel(dev, P.LockinLockLevel, "Lock Level", "V", 4, 0.001));
            LockinAutoLirProgress = Track(new ReadoutParamViewModel(dev, P.LockinAutoLirProgress, "Auto LIR 진행", "%", ReadoutParamViewModel.Num("F0"), 300));

            // ---------------- Filter ----------------
            FilterTop = Track(new CheckParamViewModel(dev, P.LockFilterTop, "Top (피크)"));
            FilterBottom = Track(new CheckParamViewModel(dev, P.LockFilterBottom, "Bottom (골)"));
            FilterPosEdge = Track(new CheckParamViewModel(dev, P.LockFilterPosEdge, "Rising edge"));
            FilterNegEdge = Track(new CheckParamViewModel(dev, P.LockFilterNegEdge, "Falling edge"));
            FilterEdgeLevel = Track(new NumberParamViewModel(dev, P.LockFilterEdgeLevel, "Edge Level", "V", 4, 0.01));
            FilterNoiseTol = Track(new NumberParamViewModel(dev, P.LockFilterNoiseTol, "Peak Noise Tol. (0=자동)", "V", 4, 0.001));

            d.WatchMany(200, P.LockState, P.LockStateTxt, P.LockType, P.LockSetpoint, P.LockFilterEdgeLevel, P.LockWithoutLockpoint);
            d.WatchMany(1000, P.ScopeCh1Name, P.ScopeCh1Unit, P.ScopeCh2Name, P.ScopeCh2Unit, P.ScopeXName, P.ScopeXUnit, P.ScopeVariant);
            d.WatchMany(300, P.ScanOffset, P.ScanOutputChannel, P.ScopeXSignal, P.DlPcVoltageSet, P.LockLockpointPosition, P.LockLockpointType, P.ScanEnabled);
            d.ScopeFrameReceived += f => _latest = f;
            dev.FreqSettingsChanged += () => {
                _updatingMhz = true;
                try { ShowMhz = Dev.Settings.Freq.ShowFrequency; }
                finally { _updatingMhz = false; }
                Plot.AutoScaleX = true;   // 단위가 바뀌면 X 범위 다시 맞춤
                _shown = null;            // 다음 틱에 다시 그리기
            };
        }

        public override string Title => "Scan & Lock";
        public override string Subtitle => "① 스캔 ON  ② 그래프에서 피크/엣지 클릭 → 락 포인트 선택  ③ LOCK  ·  더블클릭 = 자동 스케일";

        public PlotModel Plot { get; }
        public FrequencyAxisViewModel FreqAxis { get; }
        public TuningCalibrationViewModel Calibration { get; }
        public PidViewModel Pid1 { get; }
        public PidViewModel Pid2 { get; }

        // 툴바
        public ChoiceParamViewModel ScopeVariant { get; }
        public ChoiceParamViewModel ScopeCh1 { get; }
        public ChoiceParamViewModel ScopeCh2 { get; }
        public ChoiceParamViewModel ScopeX { get; }
        public NumberParamViewModel ScopeRate { get; }

        [ObservableProperty] private bool _freeze;
        [ObservableProperty] private bool _showMhz;
        [ObservableProperty] private LedState _lockLed;
        [ObservableProperty] private string _lockStateText = "—";
        [ObservableProperty] private bool _isLocked;

        // Lock
        public ChoiceParamViewModel LockType { get; }
        public ChoiceParamViewModel LockInput { get; }
        public ChoiceParamViewModel PidSelection { get; }
        public CheckParamViewModel WithoutLockpoint { get; }
        public NumberParamViewModel LockingDelay { get; }
        public NumberParamViewModel Setpoint { get; }
        public ReadoutParamViewModel Lockpoint { get; }
        public ComputedRowViewModel LockpointMhz { get; }
        public ReadoutParamViewModel LockpointType { get; }
        public ReadoutParamViewModel LockStateRow { get; }
        public ToggleParamViewModel LockHold { get; }

        // Scan
        public ToggleParamViewModel ScanEnable { get; }
        public CheckParamViewModel ScanHold { get; }
        public ChoiceParamViewModel ScanSignalType { get; }
        public ChoiceParamViewModel ScanOutput { get; }
        public NumberParamViewModel ScanFrequency { get; }
        public NumberParamViewModel ScanAmplitude { get; }
        public NumberParamViewModel ScanOffset { get; }
        public NumberParamViewModel ScanStart { get; }
        public NumberParamViewModel ScanEnd { get; }
        public NumberParamViewModel ScopeTimescale { get; }
        public NumberParamViewModel ScopeSpectrumRange { get; }

        // Lock-In
        public CheckParamViewModel LockinModEnabled { get; }
        public ChoiceParamViewModel LockinModOutput { get; }
        public NumberParamViewModel LockinFrequency { get; }
        public NumberParamViewModel LockinAmplitude { get; }
        public NumberParamViewModel LockinPhase { get; }
        public NumberParamViewModel LockinLockLevel { get; }
        public ReadoutParamViewModel LockinAutoLirProgress { get; }

        // Filter
        public CheckParamViewModel FilterTop { get; }
        public CheckParamViewModel FilterBottom { get; }
        public CheckParamViewModel FilterPosEdge { get; }
        public CheckParamViewModel FilterNegEdge { get; }
        public NumberParamViewModel FilterEdgeLevel { get; }
        public NumberParamViewModel FilterNoiseTol { get; }

        partial void OnShowMhzChanged(bool value) {
            if (_updatingMhz) return;
            Dev.Settings.Freq.ShowFrequency = value;
            Dev.NotifyFreqChanged();
        }

        protected override void OnActivated(bool active) {
            // 스코프 데이터는 이 페이지가 보일 때만 받아온다 (통신량 절약)
            Dev.Device.ScopeStreaming = active;
            if (active) _shown = null;
        }

        // ------------------------------------------------------------------
        // 명령
        // ------------------------------------------------------------------

        [RelayCommand]
        private async Task FindCandidatesAsync() {
            if (!Dev.Device.GetBool(P.ScanEnabled, false) && !await Dev.SetAsync(P.ScanEnabled, true)) return;
            await Dev.ExecAsync(P.CmdLockFindCandidates);
        }

        [RelayCommand]
        private async Task LockAsync() {
            int st = Dev.Device.GetInt(P.LockState, -1);
            bool wlp = Dev.Device.GetBool(P.LockWithoutLockpoint, false);
            if (!wlp && st != (int)LockStateCode.Selected) {
                await _dialogs.AlertAsync("Lock", "락 포인트가 선택되지 않았습니다.\n그래프에서 락 포인트를 먼저 클릭하세요.", DialogKind.Info);
                return;
            }
            if (await Dev.ExecAsync(P.CmdLockClose) != null) _log.Info("LOCK 명령");
        }

        [RelayCommand]
        private async Task UnlockAsync() {
            if (await Dev.ExecAsync(P.CmdLockOpen) != null) _log.Info("UNLOCK 명령");
        }

        [RelayCommand]
        private Task AutoLirStartAsync() => Dev.ExecAsync(P.CmdLockinAutoLirStart);

        [RelayCommand]
        private Task AutoLirAbortAsync() => Dev.ExecAsync(P.CmdLockinAutoLirAbort);

        [RelayCommand]
        private void ResetScale() => Plot.ResetScale();

        /// <summary>현재 스코프 X축이 피에조 전압인지 (XY 모드, X=[50] 또는 [101]+스캔 출력 [50]).</summary>
        private bool ScopeXIsPiezo() {
            DlcDevice d = Dev.Device;
            if (!d.TryGetInt(P.ScopeVariant, out int variant) || variant != 0) return false;
            if (!d.TryGetInt(P.ScopeXSignal, out int xs) || !d.TryGetInt(P.ScanOutputChannel, out int so)) return false;
            return xs == 50 || (xs == 101 && so == 50);
        }

        /// <summary>교정용: 최근 스코프 프레임 (피에조 전압 vs CH1). 조건이 맞지 않으면 null.</summary>
        private SpectrumData? CurrentScanData() {
            ScopeFrame? f = _shown ?? _latest;
            if (f?.X == null || f.Y1 == null || f.X.Length < 100 || !ScopeXIsPiezo()) return null;
            int n = Math.Min(f.X.Length, f.Y1.Length);
            var pts = new List<(double x, double y)>(n);
            for (int i = 0; i < n; i++) if (float.IsFinite(f.X[i]) && float.IsFinite(f.Y1[i])) pts.Add((f.X[i], f.Y1[i]));
            pts.Sort((a, b) => a.x.CompareTo(b.x));
            string xn = AxisLabel(Dev.Device.GetString(P.ScopeXName) ?? "Piezo Voltage", "V");
            string yn = AxisLabel(Dev.Device.GetString(P.ScopeCh1Name) ?? "CH1", Dev.Device.GetString(P.ScopeCh1Unit));
            return new SpectrumData {
                X = pts.ConvertAll(p => p.x).ToArray(), Y = pts.ConvertAll(p => p.y).ToArray(),
                XName = xn, YName = yn, Columns = new[] { xn, yn }, YColumn = 1,
                Source = "현재 스캔 " + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            };
        }

        /// <summary>
        /// 현재 스캔 그래프를 CSV로 저장 (X = 피에조 전압 V 그대로, DLC pro 내보내기와 같은 형식).
        /// MHz 축 표시와 관계없이 전압으로 저장하므로 튜닝 계수 교정에 바로 쓸 수 있다.
        /// </summary>
        [RelayCommand]
        private async Task SaveScanCsvAsync() {
            ScopeFrame? f = _shown ?? _latest;
            if (f?.X == null || f.Y1 == null || f.X.Length == 0) {
                await _dialogs.AlertAsync("스캔 CSV 저장", "저장할 스캔 데이터가 없습니다. 스캔을 켜고 스펙트럼이 보이는 상태에서 저장해 주세요.", DialogKind.Info);
                return;
            }
            if (!ScopeXIsPiezo())
                _log.Warn("스캔 CSV: 스코프 X축이 피에조 전압이 아닙니다. 이 파일은 튜닝 계수 교정에 쓸 수 없습니다.");
            string? path = await _dialogs.SaveFilePickerAsync("스캔 데이터 CSV 저장", "scan_laser" + Dev.Device.LaserId + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv", "CSV", "*.csv");
            if (path == null) return;
            try {
                DlcDevice d = Dev.Device;
                string xn = AxisLabel(d.GetString(P.ScopeXName) ?? "X", d.GetString(P.ScopeXUnit));
                var ys = new List<(string, IReadOnlyList<float>)> { (AxisLabel(d.GetString(P.ScopeCh1Name) ?? "CH1", d.GetString(P.ScopeCh1Unit)), f.Y1) };
                if (f.Y2 != null) ys.Add((AxisLabel(d.GetString(P.ScopeCh2Name) ?? "CH2", d.GetString(P.ScopeCh2Unit)), f.Y2));
                SpectrumCsv.Save(path, xn, f.X, ys);
                _log.Info("스캔 CSV 저장: " + path + " (" + f.X.Length + "점)");
            }
            catch (Exception ex) { Dev.ReportError("스캔 CSV 저장 실패: " + ex.Message); }
        }

        /// <summary>그래프 클릭 → 락 포인트 선택 (laser1:dl:lock:select-lockpoint x y 0).</summary>
        [RelayCommand]
        private async Task SelectLockpointAsync(PlotPoint p) {
            DlcDevice d = Dev.Device;
            if (!d.IsConnected || _clickBusy) return;   // 처리 중인 클릭이 있으면 무시 (중복 명령 방지)
            if (d.GetInt(P.ScopeVariant, 0) != 0) {
                _log.SetStatus("락 포인트 선택은 XY(스펙트럼) 모드에서만 가능합니다.");
                return;
            }
            FrequencyTransform? xf = _xf;
            double x = p.X, y = p.Y, shownX = p.X;
            if (xf.HasValue) x = xf.Value.ToVolt(x);   // 그래프가 MHz면 장비 명령용 전압으로 되돌림
            int st = d.GetInt(P.LockState, -1);
            if (st < 0) {
                _log.SetStatus("락 모듈 상태를 아직 알 수 없습니다 (Lock 옵션/연결 확인).");
                return;
            }
            if (st >= (int)LockStateCode.Locking) {
                _log.SetStatus("락 상태에서는 락 포인트를 바꿀 수 없습니다. 먼저 UNLOCK 하세요.");
                return;
            }
            _clickBusy = true;
            try {
                if (st == (int)LockStateCode.Idle) await d.SetAsync(P.ScanEnabled, true);
                if (st < (int)LockStateCode.Selecting) await d.ExecAsync(P.CmdLockFindCandidates);
                await d.ExecAsync(P.CmdLockSelectLockpoint, x, y, 0);
                _log.SetStatus(xf.HasValue
                    ? string.Format(CultureInfo.InvariantCulture, "락 포인트 선택 요청: {0:F2} MHz (= {1:F4} V), y={2:F4}", shownX, x, y)
                    : string.Format(CultureInfo.InvariantCulture, "락 포인트 선택 요청: x={0:F4}, y={1:F4}", x, y));
            }
            catch (Exception ex) { Dev.ReportError("락 포인트 선택 실패: " + DeviceService.Unwrap(ex).Message); }
            finally { _clickBusy = false; }
        }

        // ------------------------------------------------------------------
        // 계산
        // ------------------------------------------------------------------

        /// <summary>스캔 중심 전압 (스캔 출력이 피에조일 때 laser1:scan:offset, 아니면 피에조 설정 전압).</summary>
        private double ScanCenterVolt() {
            DlcDevice d = Dev.Device;
            if (d.GetInt(P.ScanOutputChannel, 50) == 50) return d.GetDouble(P.ScanOffset, double.NaN);
            return d.GetDouble(P.DlPcVoltageSet, double.NaN);
        }

        private double LockpointVolt() {
            if (!Dev.Device.TryGetRaw(P.LockLockpointPosition, out string raw)) return double.NaN;
            List<string> t = DecofValue.Tuple(raw);
            if (Dev.Device.GetString(P.LockLockpointType) == "none") return double.NaN;
            return t.Count >= 1 && DecofValue.TryDouble(t[0], out double x) ? x : double.NaN;
        }

        /// <summary>
        /// 현재 스코프 X축이 피에조 전압이면 V→MHz 변환을 만든다.
        /// (XY 모드 + X 신호가 [50] Piezo 또는 [101] 스캔 출력 채널이면서 스캔 출력이 [50] Piezo)
        /// </summary>
        private FrequencyTransform? CurrentTransform() {
            FrequencyAxisSettings s = Dev.Settings.Freq;
            if (!s.ShowFrequency || !s.HasCoefficient) return null;
            if (!ScopeXIsPiezo()) return null;
            return FrequencyTransform.Create(s, ScanCenterVolt());
        }

        // ------------------------------------------------------------------
        // 화면 갱신 (100 ms)
        // ------------------------------------------------------------------

        protected override void OnActiveTick() {
            DlcDevice d = Dev.Device;
            int st = d.GetInt(P.LockState, -1);
            string? txt = d.GetString(P.LockStateTxt);
            LockStateText = d.IsConnected ? (txt ?? "—") : "연결 안 됨";
            LockLed = st == (int)LockStateCode.Locked ? LedState.On
                : st == (int)LockStateCode.Relocking || st == (int)LockStateCode.Locking ? LedState.Warn
                : st == (int)LockStateCode.OnHold || st == (int)LockStateCode.Reset ? LedState.Info
                : LedState.Off;
            IsLocked = st == (int)LockStateCode.Locked;
            FreqAxis.UpdateInfo();

            if (Freeze) return;
            ScopeFrame? f = _latest;
            if (f == null || ReferenceEquals(f, _shown)) return;
            _shown = f;

            bool locked = st >= (int)LockStateCode.Locking;
            FrequencyTransform? xf = CurrentTransform();
            _xf = xf;
            float[] fx = xf.HasValue ? xf.Value.ToMHz(f.X) : f.X;
            _ch1.X = fx;
            _ch1.Y = f.Y1;
            _ch1.Points = locked && d.GetInt(P.ScopeVariant, 0) == 0;
            _ch2.X = fx;
            _ch2.Y = f.Y2;
            _ch2.Visible = f.Y2 != null;
            _bg.X = xf.HasValue && f.BackgroundX != null ? xf.Value.ToMHz(f.BackgroundX) : f.BackgroundX;
            _bg.Y = f.BackgroundY;
            _bg.Visible = locked && f.BackgroundX != null;

            _ch1.Name = d.GetString(P.ScopeCh1Name) ?? "CH1";
            _ch2.Name = d.GetString(P.ScopeCh2Name) ?? "CH2";
            Plot.XLabel = xf.HasValue
                ? "Relative Frequency [MHz]   (0 MHz = " + xf.Value.ZeroVoltage.ToString("F3", CultureInfo.InvariantCulture) + " V)"
                : AxisLabel(d.GetString(P.ScopeXName), d.GetString(P.ScopeXUnit));
            Plot.YLabel = AxisLabel(_ch1.Name, d.GetString(P.ScopeCh1Unit));
            Plot.Y2Label = AxisLabel(_ch2.Name, d.GetString(P.ScopeCh2Unit));

            Plot.Markers.Clear();
            Plot.Lines.Clear();
            if (f.Candidates != null) {
                foreach (LockCandidate cd in f.Candidates) {
                    if (!cd.IsValid) continue;
                    PlotMarkerShape sh = cd.Type == 1 ? PlotMarkerShape.TriangleUp : cd.Type == 2 ? PlotMarkerShape.TriangleDown : PlotMarkerShape.Diamond;
                    Plot.Markers.Add(new PlotMarker { X = TX(xf, cd.X), Y = cd.Y, Color = PlotColors.Candidate, Shape = sh, Size = 5 });
                }
            }
            if (f.LockPoint.HasValue) {
                LockCandidate lp = f.LockPoint.Value;
                Plot.Markers.Add(new PlotMarker { X = TX(xf, lp.X), Y = lp.Y, Color = PlotColors.LockPoint, Shape = PlotMarkerShape.Circle, Size = 9, Label = "Lock point" });
                Plot.Lines.Add(new PlotLine { Value = TX(xf, lp.X), Vertical = true, Color = PlotColors.LockLine, Dash = PlotDash.Dash });
            }
            if (f.Tracking.HasValue && locked) {
                LockCandidate t = f.Tracking.Value;
                if (t.X != 0 || t.Y != 0)
                    Plot.Markers.Add(new PlotMarker { X = TX(xf, t.X), Y = t.Y, Color = PlotColors.Tracking, Shape = PlotMarkerShape.Cross, Size = 7 });
            }
            if (d.GetInt(P.LockType, 1) == 2 && d.TryGetDouble(P.LockSetpoint, out double sp))
                Plot.Lines.Add(new PlotLine { Value = sp, Color = PlotColors.Setpoint, Dash = PlotDash.Dash, Label = "setpoint" });

            string overlay = locked ? "LOCKED — 회색: 락 직전 스펙트럼" : (st == (int)LockStateCode.Selecting ? "그래프를 클릭해 락 포인트를 선택하세요" : "");
            if (Dev.Settings.Freq.ShowFrequency && !xf.HasValue) {
                string why = !Dev.Settings.Freq.HasCoefficient
                    ? "튜닝 계수(GHz/V) 미입력 — 'MHz 축' 탭에서 입력"
                    : "X축이 피에조 전압이 아니어서 MHz 변환 불가 (XY 모드, X=[101]/[50], 스캔 출력=[50] 필요)";
                overlay = (overlay.Length > 0 ? overlay + "\n" : "") + why;
            }
            Plot.Overlay = overlay;
            Plot.Invalidate();
        }

        private static double TX(FrequencyTransform? xf, double v) => xf.HasValue ? xf.Value.ToMHz(v) : v;

        private static string AxisLabel(string? name, string? unit) {
            if (string.IsNullOrEmpty(name)) return "";
            return string.IsNullOrEmpty(unit) ? name : name + " [" + unit + "]";
        }
    }
}
