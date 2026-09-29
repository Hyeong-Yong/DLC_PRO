using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace DLC_PRO.Views.Dialogs {
    public partial class ChoiceDialogView : UserControl {
        public ChoiceDialogView() {
            InitializeComponent();
        }

        /// <summary>
        /// View 전용: 대화상자가 뜨면 키보드 포커스를 대화상자 안(취소 버튼 우선)으로 옮긴다.
        /// 뒤쪽 페이지의 입력란에 포커스가 남아 방향키/스페이스가 장비 값을 바꾸는 것을 막기 위함.
        /// </summary>
        protected override void OnLoaded(RoutedEventArgs e) {
            base.OnLoaded(e);
            Dispatcher.UIThread.Post(() => {
                Button? cancel = this.FindControl<Button>("CancelButton");
                Button? confirm = this.FindControl<Button>("ConfirmButton");
                if (cancel != null && cancel.IsVisible) cancel.Focus();
                else confirm?.Focus();
            });
        }
    }
}
