using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DLC_PRO.Services;

namespace DLC_PRO.ViewModels.Params {
    /// <summary>
    /// 숫자 입력 행 (WinForms ParamNumeric 대응).
    /// View: ParamNumericUpDown.CommittedValue ⇄ <see cref="Value"/> (확정된 값만 들어옴).
    /// 사용자 확정 → (BeforeSet 확인) → 장비 쓰기(또는 CustomSetter) → 다음 갱신 때 장비 실제 값 표시.
    /// </summary>
    public partial class NumberParamViewModel : DeviceParamViewModel {
        private double? _value;
        private bool _refreshing;
        private int _inFlight;            // 진행 중인 쓰기/확인 수 → 장비 값으로 덮어쓰지 않음
        private DispatcherTimer? _debounce;
        private double _debounceValue;
        private long _editSession, _safetyVersion;
        private int _editVersion;

        public NumberParamViewModel(DeviceService dev, string name, string label, string? unit = null,
                                    int decimals = 3, double increment = 0.1, int periodMs = 250)
            : base(dev, name, label, periodMs) {
            _unit = unit ?? "";
            Decimals = decimals;
            Increment = (decimal)increment;
            FormatString = "F" + decimals.ToString(CultureInfo.InvariantCulture);
        }

        public int Decimals { get; }
        public decimal Increment { get; }
        public string FormatString { get; }
        public decimal Minimum { get; init; } = -1000000000m;
        public decimal Maximum { get; init; } = 1000000000m;

        /// <summary>false면 마우스 휠로 값 변경 금지 (전류 제한값 등).</summary>
        public bool AllowWheel { get; init; } = true;

        /// <summary>0보다 크면 마지막 변경 후 이 시간(ms)이 지나야 쓴다 (스핀 버튼 연타 시 1번만 전송).</summary>
        public int CommitDelayMs { get; init; }

        /// <summary>단위를 장비에서 읽는 경우 (예: laser1:scan:unit).</summary>
        public string? UnitParam { get; init; }

        /// <summary>쓰기 전 확인. false면 취소 (안전 확인 대화상자 등).</summary>
        public Func<double, Task<bool>>? BeforeSet { get; set; }

        /// <summary>기본 SetAsync 대신 사용할 쓰기 함수 (예: 증폭기 전류 램프).</summary>
        public Func<double, Task>? CustomSetter { get; set; }

        [ObservableProperty]
        private string _unit;

        /// <summary>표시 값. View에서 확정된 값이 들어오면 장비에 쓴다.</summary>
        public double? Value {
            get => _value;
            set {
                if (Nullable.Equals(_value, value)) return;
                if (_refreshing) {
                    SetProperty(ref _value, value);
                    return;
                }
                if (!value.HasValue) {
                    OnPropertyChanged();   // 빈 값은 무시 → 기존 값 유지
                    return;
                }
                SetProperty(ref _value, value);
                OnUserValue(value.Value);
            }
        }

        public override void Refresh() {
            // 값을 아직 모르면 입력 불가 (빈 칸에서 스핀하면 Minimum/Maximum으로 점프하는 것 방지)
            IsAvailable = ComputeAvailable() && Device.TryGetDouble(Name, out _);
            if (UnitParam != null) {
                string? u = Device.GetString(UnitParam);
                if (!string.IsNullOrEmpty(u) && u != Unit) Unit = u;
            }
            // 사용자 값이 대기(디바운스) 중이거나 쓰는 중이면 덮어쓰지 않음
            if (_inFlight > 0 || _debounce?.IsEnabled == true) return;
            double? v = Device.TryGetDouble(Name, out double d) ? d : null;
            if (!Device.IsConnected) v = null;
            _refreshing = true;
            try { Value = v; }
            finally { _refreshing = false; }
        }

        private void OnUserValue(double v) {
            _editSession = Device.SessionVersion;
            _safetyVersion = Dev.Safety.OperationVersion;
            _editVersion++;
            if (CommitDelayMs > 0) {
                _debounceValue = v;
                if (_debounce == null) {
                    _debounce = new DispatcherTimer();
                    _debounce.Tick += (_, _) => {
                        _debounce!.Stop();
                        _ = CommitAsync(_debounceValue);
                    };
                }
                _debounce.Stop();
                _debounce.Interval = TimeSpan.FromMilliseconds(CommitDelayMs);
                _debounce.Start();
                return;
            }
            _ = CommitAsync(v);
        }

        private async Task CommitAsync(double v) {
            long session = _editSession, safety = _safetyVersion;
            int edit = _editVersion;
            _inFlight++;
            try {
                if (!Device.IsConnected) {
                    Dev.ReportError("장비에 연결되어 있지 않습니다.");
                    return;
                }
                if (BeforeSet != null && !await BeforeSet(v)) return;
                if (session != Device.SessionVersion || safety != Dev.Safety.OperationVersion || edit != _editVersion) return;
                if (CustomSetter != null) await CustomSetter(v);
                else await Device.SetAsync(Name, v);
            }
            catch (Exception ex) {
                Dev.ReportError(Name + " 설정 실패: " + DeviceService.Unwrap(ex).Message);
            }
            finally {
                _inFlight--;
                Refresh();   // 장비의 실제 값(clip 등 반영)으로 되돌림 (다른 쓰기/대기 중이면 건너뜀)
            }
        }
    }
}
