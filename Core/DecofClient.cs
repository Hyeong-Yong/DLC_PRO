#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace DLC_PRO.Core
{
    /// <summary>
    /// DLC pro 명령 라인(Command Console) 클라이언트.
    /// 명령은 LF로 끝나고, 응답은 줄 맨 앞의 "&gt; " 프롬프트로 끝난다 (Command Reference 2장, 4.4.1).
    /// 모든 메서드는 스레드 안전하다 (내부 lock).
    /// </summary>
    public sealed class DecofClient : IDisposable
    {
        private readonly ITransport _t;
        private readonly object _sync;
        private readonly MemoryStream _rx;
        private readonly byte[] _buf;
        private bool _broken;
        private readonly DecofClient _owner;
        private readonly int _laserId = 1;
        private DecofClient(DecofClient owner, int laserId) { _owner = owner; _laserId = laserId; }
        internal DecofClient ForLaser(int id) => id == 1 ? this : new DecofClient(this, id);
        private string Map(string name) => LaserAddress.Map(name, _laserId);

        /// <summary>송수신 로그 (방향 "TX"/"RX", 내용). 호출 스레드에서 발생.</summary>
        public event Action<string, string> Traffic;

        public string Description { get { return _owner?.Description ?? _t.Description; } }
        public bool IsBroken { get { return _owner?.IsBroken ?? _broken; } }
        public string WelcomeText { get; private set; }

        public DecofClient(ITransport transport, bool isSerial = false)
        {
            // 레이저별 뷰는 소유 연결의 버퍼와 잠금을 사용한다.
            _sync = new object();
            _rx = new MemoryStream();
            _buf = new byte[65536];
            _t = transport;
            try
            {
                if (isSerial)
                {
                    // Python SDK와 동일: 에코 해제(0x12) + 해석기 상태 취소(0x03) 후 프롬프트 비우기
                    _t.Write("\x12\x03");
                    try { ReadUntilPrompt(1500); } catch (TimeoutException) { }
                    try { ReadUntilPrompt(500); } catch (TimeoutException) { }
                    DrainQuietLine(300);
                    WelcomeText = "";
                }
                else
                {
                    // 접속 시 환영 메시지 + 프롬프트 수신
                    WelcomeText = ReadUntilPrompt(5000);
                }
            }
            catch
            {
                try { _t.Dispose(); } catch { }
                throw;
            }
        }

        /// <summary>한 줄 명령을 보내고 프롬프트 직전까지의 응답 전체를 반환.</summary>
        public string Send(string command, int timeoutMs = 3000)
        {
            HardwareAccessPolicy.ValidateConsole(command);
            return SendCore(command, timeoutMs);
        }

        // Only validated typed operations or read-only console requests reach the transport.
        private string SendCore(string command, int timeoutMs)
        {
            if (_owner != null) return _owner.SendCore(command, timeoutMs);
            lock (_sync)
            {
                if (_broken) throw new IOException("연결이 끊어졌습니다.");
                string cmd = (command ?? "").TrimEnd('\r', '\n');
                // 줄바꿈이 섞이면 명령이 두 개로 나뉘어 응답(프롬프트) 짝이 어긋나므로 거부
                if (cmd.IndexOf('\n') >= 0 || cmd.IndexOf('\r') >= 0)
                    throw new ArgumentException("명령에 줄바꿈 문자를 넣을 수 없습니다.");
                try
                {
                    DiscardStale();
                    Raise("TX", cmd);
                    _t.Write(cmd + "\n");
                    string resp = ReadUntilPrompt(timeoutMs);
                    Raise("RX", resp);
                    return resp;
                }
                catch (TimeoutException)
                {
                    // 응답이 늦게 도착하면 다음 명령과 섞이므로 연결을 폐기 대상으로 표시
                    _broken = true;
                    throw;
                }
                catch (Exception)
                {
                    // 전송/수신 중 예외(소켓 닫힘, USB 분리 등)는 모두 연결 폐기로 처리
                    _broken = true;
                    throw;
                }
            }
        }

        /// <summary>
        /// 이전 명령의 늦은 응답 등 남아 있는 데이터를 버린다 (응답이 한 칸씩 밀리는 것 방지).
        /// </summary>
        private void DiscardStale()
        {
            int stale = (int)_rx.Length;
            int avail = 0;
            try { avail = _t.BytesAvailable; } catch { }
            while (avail > 0)
            {
                int n = _t.Read(_buf, 0, Math.Min(_buf.Length, avail));
                stale += n;
                try { avail = _t.BytesAvailable; } catch { avail = 0; }
            }
            if (stale > 0)
            {
                _rx.SetLength(0);
                Raise("WARN", "예상치 못한 수신 데이터 " + stale + " bytes 버림 (" + _t.Description + ")");
            }
        }

        /// <summary>일정 시간 추가 데이터가 없을 때까지 읽어 버린다 (USB 초기화용).</summary>
        private void DrainQuietLine(int quietMs)
        {
            int old = _t.ReadTimeoutMs;
            var elapsed = Stopwatch.StartNew();
            try
            {
                _t.ReadTimeoutMs = quietMs;
                while (true)
                {
                    if (elapsed.ElapsedMilliseconds > 3000) throw new TimeoutException("USB 초기화 중 수신이 멈추지 않습니다.");
                    try { if (_t.Read(_buf, 0, _buf.Length) == 0) throw new IOException("연결 종료됨"); }
                    catch (TimeoutException) { break; }
                }
            }
            finally { _t.ReadTimeoutMs = old; _rx.SetLength(0); }
        }

        // ------------------------------------------------------------------
        // 파라미터 / 명령 헬퍼
        // ------------------------------------------------------------------

        /// <summary>(param-ref 'name) → 원시 값 문자열 (마지막 줄).</summary>
        public string ParamRef(string name, int timeoutMs = 3000)
        {
            name = Map(name);
            HardwareAccessPolicy.ValidateName(name);
            string resp = SendCore("(param-ref '" + name + ")", timeoutMs);
            string last = LastLine(resp);
            if (DecofValue.IsError(last) || DecofValue.IsError(FirstLine(resp)))
                throw DecofException.FromErrorLine(name, DecofValue.IsError(last) ? last : FirstLine(resp));
            return last;
        }

        /// <summary>(param-set! 'name value) → 상태 코드 (0 성공, 양수 경고: 2 = clip).</summary>
        public int ParamSet(string name, object value, int timeoutMs = 3000)
        {
            name = Map(name);
            HardwareAccessPolicy.ValidateWrite(name, value);
            string enc = DecofValue.Encode(value);
            string resp = SendCore("(param-set! '" + name + " " + enc + ")", timeoutMs);
            string last = LastLine(resp);
            if (DecofValue.IsError(last)) throw DecofException.FromErrorLine(name, last);
            int code;
            if (!DecofValue.TryInt(last, out code))
                throw new DecofException(name + ": 예상치 못한 응답 '" + last + "'");
            if (code < 0) throw new DecofException(name + ": 오류 코드 " + code, code);
            return code;
        }

        /// <summary>(exec 'name args...) → 응답 전체 (출력 텍스트 + 마지막 줄 반환값).</summary>
        public string Exec(string name, object[] args, int timeoutMs = 10000)
        {
            name = Map(name);
            HardwareAccessPolicy.ValidateExec(name, args);
            StringBuilder sb = new StringBuilder("(exec '").Append(name);
            if (args != null)
                foreach (object a in args) sb.Append(' ').Append(DecofValue.Encode(a));
            sb.Append(')');
            string resp = SendCore(sb.ToString(), timeoutMs);
            string first = FirstLine(resp), last = LastLine(resp);
            if (DecofValue.IsError(first)) throw DecofException.FromErrorLine(name, first);
            if (DecofValue.IsError(last)) throw DecofException.FromErrorLine(name, last);
            return resp;
        }

        public string Exec(string name) { return Exec(name, null); }

        public double GetDouble(string name)
        {
            string raw = ParamRef(name); double v;
            if (!DecofValue.TryDouble(raw, out v)) throw new DecofException(name + ": 실수 아님 '" + raw + "'");
            return v;
        }

        public int GetInt(string name)
        {
            string raw = ParamRef(name); int v;
            if (!DecofValue.TryInt(raw, out v)) throw new DecofException(name + ": 정수 아님 '" + raw + "'");
            return v;
        }

        public bool GetBool(string name)
        {
            string raw = ParamRef(name); bool v;
            if (!DecofValue.TryBool(raw, out v)) throw new DecofException(name + ": 불리언 아님 '" + raw + "'");
            return v;
        }

        public string GetString(string name) { return DecofValue.Str(ParamRef(name)); }

        public byte[] GetBinary(string name, int timeoutMs = 5000) { return DecofValue.Binary(ParamRef(name, timeoutMs)); }

        /// <summary>연결 종료 시 (quit) 전송 (실패 무시).</summary>
        public void Quit()
        {
            // 다른 스레드가 응답 대기 중이면 기다리지 않는다 (종료 지연 방지)
            if (!Monitor.TryEnter(_sync, 200)) return;
            try { if (!_broken) _t.Write("(quit)\n"); } catch { }
            finally { Monitor.Exit(_sync); }
        }

        public void Dispose()
        {
            if (_owner != null) return;
            Quit();
            _t.Dispose();
        }

        // ------------------------------------------------------------------
        // 내부
        // ------------------------------------------------------------------

        public static string LastLine(string resp)
        {
            if (string.IsNullOrEmpty(resp)) return "";
            string[] lines = resp.Replace("\r", "").Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--)
                if (lines[i].Trim().Length > 0) return lines[i].Trim();
            return "";
        }

        public static string FirstLine(string resp)
        {
            if (string.IsNullOrEmpty(resp)) return "";
            string[] lines = resp.Replace("\r", "").Split('\n');
            foreach (string l in lines) if (l.Trim().Length > 0) return l.Trim();
            return "";
        }

        /// <summary>"\n&gt; " (또는 버퍼 맨 앞의 "&gt; ")가 나올 때까지 읽는다. 프롬프트는 제외하고 반환.</summary>
        private string ReadUntilPrompt(int timeoutMs)
        {
            var elapsed = Stopwatch.StartNew();
            while (true)
            {
                string found = TryExtract();
                if (found != null) return found;
                int remain = timeoutMs - (int)elapsed.ElapsedMilliseconds;
                if (remain <= 0) throw new TimeoutException("프롬프트 대기 시간 초과 (" + _t.Description + ")");
                _t.ReadTimeoutMs = Math.Max(50, remain);
                int n = _t.Read(_buf, 0, _buf.Length);
                if (n == 0) throw new IOException("장비가 연결을 종료했습니다.");
                _rx.Write(_buf, 0, n);
            }
        }

        private string TryExtract()
        {
            byte[] data = _rx.GetBuffer();
            int len = (int)_rx.Length;
            int idx = -1, promptLen = 0;
            if (len >= 2 && data[0] == (byte)'>' && data[1] == (byte)' ') { idx = 0; promptLen = 2; }
            else
            {
                for (int i = 0; i + 2 < len; i++)
                {
                    if (data[i] == (byte)'\n' && data[i + 1] == (byte)'>' && data[i + 2] == (byte)' ')
                    { idx = i; promptLen = 3; break; }
                }
            }
            if (idx < 0) return null;
            string text = Encoding.UTF8.GetString(data, 0, idx);
            int consumed = idx + promptLen;
            byte[] rest = new byte[len - consumed];
            Array.Copy(data, consumed, rest, 0, rest.Length);
            _rx.SetLength(0);
            _rx.Write(rest, 0, rest.Length);
            return text.TrimEnd('\r');
        }

        private void Raise(string dir, string text)
        {
            Action<string, string> h = Traffic;
            if (h != null) { try { h(dir, text); } catch { } }
        }
    }
}
