using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DLC_PRO.ViewModels.Dialogs {
    /// <summary>대화상자 종류 (아이콘/버튼 색).</summary>
    public enum DialogKind { Question, Warning, Danger, Info }

    /// <summary>
    /// 확인/취소 대화상자 (BatchProcess3 ConfirmDialogViewModel 기반).
    /// 증폭기 ON, 최대 전류 변경, 연결 해제 등 WinForms 버전의 MessageBox를 대신한다.
    /// </summary>
    public partial class ConfirmDialogViewModel : DialogViewModel {
        [ObservableProperty] private string _title = "확인";
        [ObservableProperty] private string _message = "계속할까요?";
        [ObservableProperty] private string _confirmText = "확인";
        [ObservableProperty] private string _cancelText = "취소";
        [ObservableProperty] private bool _showCancelButton = true;
        [ObservableProperty] private string _statusText = "";
        [ObservableProperty] private string _progressText = "";
        [ObservableProperty] private double _dialogWidth = 520;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IconText), nameof(IsDanger), nameof(IsWarning), nameof(IsQuestion))]
        private DialogKind _kind = DialogKind.Question;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
        private bool _busy;

        [ObservableProperty]
        private bool _confirmed;

        /// <summary>Phosphor 아이콘: warning / x-circle / question / info</summary>
        public string IconText => Kind switch {
            DialogKind.Warning => "",
            DialogKind.Danger => "",
            DialogKind.Info => "",
            _ => "",
        };

        public bool IsDanger => Kind == DialogKind.Danger;
        public bool IsWarning => Kind == DialogKind.Warning;
        public bool IsQuestion => Kind == DialogKind.Question || Kind == DialogKind.Info;

        public bool NotBusy() => !Busy;

        /// <summary>
        /// 확인 버튼을 눌렀을 때 실행할 작업 (false 반환 시 대화상자 유지).
        /// 주의: 이 안에서 다른 대화상자를 띄우면 안 된다 (DialogService는 한 번에 하나만 표시 → 교착).
        /// </summary>
        public Func<ConfirmDialogViewModel, Task<bool>> OnConfirm { get; set; } = _ => Task.FromResult(true);

        [RelayCommand]
        private async Task ConfirmAsync() {
            if (Busy) return;
            Busy = true;
            StatusText = "";
            ProgressText = "처리 중...";
            bool result;
            try { result = await OnConfirm(this); }
            catch (Exception ex) {
                StatusText = ex.Message;
                result = false;
            }
            finally { Busy = false; }
            if (!result) return;
            Confirmed = true;
            Close();
        }

        [RelayCommand(CanExecute = nameof(NotBusy))]
        private void Cancel() {
            Confirmed = false;
            Close();
        }
    }
}
