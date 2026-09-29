using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLC_PRO.Core;
using DLC_PRO.Data;
using DLC_PRO.Models;
using DLC_PRO.Services;
using DLC_PRO.ViewModels.Params;

namespace DLC_PRO.ViewModels.Pages {
    /// <summary>System 페이지 — 장비 정보, 사용자 레벨(권한), 시스템 메시지.</summary>
    public partial class SystemPageViewModel : PageViewModel {
        private readonly LogService _log;

        public SystemPageViewModel(DeviceService dev, LogService log) : base(ApplicationPageNames.System, dev) {
            _log = log;
            Info = new List<ReadoutParamViewModel> {
                Track(new ReadoutParamViewModel(dev, P.SystemType, "System Type", null, null, 5000)),
                Track(new ReadoutParamViewModel(dev, P.SerialNumber, "Serial Number", null, null, 5000)),
                Track(new ReadoutParamViewModel(dev, P.FwVer, "Firmware", null, null, 5000)),
                Track(new ReadoutParamViewModel(dev, P.SswVer, "System Software", null, null, 5000)),
                Track(new ReadoutParamViewModel(dev, P.SystemModel, "System Model", null, null, 5000)),
                Track(new ReadoutParamViewModel(dev, P.LaserType, "Laser Type", null, null, 5000)),
                Track(new ReadoutParamViewModel(dev, P.LaserProductName, "Laser Head", null, null, 5000)),
                Track(new ReadoutParamViewModel(dev, P.DlSerial, "Master S/N", null, null, 5000)),
                Track(new ReadoutParamViewModel(dev, P.AmpSerial, "Amplifier S/N", null, null, 5000)),
                Track(new ReadoutParamViewModel(dev, P.NetIp, "IP Address", null, null, 5000)),
                Track(new ReadoutParamViewModel(dev, P.UptimeTxt, "Uptime", null, null, 1000)),
                Track(new ReadoutParamViewModel(dev, P.SystemHealthTxt, "System Health", null, null, 500) { AlertWhen = NotOk }),
                Track(new ReadoutParamViewModel(dev, P.LaserHealthTxt, "Laser Health", null, null, 500) { AlertWhen = NotOk }),
            };
            UserLevel = Track(new ReadoutParamViewModel(dev, P.UserLevel, "현재 레벨", null, raw => {
                if (!DecofValue.TryInt(raw, out int v)) return raw;
                return v switch {
                    4 => "4 read-only",
                    3 => "3 normal",
                    2 => "2 maintenance",
                    1 => "1 service",
                    _ => raw,
                };
            }, 1000));
            Levels = ChoiceItem.Enum("3=3 normal", "2=2 maintenance", "4=4 read-only");
            _selectedLevel = Levels[0];
        }

        private static bool NotOk(string raw) {
            string s = DecofValue.Str(raw) ?? "";
            return s.Trim().Length > 0 && !s.Trim().Equals("ok", StringComparison.OrdinalIgnoreCase);
        }

        public override string Title => "System";
        public override string Subtitle => "장비 정보 · 사용자 레벨 · 시스템 메시지";

        public IReadOnlyList<ReadoutParamViewModel> Info { get; }
        public ReadoutParamViewModel UserLevel { get; }
        public IReadOnlyList<ChoiceItem> Levels { get; }

        [ObservableProperty] private ChoiceItem _selectedLevel;
        [ObservableProperty] private string _password = "";
        [ObservableProperty] private string _messages = "";

        [RelayCommand]
        private async Task ChangeLevelAsync() {
            int level = SelectedLevel.Id;
            string pass = Password;
            Password = "";
            string? r = await Dev.ExecAsync(P.CmdChangeUl, level, pass);
            if (r == null) return;
            _log.Info("User level 변경 결과: " + DecofClient.LastLine(r));
            try { await Dev.Device.RefAsync(P.UserLevel); }
            catch (Exception ex) { Dev.ReportError(ex); }
        }

        [RelayCommand]
        private Task ShowNewAsync() => ShowMessagesAsync(P.CmdMsgShowNew);

        [RelayCommand]
        private Task ShowAllAsync() => ShowMessagesAsync(P.CmdMsgShowAll);

        [RelayCommand]
        private Task ShowLogAsync() => ShowMessagesAsync(P.CmdMsgShowLog);

        [RelayCommand]
        private Task ShowPersistentAsync() => ShowMessagesAsync(P.CmdMsgShowPersistent);

        private async Task ShowMessagesAsync(string cmd) {
            string? r = await Dev.ExecAsync(cmd);
            if (r == null) return;
            string text = r.Replace("\r", "");
            string trimmed = text.TrimEnd();
            if (trimmed.EndsWith("()", StringComparison.Ordinal)) text = trimmed.Substring(0, trimmed.Length - 2);
            Messages = "[" + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "] " + cmd + "\n" + text;
        }
    }
}
