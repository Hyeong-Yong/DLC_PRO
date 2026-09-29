using System;
using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLC_PRO.Core;
using DLC_PRO.Data;
using DLC_PRO.Models;
using DLC_PRO.Services;

namespace DLC_PRO.ViewModels.Pages {
    /// <summary>
    /// Settings 페이지 — TA 증폭기 소프트웨어 안전 설정, 통신 설정, 프로그램 정보.
    /// 안전 설정은 [적용 및 저장]을 눌러야 반영된다 (입력 중 값이 즉시 쓰이지 않도록).
    /// </summary>
    public partial class SettingsPageViewModel : PageViewModel {
        private readonly LogService _log;

        public SettingsPageViewModel(DeviceService dev, LogService log) : base(ApplicationPageNames.Settings, dev) {
            _log = log;
            LoadSafety();
            _logTraffic = dev.Settings.LogTraffic;
            _scopeMaxRate = (decimal)Math.Max(1, Math.Min(30, dev.Settings.ScopeMaxRate));
            _cmdPort = dev.Settings.CmdPort;
            _monPort = dev.Settings.MonPort;
            Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0";
        }

        public override string Title => "Settings";
        public override string Subtitle => "TA 안전 인터록 · 통신 · 프로그램 정보";

        public string Version { get; }
        public string SettingsFolder => AppSettings.Folder;

        // ---------------- 안전 설정 ----------------
        [ObservableProperty] private decimal? _minSeedPowerMw;
        [ObservableProperty] private decimal? _maxAmpCurrentMa;
        [ObservableProperty] private decimal? _rampStepMa;
        [ObservableProperty] private decimal? _rampIntervalMs;
        [ObservableProperty] private decimal? _confirmDeltaMa;
        [ObservableProperty] private bool _watchdogEnabled;
        [ObservableProperty] private decimal? _watchdogDelayMs;
        [ObservableProperty] private bool _safetyDirty;

        partial void OnMinSeedPowerMwChanged(decimal? value) => SafetyDirty = true;
        partial void OnMaxAmpCurrentMaChanged(decimal? value) => SafetyDirty = true;
        partial void OnRampStepMaChanged(decimal? value) => SafetyDirty = true;
        partial void OnRampIntervalMsChanged(decimal? value) => SafetyDirty = true;
        partial void OnConfirmDeltaMaChanged(decimal? value) => SafetyDirty = true;
        partial void OnWatchdogEnabledChanged(bool value) => SafetyDirty = true;
        partial void OnWatchdogDelayMsChanged(decimal? value) => SafetyDirty = true;

        private void LoadSafety() {
            AmpSafetySettings s = Dev.Settings.Safety;
            MinSeedPowerMw = (decimal)s.MinSeedPowerMw;
            MaxAmpCurrentMa = (decimal)s.MaxAmpCurrentMa;
            RampStepMa = (decimal)s.RampStepMa;
            RampIntervalMs = s.RampIntervalMs;
            ConfirmDeltaMa = (decimal)s.ConfirmDeltaMa;
            WatchdogEnabled = s.WatchdogEnabled;
            WatchdogDelayMs = s.WatchdogDelayMs;
            SafetyDirty = false;
        }

        [RelayCommand]
        private void ApplySafety() {
            AmpSafetySettings s = Dev.Settings.Safety;
            s.MinSeedPowerMw = (double)(MinSeedPowerMw ?? 0);
            s.MaxAmpCurrentMa = (double)(MaxAmpCurrentMa ?? 0);
            s.RampStepMa = (double)(RampStepMa ?? 100);
            s.RampIntervalMs = (int)(RampIntervalMs ?? 200);
            s.ConfirmDeltaMa = (double)(ConfirmDeltaMa ?? 300);
            s.WatchdogEnabled = WatchdogEnabled;
            s.WatchdogDelayMs = (int)(WatchdogDelayMs ?? 300);
            s.Sanitize();
            Dev.SaveSettings();
            LoadSafety();   // Sanitize 결과를 화면에 반영
            _log.Info("안전 설정 저장됨");
            _log.SetStatus("안전 설정 저장됨");
        }

        [RelayCommand]
        private void RevertSafety() => LoadSafety();

        // ---------------- 통신 ----------------
        [ObservableProperty] private bool _logTraffic;
        [ObservableProperty] private decimal? _scopeMaxRate;
        [ObservableProperty] private decimal? _cmdPort;
        [ObservableProperty] private decimal? _monPort;
        [ObservableProperty] private string _monitorLineText = "-";

        partial void OnLogTrafficChanged(bool value) {
            Dev.Settings.LogTraffic = value;
            Dev.Device.LogTraffic = value;
            Dev.SaveSettings();
        }

        partial void OnScopeMaxRateChanged(decimal? value) {
            if (!value.HasValue) return;
            double v = Math.Max(1, Math.Min(30, (double)value.Value));
            Dev.Settings.ScopeMaxRate = v;
            Dev.Device.MaxScopeRate = v;
            Dev.SaveSettings();
        }

        partial void OnCmdPortChanged(decimal? value) {
            if (value is >= 1 and <= 65535) {
                Dev.Settings.CmdPort = (int)value.Value;
                Dev.SaveSettings();
            }
        }

        partial void OnMonPortChanged(decimal? value) {
            if (value is >= 1 and <= 65535) {
                Dev.Settings.MonPort = (int)value.Value;
                Dev.SaveSettings();
            }
        }

        protected override void OnActiveTick() {
            DlcDevice d = Dev.Device;
            MonitorLineText = !d.IsConnected ? "-" : d.MonitorAvailable ? "사용 중 (" + Dev.Settings.MonPort + ")" : "없음 → 폴링";
        }

        [RelayCommand]
        private void OpenLogFolder() => OpenFolder(AppSettings.LogFolder);

        [RelayCommand]
        private void OpenSettingsFolder() => OpenFolder(AppSettings.Folder);

        private void OpenFolder(string path) {
            try {
                System.IO.Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (Exception ex) { Dev.ReportError("폴더 열기 실패: " + ex.Message); }
        }
    }
}
