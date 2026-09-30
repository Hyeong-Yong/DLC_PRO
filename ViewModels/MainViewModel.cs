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
        private PageFactory _pageFactory;
        private readonly DeviceService _controller;
        private readonly HardwarePageViewModel _hardware;
        private readonly WavemeterPageViewModel _wavemeter;
        private readonly WavemeterService _wlm;
        private readonly Dictionary<int, (DeviceService Dev, PageFactory Pages)> _workspaces = new();
        private DeviceService _dev;
        private readonly DialogService _dialogs;
        private readonly LogService _log;
        private bool _tripDialogOpen;
        private bool _closingConfirmed;

        public MainViewModel(PageFactory pageFactory, DeviceService dev, DialogService dialogs, LogService log,
                             WavemeterService wlm, WavemeterPageViewModel wavemeter) {
            _pageFactory = pageFactory;
            _controller = dev;
            _hardware = new HardwarePageViewModel(dev, OpenLaser);
            _wlm = wlm;
            _wavemeter = wavemeter;
            _workspaces[1] = (dev, pageFactory);
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
            _pageFactory.GetPageViewModel<LaserPageViewModel>();
            _currentPage = _hardware;
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
            nameof(SystemPageIsActive), nameof(ConsolePageIsActive), nameof(SettingsPageIsActive), nameof(HardwarePageIsActive), nameof(WavemeterPageIsActive), nameof(CanUseLaserControls), nameof(CanUseCurrentPage))]
        private PageViewModel _currentPage;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsDialogOpen))]
        private DialogViewModel? _dialog;

        public bool IsDialogOpen => Dialog?.IsDialogOpen == true;

        public bool HardwarePageIsActive => CurrentPage.PageName == ApplicationPageNames.Hardware;
        public bool WavemeterPageIsActive => CurrentPage.PageName == ApplicationPageNames.Wavemeter;
        public bool CanUseLaserControls => IsConnected && !IsConnecting && SelectedLaserId > 0 && !IsDialogOpen;
        public bool CanUseCurrentPage => !IsDialogOpen && !IsConnecting &&
            (HardwarePageIsActive || WavemeterPageIsActive || SettingsPageIsActive || (SystemPageIsActive && IsConnected) || CanUseLaserControls);
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanUseLaserControls), nameof(CanUseCurrentPage), nameof(SelectedLaserText))]
        private int _selectedLaserId;
        public string SelectedLaserText => SelectedLaserId > 0 ? $"Laser {SelectedLaserId} 제어" : "레이저 선택 전";
        [RelayCommand] private void GoToHardware() => CurrentPage = _hardware;
        [RelayCommand] private void GoToWavemeter() => CurrentPage = _wavemeter;
        private void OpenLaser(int id) {
            if (!_controller.IsConnected || !_workspaces.TryGetValue(id, out var workspace)) return;
            CurrentPage.IsActive = false;
            foreach (var w in _workspaces.Values) w.Dev.Safety.CancelRamp();
            _dev = workspace.Dev; _pageFactory = workspace.Pages; SelectedLaserId = id;
            CurrentPage = _pageFactory.GetPageViewModel<LaserPageViewModel>();
            CurrentPage.IsActive = true;
        }
        private void PrepareLaserMonitoring() {
            foreach (var row in _hardware.Lasers) {
                if (_workspaces.ContainsKey(row.Id)) continue;
                int id = row.Id;
                var service = _controller.CreateLaserService(id);
                service.SafetyTripped += reason => OnSafetyTripped($"Laser {id}: " + reason);
                _workspaces[id] = (service, PageFactory.ForLaser(service, _dialogs, _log));
            }
            foreach (var w in _workspaces.Values) {
                bool present = false;
                foreach (var row in _hardware.Lasers) if (row.Id == w.Dev.Device.LaserId) present = true;
                w.Dev.Safety.MonitoringEnabled = present;
            }
        }

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
                OnPropertyChanged(nameof(CanUseLaserControls));
                OnPropertyChanged(nameof(CanUseCurrentPage));
            }
        }

        partial void OnSideMenuExpandedChanged(bool value) {
            _controller.Settings.SideMenuExpanded = value;
            _controller.SaveSettings();
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
                    case ApplicationPageNames.Hardware: GoToHardware(); return;
                    case ApplicationPageNames.Wavemeter: GoToWavemeter(); return;
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
        [NotifyPropertyChangedFor(nameof(ConnectButtonText), nameof(CanEditConnection), nameof(CanUseLaserControls), nameof(CanUseCurrentPage))]
        [NotifyCanExecuteChangedFor(nameof(ToggleConnectCommand))]
        private bool _isConnecting;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ConnectButtonText), nameof(CanEditConnection), nameof(ConnectIcon), nameof(CanUseLaserControls), nameof(CanUseCurrentPage))]
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
            IsConnecting = true;
            try {
                if (_controller.IsConnected) {
                    if (!await _dialogs.ConfirmAsync("연결 해제", "연결을 끊어도 레이저 출력과 락은 현재 상태를 유지합니다. 연결을 끊을까요?")) return;
                    foreach (var w in _workspaces.Values) w.Dev.Safety.CancelRamp();
                    await _controller.DisconnectAsync();
                    SelectedLaserId = 0; CurrentPage = _hardware;
                    return;
                }
                bool usb = IsUsb;
                string endpoint = ((usb ? ComPort : Host) ?? "").Trim();
                foreach (var w in _workspaces.Values) w.Dev.Safety.MonitoringEnabled = false;
                string? error = await _controller.TryConnectAsync(usb, endpoint);
                if (error != null) {
                    _log.SetStatus(error.Split('\n')[0]);
                    await _dialogs.AlertAsync("연결 실패", error, DialogKind.Warning);
                    return;
                }
                _controller.Settings.ConnectionType = usb ? "USB" : "TCP";
                if (usb) _controller.Settings.ComPort = endpoint; else _controller.Settings.Host = endpoint;
                _controller.SaveSettings();
                await _hardware.DetectAsync();
                PrepareLaserMonitoring();
                _dev = _controller; _pageFactory = _workspaces[1].Pages;
                SelectedLaserId = 0; CurrentPage = _hardware;
                _log.SetStatus("연결 완료 · " + _hardware.Lasers.Count + "대의 레이저를 확인했습니다.");
            }
            catch (Exception ex) {
                _log.Error("연결 처리 실패: " + ex.Message);
                try { await _controller.DisconnectAsync(); } catch { }
                await _dialogs.AlertAsync("연결 실패", ConnectionErrors.Describe(ex, IsUsb, IsUsb ? ComPort : Host), DialogKind.Warning);
            }
            finally { IsConnecting = false; IsConnected = _controller.IsConnected; }
        }

        public ObservableCollection<DiscoveredDevice> DiscoveredDevices { get; } = new();
        [ObservableProperty] private DiscoveredDevice? _selectedDiscoveredDevice;
        [ObservableProperty] private bool _isDiscovering;
        [ObservableProperty] private string _discoveryStatus = "";
        partial void OnSelectedDiscoveredDeviceChanged(DiscoveredDevice? value) {
            if (value != null && CanEditConnection) { IsUsb = false; Host = value.Address; }
        }
        [RelayCommand]
        private async Task DiscoverDevicesAsync() {
            if (IsDiscovering || !CanEditConnection) return;
            IsDiscovering = true; DiscoveredDevices.Clear(); DiscoveryStatus = "LAN에서 장비 검색 중…";
            try {
                foreach (var device in await DeviceDiscovery.FindAsync()) DiscoveredDevices.Add(device);
                DiscoveryStatus = DiscoveredDevices.Count == 0 ? "검색된 장비가 없습니다. 같은 네트워크, 장비 전원과 UDP 60010 방화벽을 확인해 주세요." : $"{DiscoveredDevices.Count}대 검색됨 · 목록에서 선택 후 연결";
                if (DiscoveredDevices.Count == 1) SelectedDiscoveredDevice = DiscoveredDevices[0];
            }
            catch (Exception ex) { DiscoveryStatus = "장비 검색 실패: " + ex.Message + " (TOPAS가 검색 포트를 사용 중인지 확인)"; }
            finally { IsDiscovering = false; }
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
        /// <summary>상단 바의 파장계 현재 값 (어느 페이지에서나 보이도록).</summary>
        [ObservableProperty] private string _wlmText = "";
        [ObservableProperty] private string _wlmToolTip = "";
        [ObservableProperty] private LedState _wlmLed;

        public LogService Log => _log;

        [ObservableProperty] private bool _logExpanded = true;

        private void OnTick() {
            UpdateWavemeterReadout();
            DlcDevice d = _dev.Device;
            bool con = d.IsConnected;
            IsConnected = con;
            if (!con && SelectedLaserId > 0) {
                foreach (var w in _workspaces.Values) w.Dev.Safety.CancelRamp();
                SelectedLaserId = 0;
                CurrentPage = _hardware;
            }
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
            WindowTitle = "DLC pro Control · " + SelectedLaserText + (con ? " — " + (d.GetString(P.LaserType) ?? "") + " @ " + d.Endpoint : "");
        }

        private void UpdateWavemeterReadout() {
            if (!_wlm.IsConnected) {
                WlmText = "";
                WlmLed = LedState.Off;
                return;
            }
            WlmStatus s = _wlm.Status;
            double vac = _wlm.LastVacuumNm;
            int unit = s.ResultMode >= 0 && s.ResultMode <= 4 ? s.ResultMode : 0;
            bool measuring = s.ServerRunning && s.OperationState == Core.Wlm.WlmConst.cMeasurement;
            if (!s.ServerRunning) WlmText = "WLM 서버 없음";
            else if (Core.Wlm.WlmUnits.IsValid(vac))
                WlmText = Core.Wlm.WlmUnits.Format(Core.Wlm.WlmUnits.FromVacuum(vac, unit, s.AirRatio), unit) + " " + Core.Wlm.WlmUnits.Unit(unit)
                          + (measuring ? "" : " (paused)");
            else WlmText = double.IsNaN(vac) ? (measuring ? "측정 대기" : "paused") : Core.Wlm.WlmUnits.ErrorText(vac);
            WlmLed = !s.ServerRunning ? LedState.Error : measuring ? (Core.Wlm.WlmUnits.IsValid(vac) ? LedState.On : LedState.Warn) : LedState.Info;
            WlmToolTip = "파장계 " + Core.Wlm.WlmUnits.Name(unit) + " · 클릭하면 Wavemeter 페이지로 이동";
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
                await StopAllLasersAsync(false);
                _log.Warn("AMP OFF 버튼");
            }
            catch (Exception ex) { _dev.ReportError(ex); }
        }

        [RelayCommand]
        private async Task AllOffAsync() {
            try {
                await StopAllLasersAsync(true);
                _log.Warn("ALL OFF 버튼 (증폭기 → 마스터)");
            }
            catch (Exception ex) { _dev.ReportError(ex); }
        }

        private async Task StopAllLasersAsync(bool includeMaster) {
            var tasks = new List<Task>();
            foreach (var row in _hardware.Lasers) {
                var service = _workspaces[row.Id].Dev;
                if (includeMaster) tasks.Add(service.Safety.AllOffAsync());
                else if (service.Safety.AmplifierKnown != false) tasks.Add(service.Safety.AmpOffAsync());
            }
            if (tasks.Count == 0 && _hardware.Lasers.Count == 0 && _controller.IsConnected)
                throw new InvalidOperationException("레이저 구성을 확인하지 못했습니다. 하드웨어 상태를 확인해 주세요.");
            await Task.WhenAll(tasks);
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
            foreach (var workspace in _workspaces.Values) workspace.Dev.Safety.CancelRamp();
            DlcDevice d = _controller.Device;
            bool needsConfirmation = false;
            if (d.IsConnected) {
                foreach (var workspace in _workspaces.Values) {
                    if (!workspace.Dev.Safety.MonitoringEnabled || workspace.Dev.Safety.AmplifierKnown == false) continue;
                    try { needsConfirmation |= await workspace.Dev.Device.RunPriorityAsync(c => c.GetBool(P.AmpCcEnabled)); }
                    catch { needsConfirmation = true; }
                }
                if (_hardware.Lasers.Count == 0) needsConfirmation = true;
            }
            if (needsConfirmation) {
                bool? r = await _dialogs.ChooseAsync("종료",
                    "증폭기 전류가 켜져 있거나 OFF 상태를 확인할 수 없습니다.\n프로그램을 종료해도 장비는 그대로 동작합니다.",
                    "그대로 두고 종료", "증폭기 OFF 후 종료");
                if (r == null) return false;
                if (r == false) {
                    bool ok = false;
                    try {
                        Task off = StopAllLasersAsync(false);
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
