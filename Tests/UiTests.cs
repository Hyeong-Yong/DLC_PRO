using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using DLC_PRO.Controls;
using DLC_PRO.Core;
using DLC_PRO.Models;
using DLC_PRO.Services;
using DLC_PRO.ViewModels;
using DLC_PRO.ViewModels.Dialogs;
using DLC_PRO.ViewModels.Pages;
using DLC_PRO.Views;
using Microsoft.Extensions.DependencyInjection;

internal static class UiTests
{
    private static int _count;
    private static void Check(bool value, string message) {
        if (!value) throw new Exception("FAIL UI " + message);
        _count++; Console.WriteLine("PASS UI " + message);
    }
    private static async Task WaitFor(Func<bool> condition, int timeout = 5000) {
        var until = DateTime.UtcNow.AddMilliseconds(timeout);
        while (!condition()) { if (DateTime.UtcNow > until) throw new TimeoutException("UI condition timed out"); await Task.Delay(30); }
    }
    public static Task RunAsync() {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => {
            using var stop = new CancellationTokenSource();
            try {
                AppSettings.FolderOverride = Path.Combine(Path.GetTempPath(), "DLC_PRO-tests-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(AppSettings.FolderOverride);
                File.WriteAllText(Path.Combine(AppSettings.FolderOverride, "settings.ini"), "Safety.WatchdogEnabled=false\nSafety.MinSeedPowerMw=1e100\nSafety.MaxAmpCurrentMa=1e100\nSafety.RampStepMa=1e100\nSafety.ConfirmDeltaMa=1e100\n");
                AppBuilder.Configure<DLC_PRO.App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                Dispatcher.UIThread.Post(async () => {
                    try { await RunOnUi(); finished.SetResult(); }
                    catch (Exception ex) { finished.SetException(ex); }
                    finally { stop.Cancel(); }
                });
                Dispatcher.UIThread.MainLoop(stop.Token);
            } catch (Exception ex) { finished.TrySetException(ex); }
        }) { IsBackground = true };
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return finished.Task;
    }
    private static async Task RunOnUi() {
        using var rig = new DualLaserRig();
        var sp = (ServiceProvider)typeof(DLC_PRO.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Application.Current);
        var main = sp.GetRequiredService<MainViewModel>();
        var dev = sp.GetRequiredService<DeviceService>();
        var window = new Window { Width = 1440, Height = 950, Content = new MainView { DataContext = main } };
        window.Show();
        Check(main.CurrentPage is HardwarePageViewModel, "startup opens hardware dashboard");
        Check(dev.Settings.Safety.MinSeedPowerMw == 0 && dev.Settings.Safety.MaxAmpCurrentMa == 0 && dev.Settings.Safety.RampStepMa == 100 && dev.Settings.Safety.ConfirmDeltaMa == 300, "oversized settings recover without startup overflow");
        string output = Path.Combine(Directory.GetCurrentDirectory(), "Tests", "artifacts"); Directory.CreateDirectory(output);

        main.IsUsb = true; main.ComPorts.Add("COM_DOES_NOT_EXIST"); main.ComPort = "COM_DOES_NOT_EXIST";
        Task failed = main.ToggleConnectCommand.ExecuteAsync(null);
        await WaitFor(() => main.IsDialogOpen);
        var dialog = (ConfirmDialogViewModel)main.Dialog;
        Check(dialog.Message.Contains("USB 포트를 찾을 수 없습니다"), "missing USB shows friendly warning: " + dialog.Message);
        await dialog.ConfirmCommand.ExecuteAsync(null); await failed;
        Check(!main.IsConnected && !main.IsConnecting, "USB failure leaves app reusable");

        // Refused loopback port, not a real hardware address.
        var unused = new TcpListener(IPAddress.Loopback, 0); unused.Start(); int port = ((IPEndPoint)unused.LocalEndpoint).Port; unused.Stop();
        dev.Settings.CmdPort = port; main.IsUsb = false; main.Host = "127.0.0.1";
        failed = main.ToggleConnectCommand.ExecuteAsync(null);
        await WaitFor(() => main.IsDialogOpen);
        dialog = (ConfirmDialogViewModel)main.Dialog;
        Check(dialog.Message.Contains("IP 주소의 장비를 찾을 수 없거나 응답이 없습니다"), "unreachable TCP shows friendly warning");
        await Task.Delay(100); window.CaptureRenderedFrame()?.Save(Path.Combine(output,"connection-warning.png"));
        await dialog.ConfirmCommand.ExecuteAsync(null); await failed;
        Check(!main.IsConnected && !main.IsConnecting, "TCP failure leaves app reusable");
        Check(ConnectionErrors.Describe(new TimeoutException(), false, "test").Contains("응답이 없습니다"), "timeout has friendly message");
        using (var discoveryServer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0))) {
            var address = (IPEndPoint)discoveryServer.Client.LocalEndPoint;
            var find = DeviceDiscovery.FindAtAsync(new[] { address }, 0);
            var request = await discoveryServer.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Check(System.Text.Encoding.ASCII.GetString(request.Buffer) == "laserfinder", "discovery sends manual-defined laserfinder packet");
            byte[] response = System.Text.Encoding.UTF8.GetBytes("DLC pro SIM-0001 firmware 3.5.1");
            await discoveryServer.SendAsync(response, request.RemoteEndPoint);
            await discoveryServer.SendAsync(response, request.RemoteEndPoint);
            var found = await find;
            Check(found.Count == 1 && found[0].Address == "127.0.0.1", "discovery uses response source IP and deduplicates replies");
            main.DiscoveredDevices.Add(found[0]); main.SelectedDiscoveredDevice = found[0];
            Check(main.Host == "127.0.0.1" && !main.IsUsb, "selecting discovery result populates TCP endpoint");
        }

        dev.Settings.CmdPort = rig.CommandPort; dev.Settings.MonPort = rig.MonitorPort;
        await main.ToggleConnectCommand.ExecuteAsync(null);
        await Task.Delay(400);
        var hardware = (HardwarePageViewModel)main.CurrentPage;
        Check(main.IsConnected && hardware.Lasers.Count == 2, "retry succeeds and detects two lasers");
        Check(hardware.Lasers[0].Label == "TA852" && hardware.Lasers[1].Label == "TA1470", "dashboard separates laser labels");
        window.CaptureRenderedFrame()?.Save(Path.Combine(output,"hardware-two-lasers.png"));
        hardware.Lasers[1].OpenCommand.Execute(null);
        Check(main.SelectedLaserId == 2 && main.CurrentPage is LaserPageViewModel, "laser2 row opens laser2 controls");
        var laser2 = (LaserPageViewModel)main.CurrentPage;
        await Task.Delay(250);
        laser2.CcCurrentSet.Value = 80.25;
        await WaitFor(() => Math.Abs(dev.Device.ForLaser(2).GetDouble(P.DlCcCurrentSet, 0) - 80.25) < 0.001);
        Check(await dev.Device.RunAsync(c => c.GetDouble(P.DlCcCurrentSet)) != 80.25, "laser2 write leaves laser1 unchanged");
        await dev.Device.ForLaser(2).SetAsync(P.LaserLabel, "literal laser1: text");
        Check(await dev.Device.ForLaser(2).RunAsync(c => c.GetString(P.LaserLabel)) == "literal laser1: text", "routing does not rewrite string payloads");
        await dev.Device.ForLaser(2).SetAsync(P.LaserLabel, "TA1470");
        main.GoToScanLockCommand.Execute(null);
        await Task.Delay(500);
        Check(rig.Requests.Any(x => x == "(param-ref 'laser2:scope:data)"), "laser2 scope reads laser2 data");
        await main.CurrentPage.AsScan().FindCandidatesCommand.ExecuteAsync(null);
        Check(rig.Requests.Any(x => x == "(exec 'laser2:dl:lock:find-candidates)"), "laser2 lock command targets laser2");
        window.CaptureRenderedFrame()?.Save(Path.Combine(output,"scanlock-laser2.png"));
        main.GoToStabilizationCommand.Execute(null); await Task.Delay(200);
        window.CaptureRenderedFrame()?.Save(Path.Combine(output,"stabilization-laser2.png"));
        Check(PlotTypography.FontName.Contains("Gothic", StringComparison.OrdinalIgnoreCase) || PlotTypography.FontName.Contains("고딕"), "Windows Korean font selected");
        using (var typeface = SkiaSharp.SKTypeface.FromFamilyName(PlotTypography.FontName))
        using (var font = new SkiaSharp.SKFont(typeface))
            Check(font.ContainsGlyphs("파워 추세 시간 수동 스케일"), "plot font contains Korean glyphs");
        var plot = new ScottPlot.Plot(); plot.Title("파워 추세 (최근 약 10분)"); plot.XLabel("시간 [s]"); plot.YLabel("파워 [mW]");
        plot.Axes.Right.Label.Text = "Seed [mW]"; PlotTypography.Apply(plot);
        plot.SavePng(Path.Combine(output,"korean-plot.png"),800,500);
        Check(plot.Axes.Title.Label.FontName == PlotTypography.FontName && plot.Axes.Bottom.Label.FontName == PlotTypography.FontName && plot.Axes.Left.Label.FontName == PlotTypography.FontName, "all plot axes and title use same Korean font");
        main.GoToWideScanCommand.Execute(null);
        var wide = (WideScanPageViewModel)main.CurrentPage;
        var starting = wide.StartCommand.ExecuteAsync(null);
        await WaitFor(() => main.IsDialogOpen);
        var approval = (ConfirmDialogViewModel)main.Dialog;
        // Change hardware while the scan-stop confirmation is open.
        rig.Set(2, P.LockState, "5");
        await approval.ConfirmCommand.ExecuteAsync(null);
        await WaitFor(() => main.IsDialogOpen && !ReferenceEquals(main.Dialog, approval));
        Check(((ConfirmDialogViewModel)main.Dialog).Message.Contains("UNLOCK") && !rig.Requests.Any(x => x == "(exec 'laser2:wide-scan:start)"), "wide scan rejects lock activated during confirmation");
        await ((ConfirmDialogViewModel)main.Dialog).ConfirmCommand.ExecuteAsync(null); await starting;
        rig.Set(2, P.LockState, "0");
        starting = wide.StartCommand.ExecuteAsync(null);
        await WaitFor(() => main.IsDialogOpen);
        approval = (ConfirmDialogViewModel)main.Dialog;
        // Reconnect while that approval is open: the old request must not control the new session.
        await dev.DisconnectAsync();
        await WaitFor(() => main.SelectedLaserId == 0);
        Check(main.CurrentPage is HardwarePageViewModel && !main.CanUseLaserControls, "lost connection returns to dashboard and disables previous laser controls");
        await dev.ConnectTcpAsync("127.0.0.1");
        await approval.ConfirmCommand.ExecuteAsync(null);
        await WaitFor(() => main.IsDialogOpen && !ReferenceEquals(main.Dialog, approval));
        Check(((ConfirmDialogViewModel)main.Dialog).Message.Contains("연결이 변경") && !rig.Requests.Any(x => x == "(exec 'laser2:wide-scan:start)"), "wide scan rejects approval from previous connection");
        await ((ConfirmDialogViewModel)main.Dialog).ConfirmCommand.ExecuteAsync(null); await starting;
        await hardware.DetectAsync();
        hardware.Lasers[1].OpenCommand.Execute(null);
        main.GoToWideScanCommand.Execute(null);
        starting = wide.StartCommand.ExecuteAsync(null);
        await WaitFor(() => main.IsDialogOpen);
        await ((ConfirmDialogViewModel)main.Dialog).ConfirmCommand.ExecuteAsync(null); await starting;
        Check(rig.Requests.Any(x => x == "(exec 'laser2:wide-scan:start)") && !await dev.Device.ForLaser(2).RunAsync(c => c.GetBool(P.ScanEnabled)), "wide scan starts selected laser after verified scan OFF");
        await wide.StopCommand.ExecuteAsync(null);
        rig.FillRecorder(2, true);
        await wide.LoadDataCommand.ExecuteAsync(null);
        Check(wide.Plot.XLabel.Contains("Piezo") && rig.Requests.Any(x => x.StartsWith("(exec 'laser2:recorder:data:get-data")), "wide scan loads selected laser recorder data");
        await Task.Delay(150); window.CaptureRenderedFrame()?.Save(Path.Combine(output,"widescan-laser2.png"));
        main.GoToRecorderCommand.Execute(null);
        var recorder = (RecorderPageViewModel)main.CurrentPage;
        rig.FillRecorder(2, false);
        await recorder.LoadDataCommand.ExecuteAsync(null);
        Check(recorder.Plot.XLabel.Contains("Time"), "recorder shows time axis after data download");
        await Task.Delay(150); window.CaptureRenderedFrame()?.Save(Path.Combine(output,"recorder-laser2.png"));
        main.GoToSettingsCommand.Execute(null);
        var settings2 = (SettingsPageViewModel)main.CurrentPage;
        settings2.ScopeMaxRate = 12;
        Check(dev.Settings.ScopeMaxRate == 12 && dev.Device.MaxScopeRate == 12, "laser2 communication settings are shared with controller");
        settings2.MinSeedPowerMw = 7;
        settings2.ApplySafetyCommand.Execute(null);
        Check(dev.Settings.Safety.MinSeedPowerMw == 0 && AppSettings.Load(2).Safety.MinSeedPowerMw == 7, "laser2 safety settings remain independent");
        main.GoToHardwareCommand.Execute(null); hardware.Lasers[0].OpenCommand.Execute(null);
        Check(main.SelectedLaserId == 1 && !ReferenceEquals(laser2,main.CurrentPage), "laser1 has independent page state");

        await dev.Device.SetAsync(P.AmpCcEnabled, true); await dev.Device.ForLaser(2).SetAsync(P.AmpCcEnabled, true);
        await main.AmpOffCommand.ExecuteAsync(null);
        Check(!await dev.Device.RunAsync(c => c.GetBool(P.AmpCcEnabled)) && !await dev.Device.ForLaser(2).RunAsync(c => c.GetBool(P.AmpCcEnabled)), "global AMP OFF shuts off both amplifiers");
        await main.AllOffCommand.ExecuteAsync(null);
        Check(!await dev.Device.RunAsync(c => c.GetBool(P.DlCcEnabled)) && !await dev.Device.ForLaser(2).RunAsync(c => c.GetBool(P.DlCcEnabled)), "global ALL OFF shuts off both masters after amps");

        var disconnect = main.ToggleConnectCommand.ExecuteAsync(null); await WaitFor(() => main.IsDialogOpen);
        await ((ConfirmDialogViewModel)main.Dialog).ConfirmCommand.ExecuteAsync(null); await disconnect;
        rig.HasSecond = false;
        await main.ToggleConnectCommand.ExecuteAsync(null); await Task.Delay(150);
        Check(((HardwarePageViewModel)main.CurrentPage).Lasers.Count == 1, "reconnect correctly detects one laser");
        Check(main.SelectedLaserId == 0 && !main.CanUseLaserControls, "old laser2 controls cannot be reused after reconnect");
        await dev.DisconnectAsync(); window.Close(); dev.Dispose(); sp.GetRequiredService<LogService>().Dispose();
        Console.WriteLine($"UI PASS: {_count} assertions; screenshots in {output}");
    }
    private static ScanLockPageViewModel AsScan(this PageViewModel page) => (ScanLockPageViewModel)page;
}
