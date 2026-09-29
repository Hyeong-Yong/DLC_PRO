using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DLC_PRO.ViewModels.Dialogs {
    /// <summary>
    /// 세 가지 선택 대화상자 (예: 종료 시 "그대로 두고 종료 / 증폭기 OFF 후 종료 / 취소").
    /// </summary>
    public partial class ChoiceDialogViewModel : DialogViewModel {
        [ObservableProperty] private string _title = "선택";
        [ObservableProperty] private string _message = "";
        [ObservableProperty] private string _firstText = "예";
        [ObservableProperty] private string _secondText = "아니오";
        [ObservableProperty] private string _cancelText = "취소";
        [ObservableProperty] private DialogKind _kind = DialogKind.Warning;

        /// <summary>true = 첫 번째, false = 두 번째, null = 취소.</summary>
        public bool? Result { get; private set; }

        public string IconText => Kind == DialogKind.Danger ? "" : "";

        [RelayCommand]
        private void First() {
            Result = true;
            Close();
        }

        [RelayCommand]
        private void Second() {
            Result = false;
            Close();
        }

        [RelayCommand]
        private void Cancel() {
            Result = null;
            Close();
        }
    }
}
