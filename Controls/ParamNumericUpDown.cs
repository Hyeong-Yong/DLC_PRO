using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace DLC_PRO.Controls {
    /// <summary>
    /// 장비 파라미터 입력용 NumericUpDown.
    /// 기본 NumericUpDown은 글자를 칠 때마다 Value가 바뀌므로(예: "150" 입력 중 1 → 15 → 150),
    /// 그대로 바인딩하면 중간 값이 장비(레이저 전류 등)에 쓰일 수 있다.
    /// 이 컨트롤은 확정된 값만 <see cref="CommittedValue"/>로 내보낸다:
    ///   - 스핀 버튼/화살표 키/(포커스 상태의) 마우스 휠 → 즉시 확정
    ///   - 직접 입력 → Enter 또는 포커스 이동 시 확정, Esc는 취소
    /// ViewModel이 CommittedValue를 바꾸면(장비 값 갱신) 사용자가 입력 중이 아닐 때만 화면에 반영된다.
    /// </summary>
    public class ParamNumericUpDown : NumericUpDown {
        public static readonly StyledProperty<double?> CommittedValueProperty =
            AvaloniaProperty.Register<ParamNumericUpDown, double?>(nameof(CommittedValue), defaultBindingMode: BindingMode.TwoWay);

        public static readonly StyledProperty<bool> AllowWheelProperty =
            AvaloniaProperty.Register<ParamNumericUpDown, bool>(nameof(AllowWheel), true);

        /// <summary>확정된 값 (ViewModel과 양방향 바인딩).</summary>
        public double? CommittedValue {
            get => GetValue(CommittedValueProperty);
            set => SetValue(CommittedValueProperty, value);
        }

        /// <summary>false면 마우스 휠로 값을 바꾸지 않는다 (증폭기 전류 등).</summary>
        public bool AllowWheel {
            get => GetValue(AllowWheelProperty);
            set => SetValue(AllowWheelProperty, value);
        }

        private bool _sync;            // 코드에서 Value/CommittedValue를 바꾸는 중
        private string? _pushedText;   // 마지막으로 장비 값을 표시했을 때의 문자열 (사용자 입력 여부 판단)

        protected override Type StyleKeyOverride => typeof(NumericUpDown);

        public ParamNumericUpDown() {
            // 스타일 선택자는 StyleKey(NumericUpDown)로 매칭되므로 클래스로 구분 (Styles/DlcPro.axaml의 NumericUpDown.param)
            Classes.Add("param");
            // 휠 차단은 내부 ButtonSpinner보다 먼저 받아야 하므로 터널링 단계에서 처리
            AddHandler(PointerWheelChangedEvent, OnWheelTunnel, RoutingStrategies.Tunnel);
        }

        /// <summary>포커스가 있고 표시 문자열이 장비 값과 달라졌으면 사용자가 입력 중.</summary>
        private bool IsUserEditing => IsKeyboardFocusWithin && Text != _pushedText;

        private void OnWheelTunnel(object? sender, PointerWheelEventArgs e) {
            // 휠 금지면 값 변경 안 함 (포커스가 없으면 스피너도 반응하지 않으므로 부모 ScrollViewer가 스크롤)
            if (!AllowWheel && IsKeyboardFocusWithin) e.Handled = true;
        }

        protected override void OnSpin(SpinEventArgs e) {
            // 값이 없을 때 스핀하면 NumericUpDown은 Minimum/Maximum으로 점프한다 → 장비에 쓰면 위험하므로 무시
            if (!Value.HasValue || !CommittedValue.HasValue) {
                e.Handled = true;
                return;
            }
            base.OnSpin(e);
            Commit();   // 스핀 버튼/화살표 키/휠 → 즉시 확정
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            if (e.Key == Key.Escape && IsUserEditing) {
                PushToControl(CommittedValue);   // 입력 취소
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);                    // Enter → 문자열을 Value로 변환
            if (e.Key == Key.Enter) CommitTyped();
        }

        protected override void OnLostFocus(FocusChangedEventArgs e) {
            base.OnLostFocus(e);                  // 문자열 → Value 확정
            CommitTyped();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (_sync) return;
            // 장비 값 갱신: 사용자가 입력 중이면 덮어쓰지 않음 (확정 또는 Esc 후 반영)
            if (change.Property == CommittedValueProperty && !IsUserEditing) PushToControl(CommittedValue);
        }

        private void PushToControl(double? v) {
            _sync = true;
            try {
                decimal? d = null;
                if (v.HasValue && !double.IsNaN(v.Value) && !double.IsInfinity(v.Value)) {
                    try {
                        decimal x = (decimal)v.Value;
                        if (x < Minimum) x = Minimum;
                        if (x > Maximum) x = Maximum;
                        d = x;
                    }
                    catch (OverflowException) { d = null; }
                }
                SetCurrentValue(ValueProperty, d);
                _pushedText = Text;
            }
            finally { _sync = false; }
        }

        /// <summary>직접 입력(Enter/포커스 이동) 확정. 문자열을 바꾸지 않았으면 아무것도 쓰지 않는다.</summary>
        private void CommitTyped() {
            if (Text == _pushedText) {
                // 표시 문자열 재해석으로 Value가 반올림되었을 수 있으므로 원래 값으로 되돌림 (장비에 쓰지 않음)
                PushToControl(CommittedValue);
                return;
            }
            Commit();
        }

        private void Commit() {
            decimal? v = Value;
            if (!v.HasValue) {
                PushToControl(CommittedValue);    // 빈 칸/잘못된 입력 → 원래 값으로
                return;
            }
            double d = (double)v.Value;
            _pushedText = Text;
            if (CommittedValue.HasValue && Math.Abs(CommittedValue.Value - d) < 1e-12) return;
            _sync = true;
            try { SetCurrentValue(CommittedValueProperty, d); }
            finally { _sync = false; }
        }
    }
}
