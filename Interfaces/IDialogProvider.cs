using DLC_PRO.ViewModels.Dialogs;

namespace DLC_PRO.Interfaces {
    /// <summary>대화상자를 표시할 수 있는 ViewModel (MainViewModel).</summary>
    public interface IDialogProvider {
        DialogViewModel? Dialog { get; set; }
    }
}
