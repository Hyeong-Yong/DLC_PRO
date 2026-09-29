using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using DLC_PRO.ViewModels.Pages;

namespace DLC_PRO.Views.Pages {
    public partial class ConsolePageView : UserControl {
        private ConsolePageViewModel? _vm;

        public ConsolePageView() {
            InitializeComponent();
        }

        protected override void OnDataContextChanged(System.EventArgs e) {
            base.OnDataContextChanged(e);
            if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
            _vm = DataContext as ConsolePageViewModel;
            if (_vm != null) _vm.PropertyChanged += OnVmChanged;
        }

        /// <summary>View 전용: 출력이 추가되면 끝으로 스크롤.</summary>
        private void OnVmChanged(object? sender, PropertyChangedEventArgs e) {
            if (e.PropertyName != nameof(ConsolePageViewModel.OutputVersion)) return;
            TextBox? box = this.FindControl<TextBox>("Output");
            if (box == null) return;
            Dispatcher.UIThread.Post(() => box.CaretIndex = box.Text?.Length ?? 0, DispatcherPriority.Background);
        }
    }
}
