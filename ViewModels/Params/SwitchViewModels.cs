using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLC_PRO.Core;
using DLC_PRO.Models;
using DLC_PRO.Services;

namespace DLC_PRO.ViewModels.Params {
    /// <summary>
    /// Enable 토글 버튼 (TOPAS의 "Enable ●", WinForms ParamToggle 대응).
    /// 상태를 모르면 누를 수 없고, 진행 중 작업(예: 증폭기 램프)이 있으면 끄기만 가능.
    /// </summary>
    public partial class ToggleParamViewModel : DeviceParamViewModel {
        private bool _busy;

        public ToggleParamViewModel(DeviceService dev, string name, string onText = "Enable", string? offText = null, int periodMs = 200)
            : base(dev, name, onText, periodMs) {
            OnText = onText;
            OffText = offText ?? onText;
            _buttonText = "○ " + OffText;
        }

        public string OnText { get; }
        public string OffText { get; }

        /// <summary>쓰기 전 확인 (target 값). false면 취소.</summary>
        public Func<bool, Task<bool>>? BeforeSet { get; set; }

        [ObservableProperty]
        private bool _isOn;

        [ObservableProperty]
        private bool _isKnown;

        [ObservableProperty]
        private string _buttonText;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ToggleCommand))]
        private bool _canToggle;

        public override void Refresh() {
            DlcDevice d = Device;
            bool known = d.TryGetBool(Name, out bool v) && d.IsConnected;
            IsKnown = known;
            IsOn = known && v;
            IsAvailable = ComputeAvailable();
            // 현재 상태를 모르면 누를 수 없게 (모르는 상태에서 반대로 켜는 것 방지)
            // 진행 중이라도 켜져 있으면 끄기는 항상 가능
            CanToggle = known && IsAvailable && (!_busy || v);
            ButtonText = (IsOn ? "● " + OnText : "○ " + OffText);
        }

        // AllowConcurrentExecutions: 증폭기 ON 램프(BeforeSet 안에서 실행) 중에도 다시 눌러 끌 수 있어야 한다.
        // 동시 실행은 _busy로 제어 (진행 중에는 OFF만 허용).
        [RelayCommand(CanExecute = nameof(CanToggle), AllowConcurrentExecutions = true)]
        private async Task ToggleAsync() {
            if (!Device.TryGetBool(Name, out bool cur)) return;
            bool target = !cur;
            if (_busy) {
                // 진행 중인 작업이 있을 때는 끄기만 허용 (BeforeSet(false)로 램프 취소 후 OFF)
                if (target) return;
                try { if (BeforeSet != null && !await BeforeSet(false)) return; }
                catch (Exception ex) { Dev.ReportError(ex); return; }
                await Dev.SetAsync(Name, false);
                return;
            }
            _busy = true;
            Refresh();
            try {
                long session = Device.SessionVersion;
                if (BeforeSet != null && !await BeforeSet(target)) return;
                if (session != Device.SessionVersion) return;
                await Dev.SetAsync(Name, target);
            }
            catch (Exception ex) { Dev.ReportError(ex); }
            finally {
                _busy = false;
                Refresh();
            }
        }
    }

    /// <summary>불리언 옵션 체크박스 (WinForms ParamCheck 대응).</summary>
    public partial class CheckParamViewModel : DeviceParamViewModel {
        private bool? _isChecked;
        private bool _refreshing;

        public CheckParamViewModel(DeviceService dev, string name, string text, int periodMs = 300)
            : base(dev, name, text, periodMs) {
        }

        public bool? IsChecked {
            get => _isChecked;
            set {
                if (_isChecked == value) return;
                SetProperty(ref _isChecked, value);
                if (_refreshing || !value.HasValue) return;
                _ = WriteAsync(value.Value);
            }
        }

        private async Task WriteAsync(bool v) {
            await Dev.SetAsync(Name, v);
            Refresh();
        }

        public override void Refresh() {
            IsAvailable = ComputeAvailable();
            bool? v = Device.IsConnected && Device.TryGetBool(Name, out bool b) ? b : null;
            _refreshing = true;
            try { IsChecked = v; }
            finally { _refreshing = false; }
        }
    }

    /// <summary>정수 열거/신호 채널 콤보 (WinForms ParamCombo 대응).</summary>
    public partial class ChoiceParamViewModel : DeviceParamViewModel {
        private ChoiceItem? _selected;
        private bool _refreshing;

        public ChoiceParamViewModel(DeviceService dev, string name, string label, IEnumerable<ChoiceItem> items, int periodMs = 300)
            : base(dev, name, label, periodMs) {
            Items = new ObservableCollection<ChoiceItem>(items);
        }

        public static ChoiceParamViewModel Channels(DeviceService dev, string name, string label, IEnumerable<int> ids) =>
            new ChoiceParamViewModel(dev, name, label, ChoiceItem.Channels(ids));

        public static ChoiceParamViewModel Enum(DeviceService dev, string name, string label, params string[] idTextPairs) =>
            new ChoiceParamViewModel(dev, name, label, ChoiceItem.Enum(idTextPairs));

        public ObservableCollection<ChoiceItem> Items { get; }

        /// <summary>선택 항목. 사용자가 바꾸면 장비에 쓴다.</summary>
        public ChoiceItem? SelectedItem {
            get => _selected;
            set {
                if (ReferenceEquals(_selected, value)) return;
                if (value == null && !_refreshing) {
                    OnPropertyChanged();   // 목록 변경 등으로 인한 null 선택은 무시
                    return;
                }
                SetProperty(ref _selected, value);
                if (_refreshing || value == null) return;
                _ = WriteAsync(value.Id);
            }
        }

        private async Task WriteAsync(int id) {
            await Dev.SetAsync(Name, id);
            Refresh();
        }

        public override void Refresh() {
            IsAvailable = ComputeAvailable();
            if (!Device.IsConnected || !Device.TryGetInt(Name, out int v)) {
                if (!Device.IsConnected) SetSelectedSilently(null);
                return;
            }
            if (_selected != null && _selected.Id == v) return;
            ChoiceItem? found = null;
            foreach (ChoiceItem it in Items) {
                if (it.Id == v) {
                    found = it;
                    break;
                }
            }
            if (found == null) {
                // 목록에 없는 값 → 채널 이름으로 추가해서 표시
                found = ChoiceItem.Channel(v);
                Items.Add(found);
            }
            SetSelectedSilently(found);
        }

        private void SetSelectedSilently(ChoiceItem? item) {
            _refreshing = true;
            try { SelectedItem = item; }
            finally { _refreshing = false; }
        }
    }

    /// <summary>표시등 행 (라벨 + LED).</summary>
    public partial class LedRowViewModel : ParamRowViewModel {
        private readonly Func<DlcDevice, LedState> _state;

        public LedRowViewModel(DeviceService dev, string label, Func<DlcDevice, LedState> state) : base(dev, label) {
            _state = state;
        }

        /// <summary>불리언 파라미터가 true면 onState.</summary>
        public static LedRowViewModel FromBool(DeviceService dev, string label, string param, LedState onState, int periodMs = 200) {
            dev.Device.Watch(param, periodMs);
            return new LedRowViewModel(dev, label, d => d.IsConnected && d.TryGetBool(param, out bool v) && v ? onState : LedState.Off) {
                ToolTip = param,
            };
        }

        [ObservableProperty]
        private LedState _led;

        public override void Refresh() {
            IsAvailable = Device.IsConnected;
            Led = _state(Device);
        }
    }
}
