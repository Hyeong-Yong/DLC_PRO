using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLC_PRO.Core;
using DLC_PRO.Data;
using DLC_PRO.Models;
using DLC_PRO.Services;
using DLC_PRO.ViewModels.Params;

namespace DLC_PRO.ViewModels.Pages {
    /// <summary>
    /// ReLock 페이지 — 자동 재잠금(ReLock), 락 윈도우(out-of-lock 감지), PID 리셋 + 락 이벤트 기록.
    /// 이벤트 기록은 페이지가 보이지 않아도 계속된다 (OnTick).
    /// </summary>
    public partial class RelockPageViewModel : PageViewModel {
        private static readonly string[] StateNames = { "Idle", "Scanning", "Selecting", "Selected", "Locking", "Locked", "On Hold", "Resetting", "Reset", "Relocking" };

        private readonly LogService _log;
        private readonly DialogService _dialogs;
        private readonly DateTime _sessionStart = DateTime.Now;
        private int _lastState = -1;
        private DateTime? _lockedSince;
        private TimeSpan _lockedTotal = TimeSpan.Zero;
        private int _unlockCount, _relockCount;

        public RelockPageViewModel(DeviceService dev, LogService log, DialogService dialogs) : base(ApplicationPageNames.Relock, dev) {
            _log = log;
            _dialogs = dialogs;

            RelockEnable = Track(new ToggleParamViewModel(dev, P.RelockEnabled));
            RelockOutput = Track(ChoiceParamViewModel.Channels(dev, P.RelockOutput, "Output Channel", SignalChannels.Outputs));
            RelockFrequency = Track(new NumberParamViewModel(dev, P.RelockFrequency, "Frequency", "Hz", 3, 0.1));
            RelockAmplitude = Track(new NumberParamViewModel(dev, P.RelockAmplitude, "Amplitude", "V", 3, 0.1));
            RelockDelay = Track(new NumberParamViewModel(dev, P.RelockDelay, "Delay", "s", 3, 0.01));

            WindowEnable = Track(new ToggleParamViewModel(dev, P.WindowEnabled));
            WindowInput = Track(ChoiceParamViewModel.Channels(dev, P.WindowInput, "Input Channel", SignalChannels.Inputs));
            WindowHigh = Track(new NumberParamViewModel(dev, P.WindowHigh, "Level High", "V", 4, 0.01));
            WindowLow = Track(new NumberParamViewModel(dev, P.WindowLow, "Level Low", "V", 4, 0.01));
            WindowHysteresis = Track(new NumberParamViewModel(dev, P.WindowHysteresis, "Hysteresis", "V", 4, 0.001));

            ResetEnable = Track(new ToggleParamViewModel(dev, P.ResetEnabled));
            Pid1InLock = Track(LedRowViewModel.FromBool(dev, "PID1 In-Lock", P.Pid(1, P.PidLockState), LedState.On));
            Pid1Regulating = Track(LedRowViewModel.FromBool(dev, "PID1 Regulating", P.Pid(1, P.PidRegulatingState), LedState.On));
            Pid2InLock = Track(LedRowViewModel.FromBool(dev, "PID2 In-Lock", P.Pid(2, P.PidLockState), LedState.On));
            Pid2Regulating = Track(LedRowViewModel.FromBool(dev, "PID2 Regulating", P.Pid(2, P.PidRegulatingState), LedState.On));
            LockStateRow = Track(new ReadoutParamViewModel(dev, P.LockStateTxt, "Lock State", null, null, 200));

            dev.Device.Watch(P.LockState, 100);
            SessionStartText = _sessionStart.ToString("HH:mm:ss");
        }

        public override string Title => "ReLock";
        public override string Subtitle => "자동 재잠금 · 락 윈도우(out-of-lock 감지) · PID 리셋 · 락 이벤트 기록";

        public ToggleParamViewModel RelockEnable { get; }
        public ChoiceParamViewModel RelockOutput { get; }
        public NumberParamViewModel RelockFrequency { get; }
        public NumberParamViewModel RelockAmplitude { get; }
        public NumberParamViewModel RelockDelay { get; }

        public ToggleParamViewModel WindowEnable { get; }
        public ChoiceParamViewModel WindowInput { get; }
        public NumberParamViewModel WindowHigh { get; }
        public NumberParamViewModel WindowLow { get; }
        public NumberParamViewModel WindowHysteresis { get; }

        public ToggleParamViewModel ResetEnable { get; }
        public LedRowViewModel Pid1InLock { get; }
        public LedRowViewModel Pid1Regulating { get; }
        public LedRowViewModel Pid2InLock { get; }
        public LedRowViewModel Pid2Regulating { get; }
        public ReadoutParamViewModel LockStateRow { get; }

        /// <summary>락 이벤트 (최신이 위).</summary>
        public ObservableCollection<LockEvent> Events { get; } = new ObservableCollection<LockEvent>();

        public string SessionStartText { get; }
        [ObservableProperty] private string _currentLockText = "-";
        [ObservableProperty] private string _totalLockText = "00:00:00";
        [ObservableProperty] private int _unlocks;
        [ObservableProperty] private int _relocks;

        private static string N(int s) => s >= 0 && s < StateNames.Length ? StateNames[s] : s.ToString(CultureInfo.InvariantCulture);

        private static string Fmt(TimeSpan t) =>
            ((int)t.TotalHours).ToString("00") + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00");

        /// <summary>페이지가 보이지 않아도 락 상태 변화를 기록한다.</summary>
        protected override void OnTick() {
            DlcDevice d = Dev.Device;
            if (d.IsConnected && d.TryGetInt(P.LockState, out int st) && st != _lastState) {
                if (_lastState >= 0) {
                    string note = "";
                    if (_lastState == (int)LockStateCode.Locked && st != (int)LockStateCode.Locked) {
                        _unlockCount++;
                        if (_lockedSince.HasValue) {
                            TimeSpan dur = DateTime.Now - _lockedSince.Value;
                            _lockedTotal += dur;
                            note = "락 유지 시간 " + Fmt(dur);
                        }
                        _lockedSince = null;
                        if (st == (int)LockStateCode.Relocking) note += " / ReLock 시작";
                    }
                    if (st == (int)LockStateCode.Locked) {
                        _lockedSince = DateTime.Now;
                        if (_lastState == (int)LockStateCode.Relocking) {
                            _relockCount++;
                            note = "ReLock 성공";
                        }
                    }
                    LockEventKind kind = st == (int)LockStateCode.Locked ? LockEventKind.Locked
                        : _lastState == (int)LockStateCode.Locked ? LockEventKind.Unlocked : LockEventKind.Neutral;
                    Events.Insert(0, new LockEvent(DateTime.Now, N(_lastState), N(st), note, kind));
                    if (Events.Count > 2000) Events.RemoveAt(Events.Count - 1);
                    if (kind == LockEventKind.Unlocked) _log.Warn("락 해제 감지: " + N(_lastState) + " → " + N(st));
                }
                else if (st == (int)LockStateCode.Locked) _lockedSince = DateTime.Now;
                _lastState = st;
            }
            if (!d.IsConnected) {
                // 연결이 끊기면 진행 중이던 락 유지 시간을 누적하고 타이머를 멈춘다
                if (_lockedSince.HasValue) {
                    _lockedTotal += DateTime.Now - _lockedSince.Value;
                    _lockedSince = null;
                }
                _lastState = -1;
            }
            if (!IsActive) return;
            TimeSpan cur = _lockedSince.HasValue ? DateTime.Now - _lockedSince.Value : TimeSpan.Zero;
            CurrentLockText = _lockedSince.HasValue ? Fmt(cur) : "-";
            TotalLockText = Fmt(_lockedTotal + cur);
            Unlocks = _unlockCount;
            Relocks = _relockCount;
        }

        [RelayCommand]
        private void ClearEvents() {
            Events.Clear();
            _unlockCount = _relockCount = 0;
            _lockedTotal = TimeSpan.Zero;
            if (_lockedSince.HasValue) _lockedSince = DateTime.Now;
        }

        [RelayCommand]
        private async Task SaveEventsAsync() {
            string? path = await _dialogs.SaveFilePickerAsync("락 이벤트 CSV 저장", "lock_events_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv", "CSV", "*.csv");
            if (path == null) return;
            StringBuilder sb = new StringBuilder("time,from,to,note\r\n");
            for (int i = Events.Count - 1; i >= 0; i--) {
                LockEvent e = Events[i];
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0},{1},{2},\"{3}\"\r\n", e.TimeText, e.From, e.To, e.Note.Replace("\"", "'"));
            }
            try {
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                _log.Info("저장: " + path);
            }
            catch (Exception ex) { Dev.ReportError("CSV 저장 실패: " + ex.Message); }
        }
    }
}
