using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLC_PRO.Core;
using DLC_PRO.Data;
using DLC_PRO.Factories;
using DLC_PRO.Interfaces;
using DLC_PRO.Models;
using DLC_PRO.Services;
using DLC_PRO.ViewModels.Dialogs;
using DLC_PRO.ViewModels.Pages;

namespace DLC_PRO.ViewModels {
    /// <summary>
    /// 메인 화면 ViewModel (BatchProcess3 MainViewModel 구조).
    /// - 사이드 메뉴 페이지 전환 (PageFactory)
    /// - 상단 연결/상태 바 (연결, Emission/Interlock/Lock 표시, AMP OFF / ALL OFF)
    /// - 하단 로그
    /// - 대화상자 호스트 (IDialogProvider)
    /// </summary>
    public partial class MainViewModel : ViewModelBase, IDialogProvider {
        private readonly PageFactory _pageFactory;
        private readonly DeviceService _dev;
        private readonly DialogService _dialogs;
        private readonly LogService _log;
        private bool _tripDialogOpen;
        private bool _closingConfirmed;

        public MainViewModel(PageFactory pageFactory, DeviceService dev, DialogService dialogs, LogService log) {
            _pageFactory = pageFactory;
            _dev = dev;
            _dialogs = dialogs;
            _log = log;

            _sideMenuExpanded = dev.Settings.SideMenuExpanded;
            _isUsb = dev.Settings.ConnectionType == "USB";
            _host = dev.Settings.Host;
            _comPort = dev.Settings.ComPort;
            RefreshPorts();

            // 모든 페이지를 미리 만든다: 락 이벤트 기록, 파워 추세, 와이드스캔 자동 읽기 등은
            // 페이지가 보이지 않아도 동작해야 하기 때문 (WinForms 버전의 모든 탭과 동일)
            _pageFactory.GetPageViewModel<ScanLockPageViewModel>();
            _pageFactory.GetPageViewModel<RelockPageViewModel>();
            _pageFactory.GetPageViewModel<StabilizationPageViewModel>();
            _pageFactory.GetPageViewModel<WideScanPageViewModel>();
            _pageFactory.GetPageViewModel<RecorderPageViewModel>();
            _pageFactory.GetPageViewModel<SystemPageViewModel>();
            _pageFactory.GetPageViewModel<ConsolePageViewModel>();
            _pageFactory.GetPageViewModel<SettingsPageViewModel>();
            _currentPage = _pageFactory.GetPageViewModel<LaserPageViewModel>();
            _currentPage.IsActive = true;

            dev.Tick += OnTick;
            dev.SafetyTripped += OnSafetyTripped;
        }

        // ------------------------------------------------------------------
        // 페이지 / 사이드 메뉴
        // ------------------------------------------------------------------

        [ObservableProperty]
        private bool _sideMenuExpanded;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LaserPageIsActive), nameof(ScanLockPageIsActive), nameof(RelockPageIsActive),
            nameof(StabilizationPageIsActive), nameof(WideScanPageIsActive), nameof(RecorderPageIsActive),
            nameof(SystemPageIsActive), nameof(ConsolePageIsActive), nameof(SettingsPageIsActive))]
        private PageViewModel _currentPage;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsDialogOpen))]
        private DialogViewModel? _dialog;

        public bool IsDialogOpen => Dialog?.IsDialogOpen == true;

        public bool LaserPageIsActive => CurrentPage.PageName == ApplicationPageNames.Laser;
        public bool ScanLockPageIsActive => CurrentPage.PageName == ApplicationPageNames.ScanLock;
        public bool RelockPageIsActive => CurrentPage.PageName == ApplicationPageNames.Relock;
        public bool StabilizationPageIsActive => CurrentPage.PageName == ApplicationPageNames.Stabilization;
        public bool WideScanPageIsActive => CurrentPage.PageName == ApplicationPageNames.WideScan;
        public bool RecorderPageIsActive => CurrentPage.PageName == ApplicationPageNames.Recorder;
        public bool SystemPageIsActive => CurrentPage.PageName == ApplicationPageNames.System;
        public bool ConsolePageIsActive => CurrentPage.PageName == ApplicationPageNames.Console;
        public bool SettingsPageIsActive => CurrentPage.PageName == ApplicationPageNames.Settings;

        partial void OnCurrentPageChanging(PageViewModel value) {
            if (_currentPage != null) _currentPage.IsActive = false;
        }

        partial void OnCurrentPageChanged(PageViewModel value) => value.IsActive = true;

        partial void OnDialogChanged(DialogViewModel? oldValue, DialogViewModel? newValue) {
            if (oldValue != null) oldValue.PropertyChanged -= OnDialogPropertyChanged;
            if (newValue != null) newValue.PropertyChanged += OnDialogPropertyChanged;
        }

        private void OnDialogPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(DialogViewModel.IsDialogOpen)) {
                OnPropertyChanged(nameof(IsDialogOpen));
                OnPropertyChanged(nameof(CanEditConnection));
            }
        }

        partial void OnSideMenuExpandedChanged(bool value) {
            _dev.Settings.SideMenuExpanded = value;
            _dev.SaveSettings();
        }

        [RelayCommand] private void SideMenuResize() => SideMenuExpanded = !SideMenuExpanded;
        [RelayCommand] private void GoToLaser() => CurrentPage = _pageFactory.GetPageViewModel<LaserPageViewModel>();
        [RelayCommand] private void GoToScanLock() => CurrentPage = _pageFactory.GetPageViewModel<ScanLockPageViewModel>();
        [RelayCommand] private void GoToRelock() => CurrentPage = _pageFactory.GetPageViewModel<RelockPageViewModel>();
        [RelayCommand] private void GoToStabilization() => CurrentPage = _pageFactory.GetPageViewModel<StabilizationPageViewModel>();
        [RelayCommand] private void GoToWideScan() => CurrentPage = _pageFactory.GetPageViewModel<WideScanPageViewModel>();
        [RelayCommand] private void GoToRecorder() => CurrentPage = _pageFactory.GetPageViewModel<RecorderPageViewModel>();
        [RelayCommand] private void GoToSystem() => CurrentPage = _pageFactory.GetPageViewModel<SystemPageViewModel>();
        [RelayCommand] private void GoToConsole() => CurrentPage = _pageFactory.GetPageViewModel<ConsolePageViewModel>();
        [RelayCommand] private void GoToSettings() => CurrentPage = _pageFactory.GetPageViewModel<SettingsPageViewModel>();

        /// <summary>명령줄 --page 이름으로 시작 페이지 선택.</summary>
        public void NavigateByName(string name) {
            foreach (ApplicationPageNames p in Enum.GetValues<ApplicationPageNames>()) {
                if (!p.ToString().StartsWith(name.Replace("&", "").Replace(" ", ""), StringComparison.OrdinalIgnoreCase)) continue;
                switch (p) {
                    case ApplicationPageNames.Laser: GoToLaser(); return;
                    case ApplicationPageNames.ScanLock: GoToScanLock(); return;
                    case ApplicationPageNames.Relock: GoToRelock(); return;
                    case ApplicationPageNames.Stabilization: GoToStabilization(); return;
                    case ApplicationPageNames.WideScan: GoToWideScan(); return;
                    case ApplicationPageNames.Recorder: GoToRecorder(); return;
                    case ApplicationPageNames.System: GoToSystem(); return;
                    case ApplicationPageNames.Console: GoToConsole(); return;
                    case ApplicationPageNames.Settings: GoToSettings(); return;
                }
            }
        }

        // ------------------------------------------------------------------
        // 연결 바
        // ------------------------------------------------------------------

        public IReadOnlyList<string> ConnectionTypes { get; } = new[] { "TCP/IP", "USB (COM)" };

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsTcp), nameof(ConnectionTypeIndex))]
        private bool _isUsb;

        public bool IsTcp => !IsUsb;

        public int ConnectionTypeIndex {
            get => IsUsb ? 1 : 0;
            set => IsUsb = value == 1;
        }

        [ObservableProperty] private string _host;
        [ObservableProperty] private string _comPort;
        public ObservableCollection<string> ComPorts { get; } = new ObservableCollection<string>();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ConnectButtonText), nameof(CanEditConnection))]
        [NotifyCanExecuteChangedFor(nameof(ToggleConnectCommand))]
        private bool _isConnecting;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ConnectButtonText), nameof(CanEditConnection), nameof(ConnectIcon))]
        private bool _isConnected;

        public string ConnectButtonText => IsConnecting ? "연결 중..." : IsConnected ? "연결 해제" : "연결";
        /// <summary>plugs-connected / plugs</summary>
        public string ConnectIcon => IsConnected ? "" : "";
        public bool CanEditConnection => !IsConnected && !IsConnecting && !IsDialogOpen;
        public bool CanToggleConnect() => !IsConnecting;

        [RelayCommand]
        private void RefreshPorts() {
            string keep = ComPort;
            ComPorts.Clear();
            try {
                foreach (string p in SerialPort.GetPortNames()) ComPorts.Add(p);
            }
            catch {
                // 포트 목록을 읽을 수 없는 환경
            }
            if (!string.IsNullOrEmpty(keep) && !ComPorts.Contains(keep)) ComPorts.Insert(0, keep);
            ComPort = keep;
        }

        [RelayCommand(CanExecute = nameof(CanToggleConnect))]
        private async Task ToggleConnectAsync() {
            DlcDevice d = _dev.Device;
            if (d.IsConnected) {
                if (d.GetBool(P.AmpCcEnabled, false) || d.GetBool(P.LockEnabled, false)) {
                    if (!await _dialogs.ConfirmAsync("연결 해제", "연결을 끊어도 장비는 현재 상태(전류, 락 등)를 유지합니다.\n연결을 끊을까요?"))
                        return;
                }
                IsConnecting = true;
                try { await _dev.DisconnectAsync(); }
                catch (Exception ex) { _log.Error("연결 해제 오류: " + ex.Message); }
                finally { IsConnecting = false; }
                return;
            }
            IsConnecting = true;
            bool usb = IsUsb;
            string host = (Host ?? "").Trim(), com = (ComPort ?? "").Trim();
            try {
                if (usb) await _dev.ConnectSerialAsync(com);
                else await _dev.ConnectTcpAsync(host);
                _dev.Settings.ConnectionType = usb ? "USB" : "TCP";
                if (usb) _dev.Settings.ComPort = com;
                else _dev.Settings.Host = host;
                _dev.SaveSettings();
            }
            catch (Exception ex) {
                string msg = DeviceService.Unwrap(ex).Message;
                _log.Error("연결 실패: " + msg);
                try { await _dev.DisconnectAsync(); }
                catch {
                    // 정리 실패 무시
                }
                IsConnecting = false;
                string tips = usb
                    ? "\n\n- USB 케이블 / COM 포트 번호 확인 (장치 관리자)\n- TOPAS 등 다른 프로그램이 같은 COM 포트를 쓰고 있지 않은지 확인"
                    : "\n\n- IP 주소 / 네트워크(같은 서브넷) 확인\n- TOPAS 등 다른 프로그램이 연결 수(최대 8)를 모두 쓰고 있지 않은지 확인";
                await _dialogs.AlertAsync("연결 실패", msg + tips, DialogKind.Danger);
            }
            finally {
                IsConnecting = false;
            }
        }

        /// <summary>명령줄 --connect IP로 시작 시 자동 연결.</summary>
        public async Task AutoConnectAsync(string host) {
            IsUsb = false;
            Host = host;
            await ToggleConnectAsync();
        }

        // ------------------------------------------------------------------
        // 상태 표시
        // ------------------------------------------------------------------

        [ObservableProperty] private LedState _connectionLed;
        [ObservableProperty] private LedState _emissionLed;
        [ObservableProperty] private LedState _interlockLed;
        [ObservableProperty] private LedState _lockLed;
        [ObservableProperty] private string _lockText = "—";
        [ObservableProperty] private string _healthText = "";
        [ObservableProperty] private bool _healthIsBad;
        [ObservableProperty] private string _endpointText = "연결 안 됨";
        [ObservableProperty] private string _userLevelText = "UL -";
        [ObservableProperty] private string _messagesText = "";
        [ObservableProperty] private string _windowTitle = "DLC pro Control";

        public LogService Log => _log;

        [ObservableProperty] private bool _logExpanded = true;

        private void OnTick() {
            DlcDevice d = _dev.Device;
            bool con = d.IsConnected;
            IsConnected = con;
            ConnectionLed = con ? LedState.On : LedState.Off;
            EndpointText = con ? ("연결: " + d.Endpoint + (d.MonitorAvailable ? " (monitor)" : " (polling)")) : "연결 안 됨";

            EmissionLed = con && d.TryGetBool(P.Emission, out bool em) && em ? LedState.Emission : LedState.Off;
            InterlockLed = con && d.TryGetBool(P.InterlockOpen, out bool il) ? (il ? LedState.Error : LedState.On) : LedState.Off;
            int st = d.GetInt(P.LockState, -1);
            LockLed = st == (int)LockStateCode.Locked ? LedState.On
                : st == (int)LockStateCode.Relocking || st == (int)LockStateCode.Locking ? LedState.Warn
                : LedState.Off;
            LockText = con ? (d.GetString(P.LockStateTxt) ?? "—") : "—";
            string? health = d.GetString(P.SystemHealthTxt);
            HealthText = con && health != null ? "Health: " + health : "";
            HealthIsBad = health != null && health.Trim().ToLowerInvariant() != "ok";
            UserLevelText = "UL " + (con ? d.GetInt(P.UserLevel, -1).ToString() : "-");
            int nm = d.GetInt(P.MsgCountNew, 0);
            MessagesText = con && nm > 0 ? "새 시스템 메시지 " + nm + "개" : "";
            WindowTitle = "DLC pro Control" + (con ? " — " + (d.GetString(P.LaserType) ?? "") + " @ " + d.Endpoint : "");
        }

        private async void OnSafetyTripped(string reason) {
            if (_tripDialogOpen) return;
            _tripDialogOpen = true;
            try {
                await _dialogs.AlertAsync("안전 차단", "소프트웨어 안전 인터록이 작동했습니다.\n\n" + reason, DialogKind.Danger);
            }
            finally { _tripDialogOpen = false; }
        }

        // ------------------------------------------------------------------
        // 비상 버튼 (대화상자가 떠 있어도 누를 수 있도록 오버레이 밖에 배치)
        // ------------------------------------------------------------------

        [RelayCommand]
        private async Task AmpOffAsync() {
            try {
                await _dev.Safety.AmpOffAsync();
                _log.Warn("AMP OFF 버튼");
            }
            catch (Exception ex) { _dev.ReportError(ex); }
        }

        [RelayCommand]
        private async Task AllOffAsync() {
            try {
                await _dev.Safety.AllOffAsync();
                _log.Warn("ALL OFF 버튼 (증폭기 → 마스터)");
            }
            catch (Exception ex) { _dev.ReportError(ex); }
        }

        // ------------------------------------------------------------------
        // 로그
        // ------------------------------------------------------------------

        [RelayCommand] private void ClearLog() => _log.Clear();
        [RelayCommand] private void ToggleLog() => LogExpanded = !LogExpanded;

        // ------------------------------------------------------------------
        // 종료
        // ------------------------------------------------------------------

        /// <summary>
        /// 창을 닫아도 되는지 확인 (MainWindow.Closing에서 호출).
        /// 증폭기가 켜져 있으면 [그대로 두고 종료 / 증폭기 OFF 후 종료 / 취소]를 묻는다.
        /// </summary>
        public async Task<bool> ConfirmCloseAsync() {
            if (_closingConfirmed) return true;
            if (IsConnecting) { _log.SetStatus("연결 작업이 끝난 뒤 종료해 주세요."); return false; }
            _dev.Safety.CancelRamp();
            DlcDevice d = _dev.Device;
            bool needsConfirmation = false;
            if (d.IsConnected && _dev.Safety.AmplifierKnown != false) {
                try { needsConfirmation = await d.RunPriorityAsync(c => c.GetBool(P.AmpCcEnabled)); }
                catch { needsConfirmation = true; }
            }
            if (needsConfirmation) {
                bool? r = await _dialogs.ChooseAsync("종료",
                    "증폭기 전류가 켜져 있거나 OFF 상태를 확인할 수 없습니다.\n프로그램을 종료해도 장비는 그대로 동작합니다.",
                    "그대로 두고 종료", "증폭기 OFF 후 종료");
                if (r == null) return false;
                if (r == false) {
                    bool ok = false;
                    try {
                        Task off = _dev.Safety.AmpOffAsync();
                        await off.WaitAsync(TimeSpan.FromSeconds(3));
                        ok = true;
                    }
                    catch {
                        ok = false;
                    }
                    if (!ok && !await _dialogs.ConfirmAsync("종료", "증폭기 OFF를 확인하지 못했습니다.\n그래도 종료할까요?", DialogKind.Danger, "종료"))
                        return false;
                }
            }
            _closingConfirmed = true;
            return true;
        }
    }
}
