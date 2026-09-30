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
        private bool _refreshingConnectionSettings;

        public SettingsPageViewModel(DeviceService dev, LogService log) : base(ApplicationPageNames.Settings, dev) {
            _log = log;
            LoadSafety();
            _logTraffic = dev.ConnectionSettings.LogTraffic;
            _scopeMaxRate = (decimal)Math.Max(1, Math.Min(30, dev.ConnectionSettings.ScopeMaxRate));
            _cmdPort = dev.ConnectionSettings.CmdPort;
            _monPort = dev.ConnectionSettings.MonPort;
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
            if (_refreshingConnectionSettings) return;
            Dev.ConnectionSettings.LogTraffic = value;
            Dev.Device.LogTraffic = value;
            Dev.ConnectionSettings.Save();
        }

        partial void OnScopeMaxRateChanged(decimal? value) {
            if (_refreshingConnectionSettings) return;
            if (!value.HasValue) return;
            double v = Math.Max(1, Math.Min(30, (double)value.Value));
            Dev.ConnectionSettings.ScopeMaxRate = v;
            Dev.Device.MaxScopeRate = v;
            Dev.ConnectionSettings.Save();
        }

        partial void OnCmdPortChanged(decimal? value) {
            if (_refreshingConnectionSettings) return;
            if (value is >= 1 and <= 65535) {
                Dev.ConnectionSettings.CmdPort = (int)value.Value;
                Dev.ConnectionSettings.Save();
            }
        }

        partial void OnMonPortChanged(decimal? value) {
            if (_refreshingConnectionSettings) return;
            if (value is >= 1 and <= 65535) {
                Dev.ConnectionSettings.MonPort = (int)value.Value;
                Dev.ConnectionSettings.Save();
            }
        }

        protected override void OnActiveTick() {
            // 다른 레이저 화면에서 변경한 공통 통신 설정도 반영한다.
            var common = Dev.ConnectionSettings;
            _refreshingConnectionSettings = true;
            try {
                LogTraffic = common.LogTraffic;
                ScopeMaxRate = (decimal)common.ScopeMaxRate;
                CmdPort = common.CmdPort;
                MonPort = common.MonPort;
            }
            finally { _refreshingConnectionSettings = false; }
            DlcDevice d = Dev.Device;
            MonitorLineText = !d.IsConnected ? "-" : d.MonitorAvailable ? "사용 중 (" + Dev.ConnectionSettings.MonPort + ")" : "없음 → 폴링";
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
