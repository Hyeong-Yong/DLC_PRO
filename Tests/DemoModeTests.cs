using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using DLC_PRO.Core.Calibration;
using DLC_PRO.Services;
using DLC_PRO.ViewModels;
using DLC_PRO.ViewModels.Dialogs;
using DLC_PRO.ViewModels.Pages;
using Microsoft.Extensions.DependencyInjection;

// 데모 모드(가상 장비) + 레이저별 튜닝 계수 교정 UI 검증. 앱 안의 루프백 가상 DLC pro만 사용한다.
internal static class DemoModeTests
{
    private static int _count;
    private static void Check(bool value, string message) {
        if (!value) throw new Exception("FAIL DEMO " + message);
        _count++; Console.WriteLine("PASS DEMO " + message);
    }
    private static async Task<bool> WaitFor(Func<bool> condition, int timeout = 8000) {
        var until = DateTime.UtcNow.AddMilliseconds(timeout);
        while (!condition()) { if (DateTime.UtcNow > until) return false; await Task.Delay(30); }
        return true;
    }
    private static async Task CloseDialog(MainViewModel main) {
        if (main.Dialog is ConfirmDialogViewModel d && main.IsDialogOpen) await d.ConfirmCommand.ExecuteAsync(null);
    }
    /// <summary>작업을 실행하고, 실패 대화상자가 뜨면 내용을 기록한 뒤 닫는다 (대화상자가 닫혀야 작업이 끝남).</summary>
    private static async Task<string> RunClosingDialog(MainViewModel main, Func<Task> action) {
        Task t = action();
        await WaitFor(() => t.IsCompleted || main.IsDialogOpen, 30000);
        string msg = main.IsDialogOpen ? (main.Dialog as ConfirmDialogViewModel)?.Message ?? "" : "";
        await CloseDialog(main);
        await t;
        return msg;
    }

    public static async Task RunOnUi(ServiceProvider sp, MainViewModel main, Window window, string output) {
        var dev = sp.GetRequiredService<DeviceService>();
        await WaitFor(() => !main.IsConnected);
        await main.ConnectDemoCommand.ExecuteAsync(null);
        await Task.Delay(300);
        var hw = main.CurrentPage as HardwarePageViewModel;
        Check(main.IsConnected && main.IsDemo && main.DemoBadgeVisible && hw != null && hw.Lasers.Count == 2, "demo connects to virtual controller with two lasers");
        Check(dev.Device.Endpoint.StartsWith("127.0.0.1"), "demo uses loopback only: " + dev.Device.Endpoint);
        await Task.Delay(150); window.CaptureRenderedFrame()?.Save(Path.Combine(output, "demo-hardware.png"));

        hw.Lasers[0].OpenCommand.Execute(null);
        Check(main.CanUseLaserControls, "laser controls usable without hardware");
        main.GoToScanLockCommand.Execute(null);
        var scan = (ScanLockPageViewModel)main.CurrentPage;
        scan.Calibration.SelectedPreset = CalibrationPresets.CsD2;
        await Task.Delay(1500);
        await RunClosingDialog(main, () => scan.Calibration.UseCurrentScanCommand.ExecuteAsync(null));
        Check(scan.Calibration.IsSuccess && Math.Abs(dev.Settings.Freq.CoefGHzPerV * 1000 - 328) < 8, $"laser1 demo PS spectrum calibrates to {dev.Settings.Freq.CoefGHzPerV * 1000:F1} MHz/V");
        string failMsg = await RunClosingDialog(main, () => scan.Calibration.AnalyzeFileAsync(CalibrationTests.DataFile("PS_D2_line2.csv")));
        Check(!scan.Calibration.IsSuccess && failMsg.Contains("허용"), "line2 file → FAIL + error dialog");
        await RunClosingDialog(main, () => scan.Calibration.AnalyzeFileAsync(CalibrationTests.DataFile("PS_D2_line3.csv")));
        Check(scan.Calibration.IsSuccess && scan.FreqAxis.Coefficient.HasValue && Math.Abs((double)scan.FreqAxis.Coefficient.Value - 0.32961) < 0.0002, "line3 file → SUCCESS, input box = 0.32961 GHz/V");
        var tabs = window.GetVisualDescendants().OfType<TabControl>().First(); tabs.SelectedIndex = 2;
        await Task.Delay(200); window.CaptureRenderedFrame()?.Save(Path.Combine(output, "demo-calibration-laser1.png"));

        main.GoToHardwareCommand.Execute(null);
        hw.Lasers[1].OpenCommand.Execute(null);
        main.GoToScanLockCommand.Execute(null);
        var scan2 = (ScanLockPageViewModel)main.CurrentPage;
        scan2.Calibration.SelectedPreset = CalibrationPresets.Cs7S;
        await Task.Delay(1500);
        await RunClosingDialog(main, () => scan2.Calibration.UseCurrentScanCommand.ExecuteAsync(null));
        Check(scan2.Calibration.IsSuccess, "laser2 6P3/2→7S1/2 demo calibration: " + scan2.Calibration.ResultTitle);
        Check(Math.Abs(DLC_PRO.Models.AppSettings.Load(1).Freq.CoefGHzPerV - 0.32961) < 0.0002 && DLC_PRO.Models.AppSettings.Load(2).Freq.CalibrationPreset == "cs-6p-7s", "coefficients saved per laser");

        var disconnect = main.ToggleConnectCommand.ExecuteAsync(null);
        await WaitFor(() => main.IsDialogOpen);
        await CloseDialog(main); await disconnect;
        Check(!main.IsConnected && !main.IsDemo, "demo disconnects and stops virtual controller");
        Console.WriteLine($"DEMO PASS: {_count} assertions");
    }
}
