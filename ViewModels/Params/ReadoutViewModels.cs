using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using DLC_PRO.Core;
using DLC_PRO.Models;
using DLC_PRO.Services;

namespace DLC_PRO.ViewModels.Params {
    /// <summary>읽기 전용 표시 행 (라벨 | 값 | 단위).</summary>
    public abstract partial class ReadoutRowViewModel : ParamRowViewModel {
        protected ReadoutRowViewModel(DeviceService dev, string label, string? unit) : base(dev, label) {
            _unit = unit ?? "";
        }

        [ObservableProperty]
        private string _text = "";

        [ObservableProperty]
        private string _unit;

        /// <summary>강조 상태 (예: 오류 문구) — 빨간 글자.</summary>
        [ObservableProperty]
        private bool _isAlert;
    }

    /// <summary>장비 파라미터 표시 (WinForms ParamReadout 대응).</summary>
    public sealed class ReadoutParamViewModel : ReadoutRowViewModel {
        private readonly Func<string, string> _format;

        public ReadoutParamViewModel(DeviceService dev, string name, string label, string? unit = null,
                                     Func<string, string>? format = null, int periodMs = 250)
            : base(dev, label, unit) {
            Name = name;
            ToolTip = name;
            _format = format ?? DecofValue.Display;
            dev.Device.Watch(name, periodMs);
        }

        public string Name { get; }

        /// <summary>단위를 장비에서 읽는 경우 (예: laser1:wide-scan:value-unit).</summary>
        public string? UnitParam { get; init; }

        /// <summary>값이 이 조건이면 강조 (선택).</summary>
        public Func<string, bool>? AlertWhen { get; init; }

        public override void Refresh() {
            DlcDevice d = Device;
            IsAvailable = d.IsConnected && !d.IsUnavailable(Name);
            if (UnitParam != null) {
                string? u = d.GetString(UnitParam);
                if (!string.IsNullOrEmpty(u)) Unit = u;
            }
            string t;
            bool alert = false;
            if (!d.IsConnected) t = "";
            else if (d.IsUnavailable(Name)) t = "n/a";
            else if (d.TryGetRaw(Name, out string raw)) {
                t = _format(raw);
                alert = AlertWhen != null && AlertWhen(raw);
            }
            else t = "…";
            Text = t;
            IsAlert = alert;
        }

        // 자주 쓰는 형식
        public static Func<string, string> Num(string fmt) => raw =>
            DecofValue.TryDouble(raw, out double v) ? v.ToString(fmt, CultureInfo.InvariantCulture) : DecofValue.Display(raw);

        public static string OnOff(string raw) =>
            DecofValue.TryBool(raw, out bool v) ? (v ? "ON" : "OFF") : DecofValue.Display(raw);

        public static string Channel(string raw) =>
            DecofValue.TryInt(raw, out int v) ? ChoiceItem.Channel(v).Text : DecofValue.Display(raw);
    }

    /// <summary>계산 값 표시 (함수 결과를 주기적으로 표시, WinForms StatusLabel 대응).</summary>
    public sealed class ComputedRowViewModel : ReadoutRowViewModel {
        private readonly Func<string> _func;

        public ComputedRowViewModel(DeviceService dev, string label, Func<string> func, string? unit = null) : base(dev, label, unit) {
            _func = func;
        }

        public override void Refresh() {
            IsAvailable = Device.IsConnected;
            Text = _func();
        }
    }
}
