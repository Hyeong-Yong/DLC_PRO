using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLC_PRO.Core;
using DLC_PRO.Data;
using DLC_PRO.Services;

namespace DLC_PRO.ViewModels.Pages;

public partial class LaserStatusViewModel : ViewModelBase
{
    private readonly DlcDevice _device;
    private readonly Action<int> _open;
    public int Id { get; }
    public LaserStatusViewModel(DlcDevice device, Action<int> open) {
        _device = device; Id = device.LaserId; _open = open;
        device.WatchMany(300, P.LaserLabel, P.LaserProductName, P.LaserHealthTxt, P.LaserEmission,
            P.DlCcEnabled, P.AmpCcEnabled, P.ScanEnabled, P.LockEnabled, P.PsEnabled);
    }
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private string _product = "";
    [ObservableProperty] private string _health = "—";
    [ObservableProperty] private string _emission = "—";
    [ObservableProperty] private string _laserEnabled = "—";
    [ObservableProperty] private string _scanEnabled = "—";
    [ObservableProperty] private string _lockEnabled = "—";
    [ObservableProperty] private string _powerLock = "—";
    [ObservableProperty] private bool _canOpen;
    private string State(string p) => _device.IsConnected && _device.TryGetBool(p, out bool v) ? v ? "● ON" : "○ OFF" : "—";
    public void Refresh() {
        CanOpen = _device.IsConnected;
        Label = _device.GetString(P.LaserLabel) ?? "Laser " + Id;
        Product = _device.GetString(P.LaserProductName) ?? _device.GetString(P.LaserType) ?? "—";
        Health = CanOpen ? _device.GetString(P.LaserHealthTxt) ?? "확인 중" : "연결 해제";
        Emission = State(P.LaserEmission); LaserEnabled = State(P.DlCcEnabled);
        ScanEnabled = State(P.ScanEnabled); LockEnabled = State(P.LockEnabled); PowerLock = State(P.PsEnabled);
    }
    [RelayCommand] private void Open() { if (CanOpen) _open(Id); }
}

public partial class HardwarePageViewModel : PageViewModel
{
    private readonly Action<int> _open;
    public HardwarePageViewModel(DeviceService dev, Action<int> open) : base(ApplicationPageNames.Hardware, dev) {
        _open = open;
        dev.Device.WatchMany(1000, P.SystemLabel, P.SerialNumber, P.SystemType);
    }
    public override string Title => "하드웨어 상태";
    public override string Subtitle => "DLC pro에 연결한 레이저를 선택하면 해당 레이저의 제어 화면이 열립니다.";
    public ObservableCollection<LaserStatusViewModel> Lasers { get; } = new();
    [ObservableProperty] private string _controllerText = "장비에 연결해 주세요";
    [ObservableProperty] private string _statusText = "TCP/IP 또는 USB로 연결하면 레이저 1·2를 자동 확인합니다.";
    public async Task DetectAsync() {
        Lasers.Clear(); StatusText = "연결된 레이저 확인 중…";
        long version = Dev.Device.SessionVersion;
        for (int id = 1; id <= 2; id++) {
            var device = Dev.Device.ForLaser(id);
            try {
                string raw = await Dev.Device.RunForSessionAsync(version, c => c.ParamRef("laser" + id + ":type"));
                string type = DecofValue.Str(raw);
                if (string.IsNullOrWhiteSpace(type) || type.Equals("none", StringComparison.OrdinalIgnoreCase)) continue;
                await device.RefAsync(P.LaserType);
                var row = new LaserStatusViewModel(device, _open);
                row.Refresh(); Lasers.Add(row);
            }
            catch (DecofException ex) when (ex.Code == -3) { }
        }
        StatusText = Lasers.Count == 0 ? "연결된 레이저를 확인하지 못했습니다. 장비 구성과 헤드 연결을 확인해 주세요." : $"레이저 {Lasers.Count}대 감지됨 · 행을 클릭하여 제어";
    }
    protected override void OnTick() {
        ControllerText = Dev.IsConnected ? (Dev.Device.GetString(P.SystemLabel) ?? "DLC pro") + " · S/N " + (Dev.Device.GetString(P.SerialNumber) ?? "—") : "장비에 연결해 주세요";
        foreach (var laser in Lasers) laser.Refresh();
        if (!Dev.IsConnected) { Lasers.Clear(); StatusText = "TCP/IP 또는 USB로 연결하면 레이저 1·2를 자동 확인합니다."; }
    }
}
