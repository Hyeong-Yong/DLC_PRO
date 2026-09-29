using System;
using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using DLC_PRO.Core;
using DLC_PRO.Data;
using DLC_PRO.Models.Plot;
using DLC_PRO.Services;
using DLC_PRO.ViewModels.Params;

namespace DLC_PRO.ViewModels.Pages {
    /// <summary>
    /// Stabilization 페이지 — 출력 파워 안정화(Power Lock) + 외부 PD 보정 + 파워 추세 그래프.
    /// 추세 샘플링은 페이지가 보이지 않아도 계속된다.
    /// </summary>
    public partial class StabilizationPageViewModel : PageViewModel {
        private static readonly string[] PsStates = { "off", "in lock", "on hold", "suspended", "limited", "unstable" };
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly TrendBuffer _tAct, _tSet, _tSeed, _tAmp;
        private readonly PlotSeries _sAct, _sSet, _sSeed, _sAmp;
        private TimeSpan _lastSample = TimeSpan.Zero;

        public StabilizationPageViewModel(DeviceService dev) : base(ApplicationPageNames.Stabilization, dev) {
            PsEnable = Track(new ToggleParamViewModel(dev, P.PsEnabled));
            PsState = Track(new ReadoutParamViewModel(dev, P.PsState, "State", null, raw =>
                DecofValue.TryInt(raw, out int v) && v >= 0 && v < PsStates.Length ? PsStates[v] : raw, 200));
            PsInput = Track(ChoiceParamViewModel.Channels(dev, P.PsInput, "Input Channel", SignalChannels.PowerStabInputs));
            PsOutput = Track(new ReadoutParamViewModel(dev, P.PsOutput, "Output Channel", null, ReadoutParamViewModel.Channel, 2000));
            PsSetpoint = Track(new NumberParamViewModel(dev, P.PsSetpoint, "Setpoint", "mW", 3, 1));
            PsValue = Track(new ReadoutParamViewModel(dev, P.PsValueAct, "Actual Power", "mW", ReadoutParamViewModel.Num("F3"), 100));
            PsGainAll = Track(new NumberParamViewModel(dev, P.PsGainAll, "Gain (all)", null, 4, 0.1));
            PsGainP = Track(new NumberParamViewModel(dev, P.PsGainP, "P", "mA/mW", 4, 0.01));
            PsGainI = Track(new NumberParamViewModel(dev, P.PsGainI, "I", "mA/(mW·ms)", 4, 0.01));
            PsGainD = Track(new NumberParamViewModel(dev, P.PsGainD, "D", "mA·µs/mW", 4, 0.01));
            PsSign = Track(new CheckParamViewModel(dev, P.PsSign, "Sign (전류↓ → 파워↑)"));
            PsHoldOnUnlock = Track(new CheckParamViewModel(dev, P.PsHoldOnUnlock, "Hold output on unlock"));
            PsWindowEnabled = Track(new CheckParamViewModel(dev, P.PsWindowEnabled, "Window (파워 감시) 사용"));
            PsWindowLow = Track(new NumberParamViewModel(dev, P.PsWindowLow, "Window Level Low", "mW", 3, 1));
            PsWindowHyst = Track(new NumberParamViewModel(dev, P.PsWindowHyst, "Window Hysteresis", "mW", 3, 1));

            PdInput = Track(ChoiceParamViewModel.Channels(dev, P.PdExtInput, "Input Channel", new[] { 0, 1, 2, 4 }));
            PdPhotodiode = Track(new ReadoutParamViewModel(dev, P.PdExtPhotodiode, "Photodiode", "V", ReadoutParamViewModel.Num("F4"), 200));
            PdPower = Track(new ReadoutParamViewModel(dev, P.PdExtPower, "Power", "mW", ReadoutParamViewModel.Num("F3"), 200));
            PdCalOffset = Track(new NumberParamViewModel(dev, P.PdExtCalOffset, "Cal. Offset", "V", 4, 0.001));
            PdCalFactor = Track(new NumberParamViewModel(dev, P.PdExtCalFactor, "Cal. Factor", "mW/V", 4, 0.01));

            _tAct = new TrendBuffer(6000, _clock);
            _tSet = new TrendBuffer(6000, _clock);
            _tSeed = new TrendBuffer(6000, _clock);
            _tAmp = new TrendBuffer(6000, _clock);
            Plot = new PlotModel { Title = "파워 추세 (최근 약 10분)", XLabel = "시간 [s]", YLabel = "파워 [mW]", Y2Label = "Seed [mW]", MinYSpan = 1.0, MinY2Span = 1.0 };
            _sAct = Plot.AddSeries("Stab. input", PlotColors.Trace1);
            _sSet = Plot.AddSeries("Setpoint", PlotColors.SetpointTrace, false, 1.3f, PlotDash.Dash);
            _sAmp = Plot.AddSeries("Amp output", PlotColors.Trace3);
            _sSeed = Plot.AddSeries("Seed", PlotColors.Trace2, true);

            dev.Device.WatchMany(100, P.PsValueAct, P.PsSetpoint, P.AmpSeedPower, P.AmpOutputPower);
        }

        public override string Title => "Stabilization";
        public override string Subtitle => "출력 파워 안정화(Power Lock) · 외부 포토다이오드 보정 · 파워 추세";

        public PlotModel Plot { get; }

        public ToggleParamViewModel PsEnable { get; }
        public ReadoutParamViewModel PsState { get; }
        public ChoiceParamViewModel PsInput { get; }
        public ReadoutParamViewModel PsOutput { get; }
        public NumberParamViewModel PsSetpoint { get; }
        public ReadoutParamViewModel PsValue { get; }
        public NumberParamViewModel PsGainAll { get; }
        public NumberParamViewModel PsGainP { get; }
        public NumberParamViewModel PsGainI { get; }
        public NumberParamViewModel PsGainD { get; }
        public CheckParamViewModel PsSign { get; }
        public CheckParamViewModel PsHoldOnUnlock { get; }
        public CheckParamViewModel PsWindowEnabled { get; }
        public NumberParamViewModel PsWindowLow { get; }
        public NumberParamViewModel PsWindowHyst { get; }

        public ChoiceParamViewModel PdInput { get; }
        public ReadoutParamViewModel PdPhotodiode { get; }
        public ReadoutParamViewModel PdPower { get; }
        public NumberParamViewModel PdCalOffset { get; }
        public NumberParamViewModel PdCalFactor { get; }

        protected override void OnTick() {
            DlcDevice d = Dev.Device;
            if (!d.IsConnected) return;
            if ((_clock.Elapsed - _lastSample).TotalMilliseconds < 95) return;
            _lastSample = _clock.Elapsed;
            if (d.TryGetDouble(P.PsValueAct, out double v)) _tAct.Add(v);
            if (d.TryGetDouble(P.PsSetpoint, out v)) _tSet.Add(v);
            if (d.TryGetDouble(P.AmpSeedPower, out v)) _tSeed.Add(v);
            if (d.TryGetDouble(P.AmpOutputPower, out v)) _tAmp.Add(v);
        }

        protected override void OnActiveTick() {
            _tAct.CopyTo(_sAct);
            _tSet.CopyTo(_sSet);
            _tSeed.CopyTo(_sSeed);
            _tAmp.CopyTo(_sAmp);
            Plot.Invalidate();
        }

        [RelayCommand]
        private void ClearTrend() {
            _tAct.Clear();
            _tSet.Clear();
            _tSeed.Clear();
            _tAmp.Clear();
            Plot.ResetScale();
        }
    }
}
