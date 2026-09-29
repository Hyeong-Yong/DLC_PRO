using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DLC_PRO.ViewModels.Dialogs {
    /// <summary>
    /// 메인 화면 위에 겹쳐 표시되는 대화상자의 기반 (BatchProcess3 DialogViewModel과 같은 구조).
    /// Show() → View가 IsDialogOpen을 보고 표시, Close() → WaitAsync() 완료.
    /// </summary>
    public partial class DialogViewModel : ViewModelBase {
        [ObservableProperty]
        private bool _isDialogOpen;

        protected TaskCompletionSource CloseTask = new TaskCompletionSource();

        public Task WaitAsync() => CloseTask.Task;

        public void Show() {
            if (CloseTask.Task.IsCompleted) CloseTask = new TaskCompletionSource();
            IsDialogOpen = true;
        }

        public void Close() {
            IsDialogOpen = false;
            CloseTask.TrySetResult();
        }
    }
}
