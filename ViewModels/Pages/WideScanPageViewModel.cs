using System;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLC_PRO.Core;
using DLC_PRO.Data;
using DLC_PRO.Models.Plot;
using DLC_PRO.Services;
using DLC_PRO.ViewModels.Dialogs;
using DLC_PRO.ViewModels.Params;

namespace DLC_PRO.ViewModels.Pages {
    /// <summary>
    /// Wide Scan 페이지 — 단발성 광대역 스캔 (TOPAS Wide Scan 패널) + 레코더 데이터 표시/저장.
    /// 와이드스캔 데이터는 레코더(laser1:recorder)에 기록된다.
    /// </summary>
    public partial class WideScanPageViewModel : PageViewModel {
        private readonly DialogService _dialogs;
        private readonly LogService _log;
        private readonly PlotSeries _s1, _s2;
        private RecorderData? _data;
        private int _lastState = -1;
        private FrequencyTransform? _xf;
        // 데이터를 읽을 때의 와이드스캔 조건 (이후 설정이 바뀌어도 저장된 데이터 해석은 그대로)
        private double _dataCenterVolt = double.NaN;
        private int _dataOutput = -1;

        public WideScanPageViewModel(DeviceService dev, DialogService dialogs, LogService log) : base(ApplicationPageNames.WideScan, dev) {
            _dialogs = dialogs;
            _log = log;

            WsOutput = Track(ChoiceParamViewModel.Channels(dev, P.WsOutput, "X-Axis Signal (출력)", SignalChannels.Outputs));
            WsValueSet = Track(new NumberParamViewModel(dev, P.WsValueSet, "Set Value", "", 4, 0.1) { UnitParam = P.WsValueUnit });
            WsValueAct = Track(new ReadoutParamViewModel(dev, P.WsValueAct, "Actual Value", "", ReadoutParamViewModel.Num("F4"), 150) { UnitParam = P.WsValueUnit });
            WsBegin = Track(new NumberParamViewModel(dev, P.WsBegin, "Start Value", "", 4, 0.1) { UnitParam = P.WsValueUnit });
            WsEnd = Track(new NumberParamViewModel(dev, P.WsEnd, "End Value", "", 4, 0.1) { UnitParam = P.WsValueUnit });
            WsSpeed = Track(new NumberParamViewModel(dev, P.WsSpeed, "Speed", "unit/s", 4, 0.1));
            WsDuration = Track(new NumberParamViewModel(dev, P.WsDuration, "Duration", "s", 2, 1));
            WsShape = Track(ChoiceParamViewModel.Enum(dev, P.WsShape, "Shape", "0=Sawtooth", "1=Triangle"));
            WsContinuous = Track(new CheckParamViewModel(dev, P.WsContinuous, "연속(반복) 모드"));
            WsRestoreOnEnd = Track(new CheckParamViewModel(dev, P.WsRestoreOnEnd, "종료 후 원래 값 복귀"));
            WsStepsize = Track(new NumberParamViewModel(dev, P.WsRecorderStepsizeSet, "Recorder Step (0=자동)", null, 5, 0.001));
            WsSampleCount = Track(new ReadoutParamViewModel(dev, P.WsRecorderSampleCount, "예상 샘플 수", null, null, 1000));
            WsStateRow = Track(new ReadoutParamViewModel(dev, P.WsStateTxt, "State", null, null, 200));
            RecCh1 = Track(ChoiceParamViewModel.Channels(dev, P.RecCh1Signal, "CH1", SignalChannels.Display));
            RecCh2 = Track(ChoiceParamViewModel.Channels(dev, P.RecCh2Signal, "CH2", SignalChannels.Display));
            dev.Device.Watch(P.WsValueUnit, 1000);

            FreqAxis = new FrequencyAxisViewModel(dev, log, WideCenterVolt, "Wide Scan 중심(Start~End)");

            Plot = new PlotModel { Title = "Wide Scan 결과" };
            _s1 = Plot.AddSeries("CH1", PlotColors.Trace1);
            _s2 = Plot.AddSeries("CH2", PlotColors.Trace2, true);

            dev.Device.WatchMany(200, P.WsState, P.WsProgress, P.WsRemaining);
            dev.Device.WatchMany(500, P.WsOutput, P.WsBegin, P.WsEnd, P.ScanEnabled);
            dev.FreqSettingsChanged += () => {
                Plot.AutoScaleX = true;
                ShowData();
            };
        }

        public override string Title => "Wide Scan";
        public override string Subtitle => "광대역 단발 스캔 — 결과는 레코더에 기록되며 종료 시 자동으로 읽어 표시";

        public PlotModel Plot { get; }
        public FrequencyAxisViewModel FreqAxis { get; }

        public ChoiceParamViewModel WsOutput { get; }
        public NumberParamViewModel WsValueSet { get; }
        public ReadoutParamViewModel WsValueAct { get; }
        public NumberParamViewModel WsBegin { get; }
        public NumberParamViewModel WsEnd { get; }
        public NumberParamViewModel WsSpeed { get; }
        public NumberParamViewModel WsDuration { get; }
        public ChoiceParamViewModel WsShape { get; }
        public CheckParamViewModel WsContinuous { get; }
        public CheckParamViewModel WsRestoreOnEnd { get; }
        public NumberParamViewModel WsStepsize { get; }
        public ReadoutParamViewModel WsSampleCount { get; }
        public ReadoutParamViewModel WsStateRow { get; }
        public ChoiceParamViewModel RecCh1 { get; }
        public ChoiceParamViewModel RecCh2 { get; }

        [ObservableProperty] private double _progress;
        [ObservableProperty] private string _progressText = "";
        [ObservableProperty] private bool _autoLoad = true;
        [ObservableProperty] private bool _isLoading;

        protected override void OnTick() {
            DlcDevice d = Dev.Device;
            int st = d.GetInt(P.WsState, -1);
            if (_lastState > 0 && st == 0 && AutoLoad) {
                _log.Info("Wide Scan 종료 → 데이터 읽기");
                _ = LoadDataAsync();
            }
            _lastState = st;
        }

        protected override void OnActiveTick() {
            DlcDevice d = Dev.Device;
            int prog = d.GetInt(P.WsProgress, 0);
            Progress = Math.Max(0, Math.Min(100, prog));
            int rem = d.GetInt(P.WsRemaining, 0);
            ProgressText = prog + "% (" + (rem / 60) + "m " + (rem % 60) + "s)";
            FreqAxis.UpdateInfo();
        }

        [RelayCommand]
        private async Task StartAsync() {
            DlcDevice d = Dev.Device;
            if (d.GetInt(P.LockState, 0) >= (int)LockStateCode.Locking) {
                await _dialogs.AlertAsync("Wide Scan", "락이 걸려 있습니다. 먼저 UNLOCK 하세요.", DialogKind.Warning);
                return;
            }
            // 매뉴얼: wide-scan:start 는 laser1:scan:enabled 가 #f 일 때만 동작
            if (d.GetBool(P.ScanEnabled, false)) {
                if (!await _dialogs.ConfirmAsync("Wide Scan", "Wide Scan은 일반 스캔(Scan Generator)이 꺼져 있어야 시작됩니다.\n스캔을 끄고 시작할까요?"))
                    return;
                if (d.GetInt(P.LockState, 0) >= (int)LockStateCode.Locking) {
                    await _dialogs.AlertAsync("Wide Scan", "락이 걸려 있습니다. 먼저 UNLOCK 하세요.", DialogKind.Warning);
                    return;
                }
                if (!await Dev.SetAsync(P.ScanEnabled, false)) return;
            }
            await Dev.ExecAsync(P.CmdWsStart);
        }

        [RelayCommand]
        private Task StopAsync() => Dev.ExecAsync(P.CmdWsStop);

        [RelayCommand]
        private async Task LoadDataAsync() {
            if (IsLoading) return;
            IsLoading = true;
            try {
                _log.SetStatus("레코더 데이터 읽는 중...");
                RecorderData data = await RecorderData.FetchAsync(Dev.Device, null);
                _data = data;
                _dataCenterVolt = WideCenterVolt();
                _dataOutput = Dev.Device.GetInt(P.WsOutput, -1);
                if (data.Incomplete) _log.Warn("Wide Scan 데이터 일부만 읽힘 (" + data.Count + "점)");
                Plot.AutoScaleX = Plot.AutoScaleY = true;
                ShowData();
                _log.SetStatus("데이터 " + data.Count.ToString(CultureInfo.InvariantCulture) + "점 읽음");
            }
            catch (Exception ex) { Dev.ReportError("데이터 읽기 실패: " + DeviceService.Unwrap(ex).Message); }
            finally { IsLoading = false; }
        }

        [RelayCommand]
        private async Task SaveCsvAsync() {
            RecorderData? data = _data;
            if (data == null || data.Count == 0) {
                await _dialogs.AlertAsync("CSV 저장", "먼저 데이터를 읽으세요.");
                return;
            }
            string? path = await _dialogs.SaveFilePickerAsync("Wide Scan CSV 저장", "widescan_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv", "CSV", "*.csv");
            if (path == null) return;
            FrequencyTransform? xf = _xf;
            try {
                if (xf.HasValue) data.SaveCsv(path, v => xf.Value.ToMHz(v), "Relative Frequency [MHz] (" + xf.Value.Describe() + ")");
                else data.SaveCsv(path);
                _log.Info("저장: " + path);
            }
            catch (Exception ex) { Dev.ReportError("CSV 저장 실패: " + ex.Message); }
        }

        /// <summary>Wide Scan 범위의 중심 전압.</summary>
        private double WideCenterVolt() {
            DlcDevice d = Dev.Device;
            if (d.TryGetDouble(P.WsBegin, out double b0) && d.TryGetDouble(P.WsEnd, out double b1)) return (b0 + b1) / 2;
            return double.NaN;
        }

        /// <summary>X축이 피에조 전압(V)인 와이드스캔 데이터면 V→MHz 변환을 만든다.</summary>
        private FrequencyTransform? CurrentTransform(RecorderData data) {
            if (_dataOutput != 50) return null;                 // 피에조 출력으로 스캔한 데이터만 변환
            if (!string.IsNullOrEmpty(data.XUnit) && data.XUnit.Trim() != "V") return null;
            return FrequencyTransform.Create(Dev.Settings.Freq, _dataCenterVolt);
        }

        private void ShowData() {
            RecorderData? data = _data;
            if (data == null) return;
            FrequencyTransform? xf = CurrentTransform(data);
            _xf = xf;
            float[] x = xf.HasValue ? xf.Value.ToMHz(data.X) : data.X;
            _s1.X = x;
            _s1.Y = data.Y1;
            _s1.Name = data.Y1Name;
            _s2.X = x;
            _s2.Y = data.Y2;
            _s2.Name = data.Y2Name;
            _s2.Visible = data.HasY2 && data.Y2Name != "none";
            Plot.XLabel = xf.HasValue
                ? "Relative Frequency [MHz]   (0 MHz = " + xf.Value.ZeroVoltage.ToString("F3", CultureInfo.InvariantCulture) + " V)"
                : data.XName + (string.IsNullOrEmpty(data.XUnit) ? "" : " [" + data.XUnit + "]");
            Plot.YLabel = data.Y1Name + (string.IsNullOrEmpty(data.Y1Unit) ? "" : " [" + data.Y1Unit + "]");
            Plot.Y2Label = data.Y2Name + (string.IsNullOrEmpty(data.Y2Unit) ? "" : " [" + data.Y2Unit + "]");
            Plot.Overlay = Dev.Settings.Freq.ShowFrequency && !xf.HasValue
                ? (!Dev.Settings.Freq.HasCoefficient ? "튜닝 계수(GHz/V) 미입력 → 전압 표시" : "X축이 피에조 전압이 아니어서 MHz 변환 불가")
                : "";
            Plot.Invalidate();
        }
    }
}
