using Avalonia.Controls;
using DLC_PRO.ViewModels;

namespace DLC_PRO.Views {
    public partial class MainWindow : Window {
        private bool _closeApproved;
        private bool _closePending;

        public MainWindow() {
            InitializeComponent();
        }

        /// <summary>
        /// 종료 확인 (View 연결 코드만): 판단은 MainViewModel.ConfirmCloseAsync가 한다.
        /// 증폭기가 켜져 있으면 대화상자를 띄우기 위해 일단 닫기를 취소하고, 승인되면 다시 닫는다.
        /// </summary>
        protected override async void OnClosing(WindowClosingEventArgs e) {
            base.OnClosing(e);
            if (_closeApproved || DataContext is not MainViewModel vm) return;
            e.Cancel = true;
            if (_closePending) return;
            _closePending = true;
            try {
                if (await vm.ConfirmCloseAsync()) {
                    _closeApproved = true;
                    Close();
                }
            }
            finally { _closePending = false; }
        }
    }
}
