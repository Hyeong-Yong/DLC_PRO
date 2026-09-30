using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using DLC_PRO.Interfaces;
using DLC_PRO.ViewModels.Wavemeter;
using DLC_PRO.Views;

namespace DLC_PRO.Services {
    /// <summary>
    /// 보조 창 서비스. LongTerm 창은 메인 창과 독립된 최상위 창이다
    /// (소유 창을 지정하지 않으므로 메인 창 옆/다른 모니터에 자유롭게 배치하고 따로 최소화할 수 있다).
    /// 메인 창을 닫으면 앱 종료와 함께 닫힌다 (App: ShutdownMode.OnMainWindowClose).
    /// </summary>
    public sealed class WindowService : IWindowService {
        private LongTermWindow? _longTerm;

        public bool IsLongTermOpen => _longTerm != null;

        public void ShowLongTerm(LongTermViewModel vm) {
            if (_longTerm != null) {
                if (_longTerm.WindowState == WindowState.Minimized) _longTerm.WindowState = WindowState.Normal;
                _longTerm.Activate();
                return;
            }
            LongTermWindow w = new LongTermWindow { DataContext = vm };
            (double x, double y, double width, double height) = vm.WindowBounds;
            w.Width = width;
            w.Height = height;
            if (!double.IsNaN(x) && !double.IsNaN(y) && IsOnScreen(w, x, y, width)) {
                w.WindowStartupLocation = WindowStartupLocation.Manual;
                w.Position = new PixelPoint((int)x, (int)y);
            }
            vm.SaveFilePicker = name => PickSaveAsync(w, name);
            w.Closed += (_, _) => {
                vm.SaveWindowBounds(w.Position.X, w.Position.Y, w.Bounds.Width > 0 ? w.Bounds.Width : w.Width, w.Bounds.Height > 0 ? w.Bounds.Height : w.Height);
                vm.IsWindowOpen = false;
                vm.SaveFilePicker = null;
                _longTerm = null;
            };
            _longTerm = w;
            vm.IsWindowOpen = true;
            w.Show();
            vm.ForceRefresh();
        }

        public void CloseLongTerm() => _longTerm?.Close();

        /// <summary>저장된 위치가 지금 연결된 모니터 안에 있는지 (모니터 구성이 바뀌면 화면 밖에 열리지 않도록).</summary>
        private static bool IsOnScreen(Window w, double x, double y, double width) {
            try {
                Screens? screens = w.Screens;
                if (screens == null || screens.ScreenCount == 0) return true;
                foreach (Screen s in screens.All) {
                    PixelRect r = s.WorkingArea;
                    if (x + Math.Min(width, 200) > r.X && x < r.Right - 50 && y >= r.Y - 10 && y < r.Bottom - 50) return true;
                }
                return false;
            }
            catch {
                return true;
            }
        }

        private static async Task<string?> PickSaveAsync(Window owner, string suggestedName) {
            IStorageFile? file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
                Title = "LongTerm 데이터 저장",
                SuggestedFileName = suggestedName,
                FileTypeChoices = new[] {
                    new FilePickerFileType("탭 구분 텍스트 (.lta.txt)") { Patterns = new[] { "*.txt", "*.lta" } },
                },
            });
            return file?.TryGetLocalPath();
        }
    }
}
