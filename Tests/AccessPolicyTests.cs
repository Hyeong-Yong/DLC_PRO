using System.Text;
using DLC_PRO.Core;

internal static class AccessPolicyTests
{
    private static int count;
    private static void Check(bool value, string message) {
        if (!value) throw new Exception("FAIL protection: " + message);
        count++; Console.WriteLine("PASS protection " + message);
    }
    private static void Block(RecordingTransport transport, Action action, string message) {
        int before = transport.Writes.Count;
        bool rejected = false;
        try { action(); } catch (UnauthorizedAccessException) { rejected = true; }
        Check(rejected && transport.Writes.Count == before, message + " blocked before transport write");
    }

    public static void Run() {
        var transport = new RecordingTransport();
        using var client = new DecofClient(transport);
        foreach (int id in new[] { 1, 2 }) {
            var view = client.ForLaser(id);
            foreach (string parameter in new[] {
                P.DlCcCurrentClip, P.AmpCcCurrentClip, P.PdExtCalOffset, P.PdExtCalFactor,
                "laser1:dl:factory-settings:cc:current-clip", "laser1:dl:factory-settings:cc:current-set",
                "laser1:amp:pd:amp:cal-factor", "laser1:dl:cc:current-clip-limit",
                "laser1:amp:seed-limits:power-min", "laser1:dl:tc:temp-max",
                "laser1:dl:pc:voltage-max", "laser1:nlo:shg:lock:calibration:power-max",
                "laser1:new-firmware:unknown-setting", "ul"
            }) {
                Block(transport, () => view.ParamSet(parameter, 999), $"laser{id} {parameter}");
            }
            foreach (string command in new[] { "laser1:dl:restore-factory-settings", "laser1:nlo:opo:cavity:motor:recalibrate", "load", "save", "reboot", "change-password" })
                Block(transport, () => view.Exec(command), $"laser{id} {command}");
            // Protection applies even when an already elevated connection is used: there is no UL bypass.
            foreach (int level in new[] { -1, 0, 1, 2, 5 })
                Block(transport, () => view.Exec(P.CmdChangeUl, new object[] { level, "password" }), $"laser{id} privilege {level}");
            Block(transport, () => view.ParamSet(P.LaserLabel, new RawValue("(exec 'change-ul 1 \"pw\")")), $"laser{id} raw value injection");
            Block(transport, () => view.Exec(P.CmdLockSelectLockpoint, new object[] { new RawValue("(param-set! 'laser1:dl:cc:current-clip 999)") }), $"laser{id} command argument injection");
            Block(transport, () => view.ParamRef("laser1:type) (param-set! 'laser1:dl:cc:current-clip 999"), $"laser{id} read name injection");
            Block(transport, () => view.ParamSet("laser1:dl:cc:current-set) (exec 'restore", 1), $"laser{id} write name injection");
            int before = transport.Writes.Count;
            view.ParamRef(P.DlCcCurrentClip); view.ParamRef(P.PdExtCalFactor);
            view.ParamSet(P.DlCcCurrentSet, 80.0); view.ParamSet(P.AmpCcEnabled, false);
            view.Exec(P.CmdLockOpen); view.Exec(P.CmdWsStop); view.Exec(P.CmdRecGetData, new object[] { 0, 100 });
            Check(transport.Writes.Count == before + 7 && transport.Writes.Skip(before).All(x => x.Contains("laser" + id + ":")), $"laser{id} reads and normal operation still route correctly");
        }
        string[] malicious = {
            "(param-set! 'laser1:dl:cc:current-clip 999)",
            "(param-set! 'laser2:pd-ext:cal-factor 2)",
            "(param-set! 'laser1:dl:cc:current-set 1)",
            "(exec 'change-ul 1 \"password\")", "(change-ul 2 \"password\")",
            "(begin (param-ref 'ul) (param-set! 'ul 1))",
            "(param-ref 'ul) (param-set! 'ul 1)",
            "(param-ref 'ul)\n(exec 'restore-factory-settings)",
            "(eval '(param-set! 'ul 1))", "(load \"file\")",
            "(param-ref (begin (param-set! 'ul 1) 'ul))",
            "(param-disp 'laser1) ; comment\n(param-set! 'ul 1)",
            "(exec 'system-messages:show-new (param-set! 'ul 1))",
            "(param-ref '|laser1:dl:cc:current-clip|)",
            "(exec 'laser1:wide-scan:start)", "(param-ref 'ul)\0"
        };
        foreach (string raw in malicious) Block(transport, () => client.Send(raw), "console " + raw.Replace('\n', ' '));
        foreach (string query in new[] { "(param-ref 'laser2:dl:factory-settings:cc:current-clip)", "(param-disp 'laser1:pd-ext)", "(exec 'system-messages:show-new)" }) {
            int before = transport.Writes.Count;
            client.Send(query);
            Check(transport.Writes.Count == before + 1 && !client.IsBroken, "read-only console still works after rejected commands");
        }
        foreach (int level in new[] { 3, 4 }) {
            client.Exec(P.CmdChangeUl, new object[] { level, "" });
            Check(transport.Writes.Last().Contains($" {level} \"\""), "normal/read-only authentication retained");
        }
        var monTransport = new RecordingTransport();
        using var monitor = new MonitorLine(monTransport);
        Block(monTransport, () => monitor.ChangeUserLevel(1, "pw"), "monitor service escalation");
        Block(monTransport, () => monitor.Add("ul) (change-ul 1 \"pw\")", 100, 0), "monitor subscription injection");
        Console.WriteLine($"Protection PASS: {count} assertions");
    }

    private sealed class RecordingTransport : ITransport {
        private byte[] pending = Encoding.UTF8.GetBytes("> ");
        public List<string> Writes { get; } = new();
        public bool IsOpen => true;
        public int ReadTimeoutMs { get; set; }
        public int BytesAvailable => pending.Length;
        public string Description => "protection recording transport";
        public void Write(string text) {
            Writes.Add(text);
            pending = Encoding.UTF8.GetBytes("0\n> ");
        }
        public int Read(byte[] buffer, int offset, int length) {
            if (pending.Length == 0) throw new TimeoutException();
            int n = Math.Min(length, pending.Length);
            Array.Copy(pending, 0, buffer, offset, n); pending = pending[n..]; return n;
        }
        public void Dispose() { }
    }
}
