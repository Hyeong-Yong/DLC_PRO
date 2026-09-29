#nullable disable
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace DLC_PRO.Core
{
    /// <summary>
    /// 모니터링 라인 (TCP 1999) 클라이언트 (Command Reference 2.4).
    /// (add 'param period_ms threshold) 로 구독하면 값이 바뀔 때마다
    /// (2013-07-15T13:58:59.123Z 'laser1:dl:cc:current-act 98.354201) 형태로 전송된다.
    /// 수신은 백그라운드 스레드에서 처리한다.
    /// </summary>
    public sealed class MonitorLine : IDisposable
    {
        private static readonly Regex ValueRx = new Regex(@"^\((\S+) '(\S+) (.*)\)\s*$", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex ErrorRx = new Regex(@"^\(Error: (.*?) \((\S*) '(\S+)\) (.*)\)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private readonly ITransport _t;
        private readonly Thread _thread;
        private readonly object _wsync = new object();
        private volatile bool _stop;

        /// <summary>(파라미터 이름, 원시 값, 장비 타임스탬프 문자열)</summary>
        public event Action<string, string, string> ValueReceived;
        /// <summary>(파라미터 이름, 오류 메시지)</summary>
        public event Action<string, string> ErrorReceived;
        public event Action<Exception> Closed;
        public event Action<string, string> Traffic;

        public MonitorLine(ITransport transport)
        {
            _t = transport;
            _t.ReadTimeoutMs = 1000;
            _thread = new Thread(Run) { IsBackground = true, Name = "DLCpro-Monitor" };
        }

        /// <summary>이벤트 연결 후 호출해 수신 스레드를 시작한다.</summary>
        public void Start()
        {
            if (!_thread.IsAlive) _thread.Start();
        }

        public void Add(string param, int periodMs, double threshold)
        {
            string cmd = threshold > 0
                ? "(add '" + param + " " + periodMs + " " + DecofValue.EncodeDouble(threshold) + ")"
                : "(add '" + param + " " + periodMs + ")";
            Write(cmd);
        }

        public void Remove(string param) { Write("(remove '" + param + ")"); }
        public void RemoveAll() { Write("(remove-all)"); }
        public void ChangeUserLevel(int ul, string password) { Write("(change-ul " + ul + " " + DecofValue.EncodeString(password ?? "") + ")"); }

        private void Write(string cmd)
        {
            lock (_wsync)
            {
                Raise("MON-TX", cmd);
                _t.Write(cmd + "\n");
            }
        }

        private void Run()
        {
            byte[] buf = new byte[65536];
            StringBuilder line = new StringBuilder();
            Decoder dec = Encoding.UTF8.GetDecoder();
            char[] chars = new char[65536];
            Exception reason = null;
            try
            {
                while (!_stop)
                {
                    int n;
                    try { n = _t.Read(buf, 0, buf.Length); }
                    catch (TimeoutException) { continue; }
                    int nc = dec.GetChars(buf, 0, n, chars, 0);
                    for (int i = 0; i < nc; i++)
                    {
                        char c = chars[i];
                        if (c == '\n')
                        {
                            string l = line.ToString().TrimEnd('\r');
                            line.Clear();
                            if (l.Length > 0) Handle(l);
                        }
                        else line.Append(c);
                    }
                }
            }
            catch (Exception ex) { reason = ex; }
            if (!_stop)
            {
                Action<Exception> h = Closed;
                if (h != null) { try { h(reason ?? new IOException("모니터링 라인 종료")); } catch { } }
            }
        }

        private void Handle(string l)
        {
            Raise("MON-RX", l);
            string t = l.Trim();
            if (t.StartsWith("> ")) t = t.Substring(2).Trim();
            if (t == "()" || t.Length == 0) return;
            Match m;
            if (t.StartsWith("Error", StringComparison.OrdinalIgnoreCase))
            {
                Action<string, string> eh0 = ErrorReceived;
                if (eh0 != null) { try { eh0("", t); } catch { } }
                return;
            }
            if (t.StartsWith("(Error", StringComparison.OrdinalIgnoreCase))
            {
                m = ErrorRx.Match(t);
                Action<string, string> eh = ErrorReceived;
                if (eh != null)
                {
                    try { eh(m.Success ? m.Groups[3].Value : "", m.Success ? ("Error: " + m.Groups[1].Value + " " + m.Groups[4].Value) : t); } catch { }
                }
                return;
            }
            m = ValueRx.Match(t);
            if (!m.Success) return;
            Action<string, string, string> h = ValueReceived;
            if (h != null)
            {
                try { h(m.Groups[2].Value, m.Groups[3].Value, m.Groups[1].Value); } catch { }
            }
        }

        private void Raise(string dir, string text)
        {
            Action<string, string> h = Traffic;
            if (h != null) { try { h(dir, text); } catch { } }
        }

        public void Dispose()
        {
            _stop = true;
            try { Write("(remove-all)"); } catch { }
            _t.Dispose();
        }
    }
}
