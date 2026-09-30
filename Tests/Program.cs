using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using DLC_PRO.Core;

// Only ephemeral loopback listeners created here are used. No hardware endpoint input.
internal static class RegressionTests
{
    private static int _checks;
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception("FAIL " + message);
        _checks++;
        Console.WriteLine("PASS " + message);
    }
    private static async Task Reject(Func<Task> action, string message)
    {
        try { await action(); }
        catch { Check(true, message); return; }
        Check(false, message);
    }
    private static TcpListener Listen(Action<TcpClient> serve)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    var client = await listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => { using (client) { try { serve(client); } catch { } } });
                }
            }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        });
        return listener;
    }
    private static void Inject(DlcSim.Sim sim, string name, string value)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var sync = typeof(DlcSim.Sim).GetField("_lk", flags).GetValue(sim);
        var values = (Dictionary<string, string>)typeof(DlcSim.Sim).GetField("_p", flags).GetValue(sim);
        lock (sync) { if (value == null) values.Remove(name); else values[name] = value; }
    }
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Contains("--ui")) { await UiTests.RunAsync(); return 0; }
            var sim = new DlcSim.Sim();
            new Thread(sim.Physics) { IsBackground = true }.Start();
            using var command = Listen(sim.ServeCommand);
            using var monitor = Listen(sim.ServeMonitor);
            int cp = ((IPEndPoint)command.LocalEndpoint).Port;
            int mp = ((IPEndPoint)monitor.LocalEndpoint).Port;
            M.Run(cp, mp);
            // Fault injection needs stable values; the physics loop would overwrite TC ready.
            sim = new DlcSim.Sim();
            using var faultCommand = Listen(sim.ServeCommand);
            using var faultMonitor = Listen(sim.ServeMonitor);
            cp = ((IPEndPoint)faultCommand.LocalEndpoint).Port;
            mp = ((IPEndPoint)faultMonitor.LocalEndpoint).Port;

            Check(!DecofValue.TryDouble("NaN", out _), "NaN rejected");
            Check(!DecofValue.TryDouble("1e999", out _), "overflow double rejected");
            Check(!DecofValue.TryInt("1.5", out _), "fractional state rejected");
            Check(!DecofValue.TryInt("2147483648", out _), "integer overflow rejected");
            Check(DecofValue.TryInt("3.0", out int n) && n == 3, "integer-valued float accepted");
            var malformed = BinaryBlob.Parse(BinaryBlob.Build(new[] { new KeyValuePair<char, byte[]>('x', new byte[3]) }));
            await Reject(() => Task.Run(() => malformed.Floats('x')), "truncated float array rejected");
            using (var client = new DecofClient(new ScriptTransport()))
            using (var device = new DlcDevice())
                await Reject(() => Task.Run(() => device.SetAndReadBack(client, "test", 1)), "readback failure propagates");

            using var d = new DlcDevice();
            using var safety = new AmpSafety(d, new AmpSafetySettings { WatchdogEnabled = false, RampStepMa = 100, RampIntervalMs = 50 });
            d.ConnectTcp("127.0.0.1", cp, mp);
            await d.SetAsync(P.DlCcEnabled, true);
            await d.SetAsync(P.DlCcCurrentSet, 94.5);
            await safety.AmpOffAsync();
            await d.SetAsync(P.AmpCcCurrentSet, 500.0);
            await Task.Delay(600);
            await Reject(() => safety.SetAmpCurrentAsync(double.NaN), "NaN target rejected");
            await Reject(() => safety.SetAmpCurrentAsync(double.PositiveInfinity), "infinite target rejected");
            await Reject(() => safety.SetAmpCurrentAsync(-1), "negative target rejected");

            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                var blocker = d.RunAsync(c => { entered.Set(); if (!release.Wait(5000)) throw new TimeoutException(); return 0; });
                Check(entered.Wait(3000), "worker gate entered");
                var enable = safety.EnableAmpAsync();
                var off = safety.AmpOffAsync();
                release.Set();
                await blocker;
                await off;
                await Reject(() => enable, "OFF cancels queued initial enable");
                Check(!await d.RunAsync(c => c.GetBool(P.AmpCcEnabled)), "no delayed ON after OFF");
            }

            await d.SetAsync(P.AmpCcCurrentSet, 500.0);
            Task offDuringRamp = null;
            await Reject(() => safety.EnableAmpAsync(new SyncProgress(v => { offDuringRamp ??= safety.AmpOffAsync(); })), "OFF cancels initial ramp delay");
            await offDuringRamp;
            Check(!await d.RunAsync(c => c.GetBool(P.AmpCcEnabled)), "amp stays OFF after canceled delay");
            Check(await d.RunAsync(c => c.GetDouble(P.AmpCcCurrentSet)) <= 100, "canceled ramp does not raise setpoint");

            Inject(sim, P.AmpTcReady, null);
            await Reject(() => safety.EnableAmpAsync(), "missing TC ready blocks enable");
            Check(!await d.RunAsync(c => c.GetBool(P.AmpCcEnabled)), "missing readiness leaves amplifier OFF");
            Inject(sim, P.AmpTcReady, "#t");
            await d.SetAsync(P.AmpCcCurrentSet, 300.0);
            await safety.EnableAmpAsync();
            Check(await d.RunAsync(c => c.GetBool(P.AmpCcEnabled)), "valid enable still works");
            Check(await d.RunAsync(c => c.GetDouble(P.AmpCcCurrentSet)) == 300, "valid ramp reaches target");

            Inject(sim, P.AmpTcReady, "#f");
            safety.Settings.WatchdogEnabled = true;
            var tripped = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            safety.Tripped += r => tripped.TrySetResult(r);
            await tripped.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(!await d.RunAsync(c => c.GetBool(P.AmpCcEnabled)), "watchdog checks live TC readiness and shuts off");
            safety.Settings.WatchdogEnabled = false;
            Inject(sim, P.AmpTcReady, "#t");

            long version = d.SessionVersion;
            d.Disconnect();
            d.ConnectTcp("127.0.0.1", cp, mp);
            await Reject(() => d.RunForSessionAsync(version, c => c.ParamSet(P.AmpCcEnabled, true)), "old session work rejected after reconnect");
            Check(!await d.RunAsync(c => c.GetBool(P.AmpCcEnabled)), "old session cannot enable replacement session");
            await d.RefAsync(P.LaserType);
            Inject(sim, P.AmpCcEnabled, null);
            await Reject(() => safety.AllOffAsync(), "TA amplifier OFF failure blocks master OFF");
            Check(await d.RunAsync(c => c.GetBool(P.DlCcEnabled)), "master stays ON when amplifier OFF is unverified");
            Inject(sim, P.AmpCcEnabled, "#f");
            await safety.AllOffAsync();
            Check(!await d.RunAsync(c => c.GetBool(P.DlCcEnabled)), "ALL OFF verifies amp before disabling master");
            Inject(sim, P.LaserType, "\"DLpro\"");
            await d.RefAsync(P.LaserType);
            await d.SetAsync(P.DlCcEnabled, true);
            Inject(sim, P.AmpCcEnabled, null);
            await safety.AllOffAsync();
            Check(!await d.RunAsync(c => c.GetBool(P.DlCcEnabled)), "known non-TA supports master OFF without amplifier parameter");
            typeof(DlcSim.Sim).GetMethod("FillRecorderTime", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(sim, null);
            bool switched = false;
            await Reject(() => RecorderData.FetchAsync(d, (i, total) =>
            {
                if (switched) return;
                switched = true;
                d.Disconnect();
                d.ConnectTcp("127.0.0.1", cp, mp);
            }), "recorder download cannot mix connection sessions");
            Check(switched, "recorder reconnect test reached first chunk");
            Console.WriteLine($"ALL PASS: integration suite + {_checks} regression assertions");
            await UiTests.RunAsync();
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private sealed class ScriptTransport : ITransport
    {
        private byte[] _data = Encoding.UTF8.GetBytes("> ");
        public bool IsOpen => true;
        public int ReadTimeoutMs { get; set; }
        public int BytesAvailable => _data.Length;
        public string Description => "readback failure test";
        public void Write(string text) => _data = Encoding.UTF8.GetBytes(text.StartsWith("(param-set!") ? "0\n> " : "Error: -3 missing\n> ");
        public int Read(byte[] buffer, int offset, int count)
        {
            if (_data.Length == 0) throw new TimeoutException();
            int n = Math.Min(count, _data.Length);
            Array.Copy(_data, 0, buffer, offset, n);
            _data = _data[n..];
            return n;
        }
        public void Dispose() { }
    }
}
