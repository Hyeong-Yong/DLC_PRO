using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

internal sealed class DualLaserRig : IDisposable
{
    private readonly DlcSim.Sim _first = new(), _second = new();
    private readonly TcpListener _cmd = new(IPAddress.Loopback, 0), _mon = new(IPAddress.Loopback, 0);
    private readonly ConcurrentBag<TcpClient> _clients = new();
    private readonly CancellationTokenSource _stop = new();
    private static readonly MethodInfo Eval = typeof(DlcSim.Sim).GetMethod("Eval", BindingFlags.Instance | BindingFlags.NonPublic);
    public readonly ConcurrentQueue<string> Requests = new();
    public int CommandPort => ((IPEndPoint)_cmd.LocalEndpoint).Port;
    public int MonitorPort => ((IPEndPoint)_mon.LocalEndpoint).Port;
    public bool HasSecond = true;
    public DualLaserRig() {
        Set(1, "laser1:label", "\"TA852\""); Set(2, "laser1:label", "\"TA1470\"");
        Set(2, "laser1:dl:cc:current-set", "82.0");
        Set(1, "laser1:product-name", "\"TApro (S/N 26086)\""); Set(2, "laser1:product-name", "\"TApro (S/N 26102)\"");
        _cmd.Start(); _mon.Start();
        _ = Accept(_cmd, ServeCommand); _ = Accept(_mon, ServeMonitor);
    }
    public void Set(int id, string parameter, string raw) {
        var sim = id == 1 ? _first : _second;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var sync = typeof(DlcSim.Sim).GetField("_lk", flags).GetValue(sim);
        var values = (Dictionary<string,string>)typeof(DlcSim.Sim).GetField("_p", flags).GetValue(sim);
        lock (sync) { if (raw == null) values.Remove(parameter); else values[parameter] = raw; }
    }
    public void FillRecorder(int id, bool wideScan) {
        var sim = id == 1 ? _first : _second;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        lock (typeof(DlcSim.Sim).GetField("_lk", flags).GetValue(sim))
            typeof(DlcSim.Sim).GetMethod(wideScan ? "FillRecorderWideScan" : "FillRecorderTime", flags)
                .Invoke(sim, wideScan ? new object[] { 20.0, 40.0 } : null);
    }
    private string Evaluate(string request) {
        Requests.Enqueue(request);
        var target = Regex.Match(request, @"^\((?:param-ref|param-set!|exec|param-disp) '(laser2:[^\s)]+)");
        if (!target.Success) return (string)Eval.Invoke(_first, new object[] { request });
        if (!HasSecond) return "Error: -3 no such parameter";
        var g = target.Groups[1];
        string routed = request[..g.Index] + "laser1:" + g.Value[7..] + request[(g.Index + g.Length)..];
        return (string)Eval.Invoke(_second, new object[] { routed });
    }
    private async Task Accept(TcpListener listener, Func<TcpClient,Task> serve) {
        try { while (!_stop.IsCancellationRequested) {
            var c = await listener.AcceptTcpClientAsync(_stop.Token); c.NoDelay = true; _clients.Add(c);
            _ = Task.Run(async () => { using(c) { try { await serve(c); } catch { } } });
        }} catch (OperationCanceledException) { } catch (SocketException) { }
    }
    private async Task ServeCommand(TcpClient client) {
        using var reader = new StreamReader(client.GetStream(), Encoding.UTF8);
        using var writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        await writer.WriteAsync("DLC pro dual simulator\n> ");
        while (await reader.ReadLineAsync(_stop.Token) is string line) {
            if (line == "(quit)") break;
            await writer.WriteAsync(Evaluate(line) + "\n> ");
        }
    }
    private async Task ServeMonitor(TcpClient client) {
        var watches = new ConcurrentDictionary<string,string>();
        using var reader = new StreamReader(client.GetStream(), Encoding.UTF8);
        using var writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        using var done = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        var send = Task.Run(async () => {
            try { while (!done.IsCancellationRequested) {
                foreach (var name in watches.Keys) {
                    string raw = Evaluate("(param-ref '" + name + ")");
                    if (raw.StartsWith("Error")) { watches.TryRemove(name, out _); await writer.WriteLineAsync($"(Error: -3 (add '{name}) no such parameter)"); }
                    else if (watches[name] != raw) { watches[name] = raw; await writer.WriteLineAsync($"(2026-09-30T00:00:00Z '{name} {raw})"); }
                }
                await Task.Delay(50, done.Token);
            }} catch { }
        });
        try { while (await reader.ReadLineAsync(done.Token) is string line) {
            var match = Regex.Match(line, @"^\(add '([^\s)]+)");
            if (match.Success) watches.TryAdd(match.Groups[1].Value, "");
            if (line == "(remove-all)") watches.Clear();
        }} finally { done.Cancel(); await send; }
    }
    public void Dispose() { _stop.Cancel(); _cmd.Stop(); _mon.Stop(); foreach (var c in _clients) c.Dispose(); }
}
