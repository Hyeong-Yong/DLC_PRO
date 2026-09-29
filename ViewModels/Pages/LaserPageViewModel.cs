using System;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLC_PRO.Core;
using DLC_PRO.Data;
using DLC_PRO.Models;
using DLC_PRO.Services;
using DLC_PRO.ViewModels.Dialogs;
using DLC_PRO.ViewModels.Params;

namespace DLC_PRO.ViewModels.Pages {
    /// <summary>
    /// Laser 페이지 — TOPAS의 Laser Control 화면.
    /// 마스터(CC/TC/PC/SC) + 증폭기(CC/TC) + 파워 + 소프트웨어 안전 인터록.
    /// (WinForms LaserTab 로직 이식, MessageBox → DialogService)
    /// </summary>
    public partial class LaserPageViewModel : PageViewModel {
        private readonly DialogService _dialogs;
        private readonly LogService _log;
        private bool _layoutDone;

        public LaserPageViewModel(DeviceService dev, DialogService dialogs, LogService log) : base(ApplicationPageNames.Laser, dev) {
            _dialogs = dialogs;
            _log = log;

            // ---------------- CC - Master ----------------
            CcEnable = Track(new ToggleParamViewModel(dev, P.DlCcEnabled) { BeforeSet = MasterEnableGuard });
            CcEmission = Track(LedRowViewModel.FromBool(dev, "Emission", P.DlCcEmission, LedState.On));
            CcCurrentSet = Track(new NumberParamViewModel(dev, P.DlCcCurrentSet, "Set Current", "mA", 3, 0.1));
            CcCurrentAct = Track(new ReadoutParamViewModel(dev, P.DlCcCurrentAct, "Actual Current", "mA", ReadoutParamViewModel.Num("F2"), 200));
            CcCurrentClip = Track(new NumberParamViewModel(dev, P.DlCcCurrentClip, "Maximum Current", "mA", 3, 1.0) {
                AllowWheel = false,
                BeforeSet = v => ConfirmClip("마스터", v),
            });
            CcVoltage = Track(new ReadoutParamViewModel(dev, P.DlCcVoltageAct, "Voltage", "V", ReadoutParamViewModel.Num("F3"), 500));
            CcStatus = Track(new ReadoutParamViewModel(dev, P.DlCcStatusTxt, "Status", null, null, 1000));

            // ---------------- TC - Master ----------------
            TcEnable = Track(new ToggleParamViewModel(dev, P.DlTcEnabled));
            TcReadyLed = Track(LedRowViewModel.FromBool(dev, "Ready", P.DlTcReady, LedState.On, 500));
            TcTempSet = Track(new NumberParamViewModel(dev, P.DlTcTempSet, "Set Temperature", "°C", 3, 0.01));
            TcTempAct = Track(new ReadoutParamViewModel(dev, P.DlTcTempAct, "Actual Temperature", "°C", ReadoutParamViewModel.Num("F3"), 250));
            TcReady = Track(new ReadoutParamViewModel(dev, P.DlTcReady, "Ready", null,
                raw => DecofValue.TryBool(raw, out bool v) ? (v ? "settled" : "not settled") : raw, 500));
            TcStatus = Track(new ReadoutParamViewModel(dev, P.DlTcStatusTxt, "Status", null, null, 1000));

            // ---------------- PC - Master ----------------
            PcEnable = Track(new ToggleParamViewModel(dev, P.DlPcEnabled));
            PcVoltageSet = Track(new NumberParamViewModel(dev, P.DlPcVoltageSet, "Set Voltage", "V", 3, 0.1));
            PcVoltageAct = Track(new ReadoutParamViewModel(dev, P.DlPcVoltageAct, "Actual Voltage", "V", ReadoutParamViewModel.Num("F3"), 200));
            PcVoltageMin = Track(new ReadoutParamViewModel(dev, P.DlPcVoltageMin, "Minimum", "V", ReadoutParamViewModel.Num("F1"), 2000));
            PcVoltageMax = Track(new ReadoutParamViewModel(dev, P.DlPcVoltageMax, "Maximum", "V", ReadoutParamViewModel.Num("F1"), 2000));

            // ---------------- SC - Master (스캔 빠른 설정) ----------------
            ScanEnable = Track(new ToggleParamViewModel(dev, P.ScanEnabled));
            ScanAmplitude = Track(new NumberParamViewModel(dev, P.ScanAmplitude, "Scan Amplitude", "V", 3, 0.5) { UnitParam = P.ScanUnit });
            ScanOffset = Track(new NumberParamViewModel(dev, P.ScanOffset, "Scan Offset", "V", 3, 0.1) { UnitParam = P.ScanUnit });
            ScanFrequency = Track(new NumberParamViewModel(dev, P.ScanFrequency, "Scan Frequency", "Hz", 2, 1));
            dev.Device.Watch(P.ScanUnit, 1000);

            // ---------------- CC - Amplifier ----------------
            AmpEnable = Track(new ToggleParamViewModel(dev, P.AmpCcEnabled) { BeforeSet = AmpEnableGuard });
            AmpEmission = Track(LedRowViewModel.FromBool(dev, "Emission", P.AmpCcEmission, LedState.Warn));
            AmpCurrentSet = Track(new NumberParamViewModel(dev, P.AmpCcCurrentSet, "Set Current", "mA", 1, 10) {
                AllowWheel = false,       // 증폭기 전류는 휠로 바꾸지 않음
                CommitDelayMs = 600,      // 스핀 버튼 연타 시 마지막 값만 한 번 전송
                BeforeSet = ConfirmAmpCurrent,
                CustomSetter = RampAmpCurrent,
            });
            AmpCurrentAct = Track(new ReadoutParamViewModel(dev, P.AmpCcCurrentAct, "Actual Current", "mA", ReadoutParamViewModel.Num("F1"), 200));
            AmpCurrentClip = Track(new NumberParamViewModel(dev, P.AmpCcCurrentClip, "Maximum Current", "mA", 1, 10) {
                AllowWheel = false,
                BeforeSet = v => ConfirmClip("증폭기", v),
            });
            AmpStatus = Track(new ReadoutParamViewModel(dev, P.AmpCcStatusTxt, "Status", null, null, 1000));

            // ---------------- TC - Amplifier ----------------
            AmpTcEnable = Track(new ToggleParamViewModel(dev, P.AmpTcEnabled));
            AmpTcReadyLed = Track(LedRowViewModel.FromBool(dev, "Ready", P.AmpTcReady, LedState.On, 500));
            AmpTcTempSet = Track(new NumberParamViewModel(dev, P.AmpTcTempSet, "Set Temperature", "°C", 3, 0.01));
            AmpTcTempAct = Track(new ReadoutParamViewModel(dev, P.AmpTcTempAct, "Actual Temperature", "°C", ReadoutParamViewModel.Num("F3"), 250));

            // ---------------- Power ----------------
            SeedPower = Track(new ReadoutParamViewModel(dev, P.AmpSeedPower, "Actual Seed Power", "mW", ReadoutParamViewModel.Num("F2"), 150));
            OutputPower = Track(new ReadoutParamViewModel(dev, P.AmpOutputPower, "Actual Output Power", "mW", ReadoutParamViewModel.Num("F2"), 150));
            SeedPowerMin = Track(new ReadoutParamViewModel(dev, P.AmpSeedPowerMin, "Seed Power Min (장비)", "mW", ReadoutParamViewModel.Num("F2"), 2000));
            SeedLimits = Track(new ReadoutParamViewModel(dev, P.AmpSeedLimitsStatusTxt, "Seed Limits", null, null, 1000));
            OutputLimits = Track(new ReadoutParamViewModel(dev, P.AmpOutputLimitsStatusTxt, "Output Limits", null, null, 1000));
        }

        public override string Title => "Laser";
        public override string Subtitle => "마스터 레이저(CC/TC/PC/Scan)와 TA 증폭기 제어 · 소프트웨어 안전 인터록";

        // Master
        public ToggleParamViewModel CcEnable { get; }
        public LedRowViewModel CcEmission { get; }
        public NumberParamViewModel CcCurrentSet { get; }
        public ReadoutParamViewModel CcCurrentAct { get; }
        public NumberParamViewModel CcCurrentClip { get; }
        public ReadoutParamViewModel CcVoltage { get; }
        public ReadoutParamViewModel CcStatus { get; }

        public ToggleParamViewModel TcEnable { get; }
        public LedRowViewModel TcReadyLed { get; }
        public NumberParamViewModel TcTempSet { get; }
        public ReadoutParamViewModel TcTempAct { get; }
        public ReadoutParamViewModel TcReady { get; }
        public ReadoutParamViewModel TcStatus { get; }

        public ToggleParamViewModel PcEnable { get; }
        public NumberParamViewModel PcVoltageSet { get; }
        public ReadoutParamViewModel PcVoltageAct { get; }
        public ReadoutParamViewModel PcVoltageMin { get; }
        public ReadoutParamViewModel PcVoltageMax { get; }

        public ToggleParamViewModel ScanEnable { get; }
        public NumberParamViewModel ScanAmplitude { get; }
        public NumberParamViewModel ScanOffset { get; }
        public NumberParamViewModel ScanFrequency { get; }

        // Amplifier
        public ToggleParamViewModel AmpEnable { get; }
        public LedRowViewModel AmpEmission { get; }
        public NumberParamViewModel AmpCurrentSet { get; }
        public ReadoutParamViewModel AmpCurrentAct { get; }
        public NumberParamViewModel AmpCurrentClip { get; }
        public ReadoutParamViewModel AmpStatus { get; }

        public ToggleParamViewModel AmpTcEnable { get; }
        public LedRowViewModel AmpTcReadyLed { get; }
        public NumberParamViewModel AmpTcTempSet { get; }
        public ReadoutParamViewModel AmpTcTempAct { get; }

        public ReadoutParamViewModel SeedPower { get; }
        public ReadoutParamViewModel OutputPower { get; }
        public ReadoutParamViewModel SeedPowerMin { get; }
        public ReadoutParamViewModel SeedLimits { get; }
        public ReadoutParamViewModel OutputLimits { get; }

        /// <summary>증폭기(TA) 패널 표시 여부 (laser1:type에 TA/MOPA가 있을 때).</summary>
        [ObservableProperty]
        private bool _hasAmplifier = true;

        /// <summary>증폭기 전류 램프 진행 표시.</summary>
        [ObservableProperty]
        private string _rampText = "";

        /// <summary>안전 설정 요약.</summary>
        [ObservableProperty]
        private string _safetyInfo = "";

        protected override void OnActiveTick() {
            DlcDevice d = Dev.Device;
            // 증폭기가 없는 레이저(DL pro 등)면 증폭기 패널 숨김 (laser1:type 확인 후 1회)
            if (!_layoutDone && d.IsConnected && d.GetString(P.LaserType) != null) {
                HasAmplifier = Dev.Safety.HasAmplifier;
                _layoutDone = true;
            }
            if (!d.IsConnected) _layoutDone = false;

            AmpSafetySettings s = Dev.Safety.Settings;
            double max = Dev.Safety.EffectiveMaxCurrent;
            SafetyInfo = string.Format(CultureInfo.InvariantCulture,
                "최소 seed 파워: {0:F2} mW\n최대 증폭기 전류: {1}\n램프: {2:F0} mA / {3} ms, 확인창 ≥ {4:F0} mA\nseed 감시(Watchdog): {5}",
                Dev.Safety.EffectiveMinSeed,
                double.IsNaN(max) ? "(알 수 없음 — 전류 상승 차단)" : max.ToString("F0", CultureInfo.InvariantCulture) + " mA",
                s.RampStepMa, s.RampIntervalMs, s.ConfirmDeltaMa,
                s.WatchdogEnabled ? "ON (" + s.WatchdogDelayMs + " ms)" : "OFF");
            if (!Dev.Safety.Ramping && RampText.StartsWith("램프 중", StringComparison.Ordinal)) RampText = "";
        }

        // ------------------------------------------------------------------
        // 명령
        // ------------------------------------------------------------------

        [RelayCommand]
        private async Task AmpOffAsync() {
            try {
                await Dev.Safety.AmpOffAsync();
                _log.Warn("AMP OFF 버튼");
            }
            catch (Exception ex) { Dev.ReportError(ex); }
        }

        [RelayCommand]
        private async Task AllOffAsync() {
            if (!await _dialogs.ConfirmAsync("전체 OFF", "증폭기 → 마스터 순서로 전류를 끕니다. 계속할까요?", DialogKind.Warning)) return;
            try {
                await Dev.Safety.AllOffAsync();
                _log.Warn("전체 OFF (증폭기 → 마스터)");
            }
            catch (Exception ex) { Dev.ReportError(ex); }
        }

        // ------------------------------------------------------------------
        // 안전 가드 (WinForms LaserTab과 동일한 순서/조건)
        // ------------------------------------------------------------------

        private async Task<bool> MasterEnableGuard(bool target) {
            if (target) return true;
            // 마스터를 끌 때 증폭기가 켜져 있으면 먼저 증폭기를 끈다 (seed 없는 TA 보호)
            if (Dev.Device.TryGetBool(P.AmpCcEnabled, out bool ampOn) && ampOn) {
                if (!await _dialogs.ConfirmAsync("안전 순서", "증폭기가 켜져 있습니다.\n증폭기를 먼저 끈 뒤 마스터를 끕니다.", DialogKind.Warning))
                    return false;
                try { await Dev.Safety.AllOffAsync(); }
                catch (Exception ex) {
                    Dev.ReportError("증폭기 OFF 실패 — 마스터 OFF 취소: " + DeviceService.Unwrap(ex).Message);
                    return false;
                }
                return false;
            }
            try { await Dev.Safety.AllOffAsync(); }
            catch (Exception ex) { Dev.ReportError(ex); }
            return false;
        }

        private async Task<bool> AmpEnableGuard(bool target) {
            if (!target) {
                try { await Dev.Safety.AmpOffAsync(); }
                catch (Exception ex) { Dev.ReportError(ex); }
                return false;
            }
            long version = Dev.Safety.OperationVersion;
            long session = Dev.Device.SessionVersion;
            string? reason = Dev.Safety.CheckEnableAmp();
            if (reason != null) {
                await _dialogs.AlertAsync("안전 인터록", "증폭기를 켤 수 없습니다.\n\n" + reason, DialogKind.Danger);
                return false;
            }
            double set = Dev.Device.GetDouble(P.AmpCcCurrentSet, 0);
            double max = Dev.Safety.EffectiveMaxCurrent;
            if (set > max) {
                await _dialogs.AlertAsync("안전 인터록", string.Format(CultureInfo.InvariantCulture,
                    "설정 전류 {0:F1} mA가 최대 허용 전류 {1:F1} mA보다 큽니다.\n설정 전류를 먼저 낮추세요.", set, max), DialogKind.Danger);
                return false;
            }
            bool ok = await _dialogs.ConfirmAsync("증폭기 ON 확인", string.Format(CultureInfo.InvariantCulture,
                "증폭기 전류를 켭니다.\n설정 전류: {0:F1} mA ({1:F0} mA에서 시작해 단계적으로 상승)\nseed 파워: {2:F2} mW\n\n계속할까요?",
                set, Math.Min(set, Dev.Safety.Settings.RampStepMa), Dev.Device.GetDouble(P.AmpSeedPower, 0)), DialogKind.Warning, "증폭기 ON");
            if (!ok) return false;
            if (version != Dev.Safety.OperationVersion || session != Dev.Device.SessionVersion) return false;
            // 확인창을 보는 동안 상태가 바뀌었을 수 있으므로 EnableAmpAsync 안에서 다시 검사한다
            Progress<double> prog = new Progress<double>(v =>
                RampText = string.Format(CultureInfo.InvariantCulture, "램프 중… {0:F0} → {1:F0} mA", v, set));
            try {
                await Dev.Safety.EnableAmpAsync(prog);
                RampText = "";
            }
            catch (OperationCanceledException) { RampText = "램프 취소됨"; }
            catch (Exception ex) {
                RampText = "증폭기 ON 중단";
                Dev.ReportError(DeviceService.Unwrap(ex).Message);
            }
            return false; // 켜기는 위에서 직접 수행했으므로 토글 자체는 쓰지 않음
        }

        private Task<bool> ConfirmClip(string which, double v) =>
            _dialogs.ConfirmAsync("최대 전류 변경", string.Format(CultureInfo.InvariantCulture,
                "{0} 최대 전류(current-clip)를 {1:F1} mA로 바꿉니다.\n레이저/증폭기 사양 범위 안의 값인지 확인하세요.\n\n계속할까요?", which, v),
                DialogKind.Warning, "변경");

        private async Task<bool> ConfirmAmpCurrent(double target) {
            double max = Dev.Safety.EffectiveMaxCurrent;
            double cur0 = Dev.Device.GetDouble(P.AmpCcCurrentSet, double.NaN);
            if (double.IsNaN(max) && !(target <= cur0)) {
                await _dialogs.AlertAsync("안전 인터록", "최대 허용 전류(current-clip)를 읽을 수 없어 전류를 올릴 수 없습니다.", DialogKind.Danger);
                return false;
            }
            if (target > max) {
                await _dialogs.AlertAsync("안전 인터록", string.Format(CultureInfo.InvariantCulture, "최대 허용 전류({0:F0} mA)를 초과합니다.", max), DialogKind.Danger);
                return false;
            }
            double cur = Dev.Device.GetDouble(P.AmpCcCurrentSet, target);
            if (target - cur >= Dev.Safety.Settings.ConfirmDeltaMa && Dev.Device.GetBool(P.AmpCcEnabled, false)) {
                return await _dialogs.ConfirmAsync("증폭기 전류 확인", string.Format(CultureInfo.InvariantCulture,
                    "증폭기 전류를 {0:F1} → {1:F1} mA로 올립니다.\n({2:F0} mA 단위로 단계적 상승)\n\n계속할까요?",
                    cur, target, Dev.Safety.Settings.RampStepMa), DialogKind.Question);
            }
            return true;
        }

        private async Task RampAmpCurrent(double target) {
            Progress<double> prog = new Progress<double>(v =>
                RampText = string.Format(CultureInfo.InvariantCulture, "램프 중… {0:F0} → {1:F0} mA", v, target));
            try {
                await Dev.Safety.SetAmpCurrentAsync(target, prog);
                RampText = "";
            }
            catch (OperationCanceledException) { RampText = "램프 취소됨"; }
            catch (Exception ex) {
                RampText = "램프 중단";
                Dev.ReportError(DeviceService.Unwrap(ex).Message);
            }
        }
    }
}
