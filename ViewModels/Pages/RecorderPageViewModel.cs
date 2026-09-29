using System;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLC_PRO.Core;
using DLC_PRO.Data;
using DLC_PRO.Models.Plot;
using DLC_PRO.Services;
using DLC_PRO.ViewModels.Params;

namespace DLC_PRO.ViewModels.Pages {
    /// <summary>Recorder 페이지 — 실시간 데이터 수집 (최대 ~200 kHz, 3채널) 설정/실행/읽기/저장.</summary>
    public partial class RecorderPageViewModel : PageViewModel {
        private readonly DialogService _dialogs;
        private readonly LogService _log;
        private readonly PlotSeries _s1, _s2;
        private RecorderData? _data;
        private int _lastState = -1;

        public RecorderPageViewModel(DeviceService dev, DialogService dialogs, LogService log) : base(ApplicationPageNames.Recorder, dev) {
            _dialogs = dialogs;
            _log = log;

            Trigger = Track(ChoiceParamViewModel.Enum(dev, P.RecTriggerMode, "Trigger", "0=즉시", "2=Wide-scan / Scan-aux", "4=DI0 상승", "5=DI0 하강", "6=DI1 상승", "7=DI1 하강"));
            Mode = Track(ChoiceParamViewModel.Enum(dev, P.RecRecordingMode, "Recording Mode", "0=reset (1회)", "2=continuous"));
            ChX = Track(ChoiceParamViewModel.Channels(dev, P.RecChxSignal, "X 채널", new[] { -2, 0, 1, 2, 4, 20, 21, 50, 51, 56, 57, 63 }));
            Ch1 = Track(ChoiceParamViewModel.Channels(dev, P.RecCh1Signal, "CH1", SignalChannels.Display));
            Ch2 = Track(ChoiceParamViewModel.Channels(dev, P.RecCh2Signal, "CH2", SignalChannels.Display));
            Ch1Lp = Track(new CheckParamViewModel(dev, P.RecCh1LpEnabled, "CH1 저역통과"));
            Ch2Lp = Track(new CheckParamViewModel(dev, P.RecCh2LpEnabled, "CH2 저역통과"));
            Ch1Cutoff = Track(new NumberParamViewModel(dev, P.RecCh1LpCutoff, "CH1 Cut-off", "Hz", 1, 100));
            Ch2Cutoff = Track(new NumberParamViewModel(dev, P.RecCh2LpCutoff, "CH2 Cut-off", "Hz", 1, 100));
            RecordingTime = Track(new NumberParamViewModel(dev, P.RecRecordingTime, "Recording Time", "ms", 3, 10));
            SampleCountSet = Track(new NumberParamViewModel(dev, P.RecSampleCountSet, "Sample Count (설정)", null, 0, 1000));
            SampleCount = Track(new ReadoutParamViewModel(dev, P.RecSampleCount, "Sample Count (실제)", null, null, 500));
            SamplingRate = Track(new ReadoutParamViewModel(dev, P.RecSamplingRate, "Sampling Rate", "Hz", ReadoutParamViewModel.Num("F1"), 500));
            StateRow = Track(new ReadoutParamViewModel(dev, P.RecStateTxt, "State", null, null, 200));
            DataCount = Track(new ReadoutParamViewModel(dev, P.RecDataCount, "기록된 샘플", null, null, 300));

            Plot = new PlotModel { Title = "Recorder 데이터" };
            _s1 = Plot.AddSeries("CH1", PlotColors.Trace1);
            _s2 = Plot.AddSeries("CH2", PlotColors.Trace2, true);

            dev.Device.Watch(P.RecState, 200);
            dev.Device.Watch(P.WsState, 500);
        }

        public override string Title => "Recorder";
        public override string Subtitle => "실시간 데이터 수집 (최대 약 200 kHz) — 설정 · 기록 · 읽기 · CSV 저장";

        public PlotModel Plot { get; }

        public ChoiceParamViewModel Trigger { get; }
        public ChoiceParamViewModel Mode { get; }
        public ChoiceParamViewModel ChX { get; }
        public ChoiceParamViewModel Ch1 { get; }
        public ChoiceParamViewModel Ch2 { get; }
        public CheckParamViewModel Ch1Lp { get; }
        public CheckParamViewModel Ch2Lp { get; }
        public NumberParamViewModel Ch1Cutoff { get; }
        public NumberParamViewModel Ch2Cutoff { get; }
        public NumberParamViewModel RecordingTime { get; }
        public NumberParamViewModel SampleCountSet { get; }
        public ReadoutParamViewModel SampleCount { get; }
        public ReadoutParamViewModel SamplingRate { get; }
        public ReadoutParamViewModel StateRow { get; }
        public ReadoutParamViewModel DataCount { get; }

        [ObservableProperty] private bool _autoLoad = true;
        [ObservableProperty] private double _loadProgress;
        [ObservableProperty] private bool _isLoading;

        protected override void OnTick() {
            int st = Dev.Device.GetInt(P.RecState, -1);
            // 와이드스캔이 끝난 경우는 Wide Scan 페이지가 읽으므로 여기서는 읽지 않음
            if (_lastState == 2 && st == 0 && AutoLoad && Dev.Device.GetInt(P.WsState, 0) == 0 && IsActive)
                _ = LoadDataAsync();
            _lastState = st;
        }

        [RelayCommand]
        private Task ArmAsync() => Dev.SetAsync(P.RecEnabled, true);

        [RelayCommand]
        private Task StopAsync() => Dev.SetAsync(P.RecEnabled, false);

        [RelayCommand]
        private Task ClearDeviceDataAsync() => Dev.ExecAsync(P.CmdRecClearData);

        [RelayCommand]
        private async Task LoadDataAsync() {
            if (IsLoading) return;
            IsLoading = true;
            try {
                LoadProgress = 0;
                IProgress<double> ip = new Progress<double>(p => LoadProgress = p);
                RecorderData data = await RecorderData.FetchAsync(Dev.Device, (i, n) => ip.Report(n > 0 ? (double)i * 100 / n : 100));
                _data = data;
                if (data.Incomplete) _log.Warn("레코더 데이터 일부만 읽힘 (" + data.Count + "점)");
                _s1.X = data.X;
                _s1.Y = data.Y1;
                _s1.Name = data.Y1Name;
                _s2.X = data.X;
                _s2.Y = data.Y2;
                _s2.Name = data.Y2Name;
                _s2.Visible = data.HasY2 && data.Y2Name != "none";
                Plot.XLabel = data.XName + (string.IsNullOrEmpty(data.XUnit) ? "" : " [" + data.XUnit + "]");
                Plot.YLabel = data.Y1Name + (string.IsNullOrEmpty(data.Y1Unit) ? "" : " [" + data.Y1Unit + "]");
                Plot.Y2Label = data.Y2Name + (string.IsNullOrEmpty(data.Y2Unit) ? "" : " [" + data.Y2Unit + "]");
                Plot.AutoScaleX = Plot.AutoScaleY = true;
                Plot.Invalidate();
                LoadProgress = 100;
                _log.SetStatus("레코더 데이터 " + data.Count.ToString(CultureInfo.InvariantCulture) + "점 읽음");
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
            string? path = await _dialogs.SaveFilePickerAsync("Recorder CSV 저장", "recorder_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv", "CSV", "*.csv");
            if (path == null) return;
            try {
                data.SaveCsv(path);
                _log.Info("저장: " + path);
            }
            catch (Exception ex) { Dev.ReportError("CSV 저장 실패: " + ex.Message); }
        }
    }
}
