using System.ComponentModel;
using Avalonia.Controls;
using DLC_PRO.Services;
using DLC_PRO.ViewModels;

namespace DLC_PRO.Views {
    public partial class MainView : UserControl {
        private LogService? _log;

        public MainView() {
            InitializeComponent();
        }

        protected override void OnDataContextChanged(System.EventArgs e) {
            base.OnDataContextChanged(e);
            if (_log != null) _log.PropertyChanged -= OnLogChanged;
            _log = (DataContext as MainViewModel)?.Log;
            if (_log != null) _log.PropertyChanged += OnLogChanged;
        }

        /// <summary>View 전용: 새 로그가 추가되면 목록 끝으로 스크롤.</summary>
        private void OnLogChanged(object? sender, PropertyChangedEventArgs e) {
            if (e.PropertyName != nameof(LogService.LastEntry) || _log?.LastEntry == null) return;
            ListBox? list = this.FindControl<ListBox>("LogList");
            if (list != null && list.IsVisible) list.ScrollIntoView(_log.LastEntry);
        }
    }
}
