using Avalonia.Controls;
using Avalonia.Headless;
using DLC_PRO.Core.Wlm;
using DLC_PRO.Interfaces;
using DLC_PRO.Models;
using DLC_PRO.Services;
using DLC_PRO.ViewModels;
using DLC_PRO.ViewModels.Pages;
using DLC_PRO.ViewModels.Wavemeter;
using Microsoft.Extensions.DependencyInjection;

// 파장계(WLM) 기능 검증. 실제 wlmData.dll/WLM 프로그램은 절대 호출하지 않고 시뮬레이터 백엔드만 사용한다
// (이 PC에서 실제 WLM 프로그램이 실행 중이어도 설정을 바꾸지 않음).
internal static class WavemeterTests
{
    private static int _count;
    private static void Check(bool value, string message) {
        if (!value) throw new Exception("FAIL WLM " + message);
        _count++; Console.WriteLine("PASS WLM " + message);
    }
    private static async Task<bool> WaitFor(Func<bool> condition, int timeout = 6000) {
        var until = DateTime.UtcNow.AddMilliseconds(timeout);
        while (!condition()) { if (DateTime.UtcNow > until) return false; await Task.Delay(30); }
        return true;
    }

    public static async Task RunOnUi(ServiceProvider sp, MainViewModel main, Window mainWindow, string output) {
        var wlm = sp.GetRequiredService<WavemeterService>();
        var page = sp.GetRequiredService<WavemeterPageViewModel>();
        var lt = sp.GetRequiredService<LongTermViewModel>();
        var windows = sp.GetRequiredService<IWindowService>();
        WlmSimulatedBackend sim = null;
        wlm.BackendFactory = () => sim = new WlmSimulatedBackend();

        main.GoToWavemeterCommand.Execute(null);
        Check(main.CurrentPage is WavemeterPageViewModel && main.CanUseCurrentPage, "wavemeter page usable without laser selection");
        Check(page.StartButtonText == "Start" && !wlm.IsConnected, "starts disconnected");

        await page.StartStopCommand.ExecuteAsync(null);
        Check(wlm.IsConnected && wlm.CallbackMode, "Start connects with CallbackProcEx");
        Check(await WaitFor(() => wlm.IsMeasuring), "Start begins measurement");
        Check(windows.IsLongTermOpen && lt.IsWindowOpen, "Start opens LongTerm window");
        var longWin = (Window)typeof(WindowService).GetField("_longTerm", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(windows);
        Check(longWin != null && longWin.Owner == null && mainWindow.IsVisible, "LongTerm is a separate top-level window beside main window");
        Check(await WaitFor(() => lt.Count > 20), "LongTerm receives samples");
        lt.ForceRefresh();
        Check(page.ResultText.StartsWith("852.3595") && main.WlmText.Contains("852.3595"), "page and top bar show wavelength");
        Check(lt.MeanText.StartsWith("852.3595") && lt.Plot.Series[0].YD != null, "LongTerm statistics and double-precision plot");
        await Task.Delay(200); longWin?.CaptureRenderedFrame()?.Save(Path.Combine(output, "wavemeter-longterm.png"));
        mainWindow.CaptureRenderedFrame()?.Save(Path.Combine(output, "wavemeter-page.png"));

        page.ResultMode = WlmConst.cReturnFrequency;
        Check(await WaitFor(() => sim.GetResultMode() == 2 && page.ResultUnit == "THz"), "result unit written and followed");
        page.ExposureAuto = false; page.Exposure = 40;
        Check(await WaitFor(() => sim.GetExposureNum(1, 1) == 40 && !sim.GetExposureModeNum(1)), "manual exposure written");
        page.RangeIndex = 1;
        Check(await WaitFor(() => sim.GetRange() == 1), "range selected by order");
        page.Exposure = 5000;
        Check(await WaitFor(() => wlm.LastError.Contains("허용 범위")) && await WaitFor(() => page.Exposure == 40), "rejected write reported and device value restored");

        sim.SignalLevel = 0.05;
        Check(await WaitFor(() => page.ResultError.Contains("Underexposed")), "underexposed shown");
        sim.SignalLevel = 1;
        Check(await WaitFor(() => page.ResultError == ""), "signal recovers");

        lt.DeltaMode = true; lt.ForceRefresh();
        Check(lt.Plot.YLabel == "Δf [MHz]", "Δf MHz display");
        lt.DeltaMode = false;
        string tsv = Path.Combine(output, "wavemeter-longterm.lta.txt");
        lt.SaveTsv(tsv);
        string[] lines = File.ReadAllLines(tsv);
        Check(lines.Length > 20 && lines.Any(l => l.Split('\t').Length == 7 && l.Split('\t')[4] == "-3"), "TSV export keeps error codes");

        await page.StartStopCommand.ExecuteAsync(null);
        Check(await WaitFor(() => !wlm.IsMeasuring) && windows.IsLongTermOpen, "Stop pauses and keeps LongTerm window");
        windows.CloseLongTerm();
        Check(!windows.IsLongTermOpen, "LongTerm window closes");
        await wlm.StartMeasurementAsync();
        await wlm.DisconnectAsync();
        Check(!wlm.IsConnected && sim.GetOperationState() == WlmConst.cMeasurement, "disconnect leaves WLM measuring");
        Check(WavemeterSettings.Load().OpenLongTermOnStart, "wavemeter.ini saved");
        sim.Dispose();
        wlm.BackendFactory = null;
        Console.WriteLine($"WLM PASS: {_count} assertions");
    }
}
