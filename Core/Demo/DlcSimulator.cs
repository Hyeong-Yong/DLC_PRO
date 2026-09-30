#nullable disable
// DLC pro 간이 시뮬레이터 (장비 없이 GUI/통신 확인용 — 데모 모드와 회귀 테스트가 함께 사용)
// - Command Reference의 param-ref / param-set! / exec / 모니터링 add/remove 일부만 구현
// - 기본 스펙트럼: 세슘 포화흡수분광(SAS)과 비슷한 모양. 데모 모드는 SpectrumOverride로 Cs D2 편광 분광/1470 nm 스펙트럼을 넣는다
// - 락 후보 검출, 락/언락, 와이드스캔, 레코더를 단순 모델로 흉내
// 실제 장비와는 통신하지 않는다 (루프백 TCP로만 제공: Core/Demo/DemoDlcServer.cs)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace DlcSim
{
    internal sealed class Sim
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private readonly object _lk = new object();
        private readonly Dictionary<string, string> _p = new Dictionary<string, string>();
        private readonly HashSet<string> _ro = new HashSet<string>();
        private readonly Random _rnd = new Random(1);
        private readonly List<string> _messages = new List<string>();
        private DateTime _lockingSince;
        private double _lockX;
        private float[] _bgX, _bgY;
        private DateTime _recStart;
        private readonly List<float[]> _rec = new List<float[]>();
        private double _t;
        private double _wsProg;

        public Sim()
        {
            // 시스템
            RO("emission", "#t"); RO("interlock-open", "#f"); RO("frontkey-locked", "#f");
            RO("system-health-txt", "\"ok\""); RO("serial-number", "\"SIM-0001\""); RO("fw-ver", "\"3.5.1\"");
            RO("ssw-ver", "\"6.3.1\""); RO("system-type", "\"DLCpro\""); RO("system-model", "\"1:DL-TA\"");
            RW("system-label", "\"Simulator\""); RO("uptime-txt", "\"0:00:00\""); RW("ul", "3");
            RO("system-messages:count-new", "0"); RO("system-messages:latest-message", "\"\"");
            RO("net-conf:ip-addr", "\"127.0.0.1\"");
            RO("laser1:type", "\"TApro\""); RO("laser1:product-name", "\"TApro SIM-26086\""); RW("laser1:label", "\"TA pro\"");
            RO("laser1:emission", "#t"); RO("laser1:health-txt", "\"ok\"");
            // 마스터
            RO("laser1:dl:serial-number", "\"DL-SIM\"");
            RW("laser1:dl:cc:enabled", "#t"); RO("laser1:dl:cc:emission", "#t");
            RW("laser1:dl:cc:current-set", "94.5"); RO("laser1:dl:cc:current-act", "94.5"); RW("laser1:dl:cc:current-clip", "106.0");
            RO("laser1:dl:cc:voltage-act", "1.85"); RO("laser1:dl:cc:status-txt", "\"OK\"");
            RW("laser1:dl:cc:feedforward-enabled", "#f"); RW("laser1:dl:cc:feedforward-factor", "0.0");
            RW("laser1:dl:tc:enabled", "#t"); RW("laser1:dl:tc:temp-set", "20.0"); RO("laser1:dl:tc:temp-act", "20.0");
            RO("laser1:dl:tc:ready", "#t"); RO("laser1:dl:tc:status-txt", "\"OK\""); RO("laser1:dl:tc:temp-set-min", "15.0"); RO("laser1:dl:tc:temp-set-max", "30.0");
            RW("laser1:dl:pc:enabled", "#t"); RW("laser1:dl:pc:voltage-set", "70.0"); RO("laser1:dl:pc:voltage-act", "70.0");
            RO("laser1:dl:pc:voltage-min", "-1.0"); RO("laser1:dl:pc:voltage-max", "140.0"); RO("laser1:dl:pc:status-txt", "\"OK\"");
            // 증폭기
            RO("laser1:amp:serial-number", "\"TA-SIM\"");
            RW("laser1:amp:cc:enabled", "#f"); RO("laser1:amp:cc:emission", "#f");
            RW("laser1:amp:cc:current-set", "2540.0"); RO("laser1:amp:cc:current-act", "0.0"); RW("laser1:amp:cc:current-clip", "3920.0");
            RO("laser1:amp:cc:status-txt", "\"OK\"");
            RW("laser1:amp:tc:enabled", "#t"); RW("laser1:amp:tc:temp-set", "20.0"); RO("laser1:amp:tc:temp-act", "20.0"); RO("laser1:amp:tc:ready", "#t");
            RO("laser1:amp:pd:seed:power", "31.4"); RO("laser1:amp:pd:amp:power", "0.0");
            RO("laser1:amp:seed-limits:power-min", "10.0"); RO("laser1:amp:seed-limits:status-txt", "\"OK\"");
            RO("laser1:amp:output-limits:power-max", "2000.0"); RO("laser1:amp:output-limits:status-txt", "\"OK\"");
            // 스캔
            RW("laser1:scan:enabled", "#t"); RW("laser1:scan:hold", "#f"); RW("laser1:scan:signal-type", "1");
            RW("laser1:scan:frequency", "20.0"); RW("laser1:scan:output-channel", "50"); RO("laser1:scan:unit", "\"V\"");
            RW("laser1:scan:amplitude", "50.0"); RW("laser1:scan:offset", "70.0"); RW("laser1:scan:start", "45.0"); RW("laser1:scan:end", "95.0");
            // 스코프
            RW("laser1:scope:variant", "0"); RW("laser1:scope:update-rate", "10");
            RW("laser1:scope:channel1:signal", "100"); RO("laser1:scope:channel1:unit", "\"V\""); RO("laser1:scope:channel1:name", "\"Fine In 1\"");
            RW("laser1:scope:channel2:signal", "-3"); RO("laser1:scope:channel2:unit", "\"\""); RO("laser1:scope:channel2:name", "\"none\"");
            RW("laser1:scope:channelx:xy-signal", "101"); RW("laser1:scope:channelx:scope-timescale", "20.0");
            RW("laser1:scope:channelx:spectrum-range", "20.0"); RO("laser1:scope:channelx:unit", "\"V\""); RO("laser1:scope:channelx:name", "\"Piezo Voltage\"");
            RO("laser1:scope:data", "\"\"");
            // 락
            RW("laser1:dl:lock:type", "1"); RW("laser1:dl:lock:lock-without-lockpoint", "#f");
            RO("laser1:dl:lock:state", "1"); RO("laser1:dl:lock:state-txt", "\"Scanning\""); RW("laser1:dl:lock:lock-enabled", "#f");
            RW("laser1:dl:lock:hold", "#f"); RW("laser1:dl:lock:spectrum-input-channel", "0"); RO("laser1:dl:lock:error-channel", "30");
            RW("laser1:dl:lock:pid-selection", "1"); RW("laser1:dl:lock:setpoint", "0.0"); RW("laser1:dl:lock:locking-delay", "300");
            RO("laser1:dl:lock:lockpoint:position", "(0.0 0.0)"); RO("laser1:dl:lock:lockpoint:type", "\"none\"");
            RO("laser1:dl:lock:candidates", "\"\""); RO("laser1:dl:lock:background-trace", "\"\"");
            RW("laser1:dl:lock:candidate-filter:top", "#t"); RW("laser1:dl:lock:candidate-filter:bottom", "#f");
            RW("laser1:dl:lock:candidate-filter:positive-edge", "#f"); RW("laser1:dl:lock:candidate-filter:negative-edge", "#f");
            RW("laser1:dl:lock:candidate-filter:edge-level", "0.5"); RW("laser1:dl:lock:candidate-filter:peak-noise-tolerance", "0.0");
            for (int n = 1; n <= 2; n++)
            {
                string pf = "laser1:dl:lock:pid" + n + ":";
                RW(pf + "enabled", n == 1 ? "#t" : "#f"); RW(pf + "gain:all", "1.0"); RW(pf + "gain:p", n == 1 ? "0.5" : "1.0");
                RW(pf + "gain:i", "2.0"); RW(pf + "gain:d", "0.0"); RW(pf + "gain:i-cutoff", "10.0"); RW(pf + "gain:i-cutoff-enabled", "#f");
                RW(pf + "sign", "#t"); RW(pf + "slope", "#t"); RW(pf + "output-channel", n == 1 ? "50" : "51");
                RW(pf + "outputlimit:enabled", "#f"); RW(pf + "outputlimit:max", "10.0"); RW(pf + "hold", "#f");
                RO(pf + "lock-state", "#f"); RO(pf + "hold-state", "#f"); RO(pf + "regulating-state", "#f");
            }
            RW("laser1:dl:lock:lockin:modulation-enabled", "#t"); RW("laser1:dl:lock:lockin:modulation-output-channel", "51");
            RW("laser1:dl:lock:lockin:frequency", "20000.0"); RW("laser1:dl:lock:lockin:amplitude", "0.1"); RW("laser1:dl:lock:lockin:phase-shift", "0.0");
            RW("laser1:dl:lock:lockin:lock-level", "0.0"); RO("laser1:dl:lock:lockin:auto-lir:state", "0"); RO("laser1:dl:lock:lockin:auto-lir:progress", "0");
            RW("laser1:dl:lock:relock:enabled", "#f"); RW("laser1:dl:lock:relock:output-channel", "50"); RW("laser1:dl:lock:relock:frequency", "1.0");
            RW("laser1:dl:lock:relock:amplitude", "2.0"); RW("laser1:dl:lock:relock:delay", "0.1"); RW("laser1:dl:lock:reset:enabled", "#f");
            RW("laser1:dl:lock:window:enabled", "#f"); RW("laser1:dl:lock:window:input-channel", "1"); RW("laser1:dl:lock:window:level-high", "5.0");
            RW("laser1:dl:lock:window:level-low", "0.2"); RW("laser1:dl:lock:window:level-hysteresis", "0.05");
            // 출력 안정화
            RW("laser1:power-stabilization:enabled", "#f"); RW("laser1:power-stabilization:gain:all", "1.0"); RW("laser1:power-stabilization:gain:p", "1.0");
            RW("laser1:power-stabilization:gain:i", "1.0"); RW("laser1:power-stabilization:gain:d", "0.0"); RW("laser1:power-stabilization:sign", "#f");
            RW("laser1:power-stabilization:input-channel", "62"); RW("laser1:power-stabilization:setpoint", "1000.0");
            RW("laser1:power-stabilization:window:enabled", "#f"); RW("laser1:power-stabilization:window:level-low", "100.0");
            RW("laser1:power-stabilization:window:level-hysteresis", "10.0"); RW("laser1:power-stabilization:hold-output-on-unlock", "#f");
            RO("laser1:power-stabilization:output-channel", "63"); RO("laser1:power-stabilization:input-channel-value-act", "0.0");
            RO("laser1:power-stabilization:state", "0");
            RW("laser1:pd-ext:input-channel", "1"); RO("laser1:pd-ext:photodiode", "0.0"); RO("laser1:pd-ext:power", "0.0");
            RW("laser1:pd-ext:cal-offset", "0.0"); RW("laser1:pd-ext:cal-factor", "1.0");
            // 와이드스캔
            RO("laser1:wide-scan:state", "0"); RO("laser1:wide-scan:state-txt", "\"disabled\""); RW("laser1:wide-scan:output-channel", "50");
            RW("laser1:wide-scan:scan-begin", "-1.0"); RW("laser1:wide-scan:scan-end", "140.0"); RW("laser1:wide-scan:continuous-mode", "#f");
            RW("laser1:wide-scan:restore-on-end", "#t"); RW("laser1:wide-scan:shape", "0"); RW("laser1:wide-scan:offset", "69.5");
            RW("laser1:wide-scan:amplitude", "141.0"); RW("laser1:wide-scan:speed", "14.1"); RO("laser1:wide-scan:speed-min", "0.01");
            RO("laser1:wide-scan:speed-max", "100.0"); RW("laser1:wide-scan:duration", "10.0"); RW("laser1:wide-scan:value-set", "70.0");
            RO("laser1:wide-scan:value-act", "70.0"); RO("laser1:wide-scan:value-unit", "\"V\""); RW("laser1:wide-scan:recorder-stepsize-set", "0.0");
            RO("laser1:wide-scan:recorder-sample-count", "5000"); RO("laser1:wide-scan:progress", "0"); RO("laser1:wide-scan:remaining-time", "0");
            // 레코더
            RO("laser1:recorder:state", "0"); RO("laser1:recorder:state-txt", "\"disabled\""); RW("laser1:recorder:enabled", "#f");
            RW("laser1:recorder:trigger-mode", "0"); RW("laser1:recorder:inputs:channel1:signal", "0"); RW("laser1:recorder:inputs:channel2:signal", "-3");
            RW("laser1:recorder:inputs:channelx:signal", "-2");
            RW("laser1:recorder:inputs:channel1:low-pass-filter:enabled", "#f"); RW("laser1:recorder:inputs:channel1:low-pass-filter:cut-off-frequency", "1000.0");
            RW("laser1:recorder:inputs:channel2:low-pass-filter:enabled", "#f"); RW("laser1:recorder:inputs:channel2:low-pass-filter:cut-off-frequency", "1000.0");
            RW("laser1:recorder:recording-mode", "0"); RW("laser1:recorder:recording-time", "1000.0"); RW("laser1:recorder:sample-count-set", "5000");
            RO("laser1:recorder:sample-count", "5000"); RO("laser1:recorder:sampling-rate", "5000.0"); RO("laser1:recorder:sampling-interval", "0.2");
            RO("laser1:recorder:data:recorded-sample-count", "0"); RO("laser1:recorder:data:recorded-sampling-interval", "0.2");
            RO("laser1:recorder:data:channel1:name", "\"Fine In 1\""); RO("laser1:recorder:data:channel1:unit", "\"V\"");
            RO("laser1:recorder:data:channel2:name", "\"none\""); RO("laser1:recorder:data:channel2:unit", "\"\"");
            RO("laser1:recorder:data:channelx:name", "\"Time\""); RO("laser1:recorder:data:channelx:unit", "\"ms\"");
            _messages.Add(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", Inv) + " * 0 ( 1001) simulator started");
        }

        private void RO(string n, string v) { _p[n] = v; _ro.Add(n); }
        private void RW(string n, string v) { _p[n] = v; }
        private string G(string n) { lock (_lk) return _p[n]; }
        private void S(string n, string v) { lock (_lk) _p[n] = v; }
        private double D(string n) { return double.Parse(G(n), Inv); }
        private bool B(string n) { return G(n) == "#t"; }
        private int I(string n) { return (int)Math.Round(D(n)); }
        private void SD(string n, double v) { S(n, v.ToString("0.######", Inv)); }
        private static string F(double v) { return v.ToString("0.######", Inv); }

        // ------------------------------------------------------------------
        // 물리 모델
        // ------------------------------------------------------------------

        /// <summary>데모 모드용 스펙트럼 (null이면 기본 SAS 유사 신호).</summary>
        internal Func<double, double> SpectrumOverride;

        /// <summary>읽기 전용 값을 포함해 파라미터를 직접 설정 (데모 초기화용).</summary>
        internal void Force(string n, string v) { lock (_lk) { if (v == null) _p.Remove(n); else _p[n] = v; } }

        /// <summary>Physics 루프 종료 요청.</summary>
        internal volatile bool Stopped;

        /// <summary>세슘 D2 SAS 유사 신호: 도플러 흡수 배경 + Lamb dip(피크) 6개. x: 피에조 전압 V</summary>
        private double Spectrum(double x)
        {
            if (SpectrumOverride != null) return SpectrumOverride(x);
            double doppler = -1.2 * Math.Exp(-Math.Pow((x - 70) / 22.0, 2));
            double[] pos = { 58, 63, 66.5, 70, 73.5, 80 };
            double[] amp = { 0.25, 0.35, 0.55, 0.30, 0.8, 0.45 };
            double s = 2.0 + doppler;
            for (int i = 0; i < pos.Length; i++) s += amp[i] / (1 + Math.Pow((x - pos[i]) / 0.6, 2));
            return s;
        }

        public void Physics()
        {
            DateTime start = DateTime.UtcNow;
            while (!Stopped)
            {
                Thread.Sleep(50);
                lock (_lk)
                {
                    _t = (DateTime.UtcNow - start).TotalSeconds;
                    TimeSpan up = DateTime.UtcNow - start;
                    _p["uptime-txt"] = "\"" + ((int)up.TotalHours) + ":" + up.Minutes.ToString("00") + ":" + up.Seconds.ToString("00") + "\"";
                    // 온도
                    foreach (string pre in new[] { "laser1:dl:tc:", "laser1:amp:tc:" })
                    {
                        double set = D(pre + "temp-set"), act = D(pre + "temp-act");
                        act += (set - act) * 0.05 + (_rnd.NextDouble() - 0.5) * 0.001;
                        _p[pre + "temp-act"] = act.ToString("0.0000", Inv);
                        _p[pre + "ready"] = Math.Abs(set - act) < 0.01 ? "#t" : "#f";
                    }
                    // 마스터 전류
                    bool dlOn = B("laser1:dl:cc:enabled");
                    double dlI = dlOn ? D("laser1:dl:cc:current-set") + (_rnd.NextDouble() - 0.5) * 0.05 : 0;
                    _p["laser1:dl:cc:current-act"] = dlI.ToString("0.000", Inv);
                    _p["laser1:dl:cc:emission"] = dlOn ? "#t" : "#f";
                    double seed = dlOn ? Math.Max(0, (dlI - 40) * 0.58) : 0;
                    _p["laser1:amp:pd:seed:power"] = seed.ToString("0.00", Inv);
                    // 증폭기
                    bool ampOn = B("laser1:amp:cc:enabled");
                    if (ampOn && seed < D("laser1:amp:seed-limits:power-min"))
                    {
                        // 장비 자체 보호 흉내 (수 초 후 차단 대신 즉시)
                    }
                    double ampI = ampOn ? D("laser1:amp:cc:current-set") : 0;
                    _p["laser1:amp:cc:current-act"] = (ampI + (ampOn ? (_rnd.NextDouble() - 0.5) : 0)).ToString("0.0", Inv);
                    _p["laser1:amp:cc:emission"] = ampOn ? "#t" : "#f";
                    double ampP = ampOn ? Math.Max(0, (ampI - 500) * 0.69 * Math.Min(1, seed / 20.0)) : 0;
                    _p["laser1:amp:pd:amp:power"] = ampP.ToString("0.00", Inv);
                    _p["laser1:power-stabilization:input-channel-value-act"] = ampP.ToString("0.00", Inv);
                    _p["emission"] = (dlOn || ampOn) ? "#t" : "#f";
                    _p["laser1:emission"] = _p["emission"];
                    // 피에조
                    double pv = D("laser1:dl:pc:voltage-set");
                    int st = I("laser1:dl:lock:state");
                    if (st == 5) pv = _lockX + 0.02 * Math.Sin(_t * 7);
                    _p["laser1:dl:pc:voltage-act"] = F(pv + (_rnd.NextDouble() - 0.5) * 0.01);
                    // 락 상태 전이
                    if (st == 4 && (DateTime.UtcNow - _lockingSince).TotalMilliseconds >= D("laser1:dl:lock:locking-delay"))
                    {
                        SetLockState(5);
                        _p["laser1:scan:enabled"] = "#f";
                        _p["laser1:dl:lock:pid1:lock-state"] = "#t"; _p["laser1:dl:lock:pid1:regulating-state"] = "#t";
                    }
                    // 와이드스캔
                    int ws = I("laser1:wide-scan:state");
                    if (ws == 2)
                    {
                        double dur = Math.Max(0.5, D("laser1:wide-scan:duration"));
                        _wsProg += 5.0 / dur; double prog = _wsProg;
                        double b0 = D("laser1:wide-scan:scan-begin"), b1 = D("laser1:wide-scan:scan-end");
                        _p["laser1:wide-scan:value-act"] = F(b0 + (b1 - b0) * Math.Min(1, prog / 100));
                        _p["laser1:wide-scan:remaining-time"] = ((int)(dur * (1 - prog / 100))).ToString(Inv);
                        if (prog >= 100)
                        {
                            prog = 100;
                            _p["laser1:wide-scan:state"] = "0"; _p["laser1:wide-scan:state-txt"] = "\"disabled\"";
                            FillRecorderWideScan(b0, b1);
                        }
                        _p["laser1:wide-scan:progress"] = ((int)prog).ToString(Inv);
                    }
                    // 레코더
                    if (I("laser1:recorder:state") == 2 && (DateTime.UtcNow - _recStart).TotalMilliseconds >= D("laser1:recorder:recording-time"))
                    {
                        FillRecorderTime();
                        _p["laser1:recorder:state"] = "0"; _p["laser1:recorder:state-txt"] = "\"disabled\""; _p["laser1:recorder:enabled"] = "#f";
                    }
                }
            }
        }

        private void SetLockState(int s)
        {
            string[] names = { "Idle", "Scanning", "Selecting", "Selected", "Locking", "Locked", "On Hold", "Resetting", "Reset", "Relocking" };
            _p["laser1:dl:lock:state"] = s.ToString(Inv);
            _p["laser1:dl:lock:state-txt"] = "\"" + names[s] + "\"";
            _p["laser1:dl:lock:lock-enabled"] = s >= 4 ? "#t" : "#f";
            if (s < 4)
            {
                _p["laser1:dl:lock:pid1:lock-state"] = "#f"; _p["laser1:dl:lock:pid1:regulating-state"] = "#f";
            }
        }

        private void FillRecorderWideScan(double b0, double b1)
        {
            _rec.Clear();
            int n = 5000;
            for (int i = 0; i < n; i++)
            {
                double x = b0 + (b1 - b0) * i / (n - 1);
                _rec.Add(new[] { (float)x, (float)(Spectrum(x) + (_rnd.NextDouble() - 0.5) * 0.02), (float)(D("laser1:dl:cc:current-act")) });
            }
            _p["laser1:recorder:data:recorded-sample-count"] = n.ToString(Inv);
            _p["laser1:recorder:data:channelx:name"] = "\"Piezo Voltage\""; _p["laser1:recorder:data:channelx:unit"] = "\"V\"";
        }

        private void FillRecorderTime()
        {
            _rec.Clear();
            int n = I("laser1:recorder:sample-count");
            double dt = D("laser1:recorder:recording-time") / n;
            for (int i = 0; i < n; i++)
            {
                double tt = i * dt;
                _rec.Add(new[] { (float)tt, (float)(0.5 + 0.1 * Math.Sin(tt / 10.0) + (_rnd.NextDouble() - 0.5) * 0.02), 0f });
            }
            _p["laser1:recorder:data:recorded-sample-count"] = n.ToString(Inv);
            _p["laser1:recorder:data:channelx:name"] = "\"Time\""; _p["laser1:recorder:data:channelx:unit"] = "\"ms\"";
        }

        private string ScopeData()
        {
            int n = 1000;
            float[] x = new float[n], y = new float[n];
            int st = I("laser1:dl:lock:state");
            double off = D("laser1:scan:offset"), amp = D("laser1:scan:amplitude");
            bool scanning = B("laser1:scan:enabled") && st < 4;
            for (int i = 0; i < n; i++)
            {
                double xv;
                if (scanning) xv = off - amp / 2 + amp * i / (n - 1);
                else if (st >= 4) xv = _lockX + (_rnd.NextDouble() - 0.5) * 0.05; // 락 중: 락 포인트 주변에 점이 모임
                else xv = D("laser1:dl:pc:voltage-set");
                x[i] = (float)xv;
                y[i] = (float)(Spectrum(xv) + (_rnd.NextDouble() - 0.5) * 0.015);
            }
            var blocks = new List<KeyValuePair<char, byte[]>>
            {
                new KeyValuePair<char, byte[]>('x', ToBytes(x)),
                new KeyValuePair<char, byte[]>('y', ToBytes(y)),
            };
            return "\"" + Convert.ToBase64String(Build(blocks)) + "\"";
        }

        private List<float[]> FindCandidates()
        {
            // 스캔 구간 내 극대점(top) 또는 가장자리(edge) 검출
            List<float[]> c = new List<float[]>();
            double off = D("laser1:scan:offset"), amp = D("laser1:scan:amplitude");
            int n = 1000;
            double[] ys = new double[n], xs = new double[n];
            for (int i = 0; i < n; i++) { xs[i] = off - amp / 2 + amp * i / (n - 1); ys[i] = Spectrum(xs[i]); }
            int type = I("laser1:dl:lock:type");
            for (int i = 2; i < n - 2; i++)
            {
                if (type != 2)
                {
                    if (ys[i] > ys[i - 1] && ys[i] >= ys[i + 1] && ys[i] > ys[i - 2] && ys[i] >= ys[i + 2])
                        c.Add(new[] { (float)xs[i], (float)ys[i], 1f });
                }
                else
                {
                    double lvl = D("laser1:dl:lock:candidate-filter:edge-level");
                    if ((ys[i - 1] - lvl) * (ys[i] - lvl) < 0)
                        c.Add(new[] { (float)xs[i], (float)lvl, ys[i] > ys[i - 1] ? 3f : 4f });
                }
            }
            return c;
        }

        private string CandidatesBlob()
        {
            int st = I("laser1:dl:lock:state");
            List<KeyValuePair<char, byte[]>> blocks = new List<KeyValuePair<char, byte[]>>();
            blocks.Add(new KeyValuePair<char, byte[]>('s', new[] { (byte)st }));
            if (st >= 2)
            {
                List<float[]> c = FindCandidates();
                byte[] cb = new byte[c.Count * 9];
                for (int i = 0; i < c.Count; i++) PutCand(cb, i * 9, c[i][0], c[i][1], (byte)c[i][2]);
                blocks.Add(new KeyValuePair<char, byte[]>('c', cb));
            }
            if (st >= 3)
            {
                List<string> pos = Tuple(G("laser1:dl:lock:lockpoint:position"));
                byte[] lb = new byte[9];
                string tn = G("laser1:dl:lock:lockpoint:type").Trim('"');
                byte tb = (byte)(tn == "top" ? 1 : tn == "bottom" ? 2 : tn == "positive-edge" ? 3 : tn == "negative-edge" ? 4 : 0);
                PutCand(lb, 0, float.Parse(pos[0], Inv), float.Parse(pos[1], Inv), tb);
                blocks.Add(new KeyValuePair<char, byte[]>('l', lb));
            }
            return "\"" + Convert.ToBase64String(Build(blocks)) + "\"";
        }

        private static void PutCand(byte[] b, int o, float x, float y, byte t)
        {
            Array.Copy(BitConverter.GetBytes(x), 0, b, o, 4);
            Array.Copy(BitConverter.GetBytes(y), 0, b, o + 4, 4);
            b[o + 8] = t;
        }

        private static byte[] ToBytes(float[] f)
        {
            byte[] b = new byte[f.Length * 4];
            for (int i = 0; i < f.Length; i++) Array.Copy(BitConverter.GetBytes(f[i]), 0, b, i * 4, 4);
            return b;
        }

        private static byte[] Build(List<KeyValuePair<char, byte[]>> blocks)
        {
            List<byte> o = new List<byte>();
            foreach (var kv in blocks)
            {
                o.Add((byte)kv.Key);
                o.AddRange(Encoding.ASCII.GetBytes(kv.Value.Length.ToString(Inv)));
                o.Add(0);
                o.AddRange(kv.Value);
            }
            return o.ToArray();
        }

        private static List<string> Tuple(string s)
        {
            s = s.Trim().TrimStart('\'').Trim('(', ')');
            return s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        // ------------------------------------------------------------------
        // 명령 해석
        // ------------------------------------------------------------------

        private static readonly Regex RefRx = new Regex(@"^\(param-ref\s+'([^\s\)]+)\s*\)$");
        private static readonly Regex SetRx = new Regex(@"^\(param-set!\s+'([^\s\)]+)\s+(.+)\)$");
        private static readonly Regex ExecRx = new Regex(@"^\(exec\s+'([^\s\)]+)\s*(.*)\)$");
        private static readonly Regex DispRx = new Regex(@"^\(param-disp(?:\s+'([^\s\)]+))?\s*\)$");
        private static readonly Regex AddRx = new Regex(@"^\(add\s+'([^\s\)]+)(?:\s+(\S+))?(?:\s+(\S+))?\s*\)$");
        private static readonly Regex RemoveRx = new Regex(@"^\(remove\s+'([^\s\)]+)\s*\)$");

        public void ServeCommand(TcpClient c)
        {
            NetworkStream s = c.GetStream();
            StreamReader r = new StreamReader(s, Encoding.UTF8);
            StreamWriter w = new StreamWriter(s, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };
            w.Write("DeCoF Command Line (DLC pro simulator)\n\n> ");
            string line;
            while ((line = r.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length == 0) { w.Write("> "); continue; }
                if (line == "(quit)") break;
                string resp;
                try { resp = Eval(line); }
                catch (Exception ex) { resp = "Error: -1 " + ex.Message; }
                w.Write(resp + "\n> ");
            }
        }

        internal string Eval(string line)
        {
            Match m;
            if ((m = RefRx.Match(line)).Success)
            {
                string n = m.Groups[1].Value;
                if (n == "laser1:scope:data") lock (_lk) return ScopeData();
                if (n == "laser1:dl:lock:candidates") lock (_lk) return CandidatesBlob();
                if (n == "laser1:dl:lock:background-trace")
                    lock (_lk) return _bgX == null ? "\"\"" : "\"" + Convert.ToBase64String(Build(new List<KeyValuePair<char, byte[]>> { new KeyValuePair<char, byte[]>('x', ToBytes(_bgX)), new KeyValuePair<char, byte[]>('y', ToBytes(_bgY)) })) + "\"";
                lock (_lk) { string v; return _p.TryGetValue(n, out v) ? v : "Error: -3 no such parameter"; }
            }
            if ((m = SetRx.Match(line)).Success) return Set(m.Groups[1].Value, m.Groups[2].Value.Trim());
            if ((m = ExecRx.Match(line)).Success) return Exec(m.Groups[1].Value, m.Groups[2].Value.Trim());
            if ((m = DispRx.Match(line)).Success)
            {
                string pre = m.Groups[1].Success ? m.Groups[1].Value : "";
                StringBuilder sb = new StringBuilder();
                lock (_lk)
                    foreach (var kv in _p.OrderBy(k => k.Key))
                        if (kv.Key.StartsWith(pre) && kv.Key != "laser1:scope:data") sb.Append(kv.Key).Append(" = ").Append(kv.Value).Append('\n');
                return sb.ToString() + "0";
            }
            if (line.StartsWith("(+ ")) return "0";
            return "Error: -1 unsupported command in simulator";
        }

        private string Set(string n, string v)
        {
            lock (_lk)
            {
                if (!_p.ContainsKey(n)) return "Error: -3 no such parameter";
                if (_ro.Contains(n)) return "Error: -11 parameter not settable";
                if (v.StartsWith("'")) v = v.Substring(1);
                string old = _p[n];
                // 형식 검사
                if ((old == "#t" || old == "#f") && v != "#t" && v != "#f") return "Error: -1 invalid argument";
                double dv;
                bool numeric = double.TryParse(old, NumberStyles.Float, Inv, out dv);
                int code = 0;
                if (numeric)
                {
                    if (!double.TryParse(v, NumberStyles.Float, Inv, out dv)) return "Error: -1 invalid argument";
                    if (n == "laser1:dl:cc:current-set" && dv > D("laser1:dl:cc:current-clip")) { dv = D("laser1:dl:cc:current-clip"); code = 2; }
                    if (n == "laser1:amp:cc:current-set" && dv > D("laser1:amp:cc:current-clip")) { dv = D("laser1:amp:cc:current-clip"); code = 2; }
                    if (n == "laser1:dl:pc:voltage-set") { double c2 = Math.Max(-1, Math.Min(140, dv)); if (c2 != dv) code = 2; dv = c2; }
                    v = (old.Contains(".") || v.Contains(".")) ? F(dv) : ((long)dv).ToString(Inv);
                }
                _p[n] = v;
                // 부수효과
                if (n == "laser1:scan:enabled" && v == "#t" && I("laser1:dl:lock:state") == 0) SetLockState(1);
                if (n == "laser1:scan:enabled" && v == "#f" && I("laser1:dl:lock:state") <= 3) SetLockState(0);
                if (n == "laser1:scan:amplitude" || n == "laser1:scan:offset")
                {
                    _p["laser1:scan:start"] = F(D("laser1:scan:offset") - D("laser1:scan:amplitude") / 2);
                    _p["laser1:scan:end"] = F(D("laser1:scan:offset") + D("laser1:scan:amplitude") / 2);
                }
                if (n == "laser1:dl:lock:lock-enabled")
                {
                    if (v == "#t")
                    {
                        string r = Close();
                        if (r.StartsWith("Error")) { _p[n] = old; return r; }
                    }
                    else Open();
                }
                if (n == "laser1:recorder:enabled" && v == "#t") { _recStart = DateTime.UtcNow; _p["laser1:recorder:state"] = "2"; _p["laser1:recorder:state-txt"] = "\"recording\""; }
                return code.ToString(Inv);
            }
        }

        private string Close()
        {
            int st = I("laser1:dl:lock:state");
            bool wlp = B("laser1:dl:lock:lock-without-lockpoint");
            if (!(st == 3 && !wlp) && !(st == 1 && wlp)) return "Error: -1 lock can not be closed in current state";
            _lockX = wlp ? D("laser1:scan:offset") : double.Parse(Tuple(G("laser1:dl:lock:lockpoint:position"))[0], Inv);
            // 배경 트레이스 저장
            int n = 1000; _bgX = new float[n]; _bgY = new float[n];
            double off = D("laser1:scan:offset"), amp = D("laser1:scan:amplitude");
            for (int i = 0; i < n; i++) { double x = off - amp / 2 + amp * i / (n - 1); _bgX[i] = (float)x; _bgY[i] = (float)Spectrum(x); }
            _lockingSince = DateTime.UtcNow;
            SetLockState(4);
            return "()";
        }

        private void Open()
        {
            if (I("laser1:dl:lock:state") < 4) return;
            _p["laser1:scan:enabled"] = "#t";
            _p["laser1:dl:pc:voltage-set"] = F(D("laser1:scan:offset"));
            SetLockState(3);
            _bgX = _bgY = null;
        }

        private string Exec(string n, string args)
        {
            lock (_lk)
            {
                List<string> a = Tuple("(" + args + ")");
                switch (n)
                {
                    case "laser1:dl:lock:find-candidates":
                        if (!B("laser1:scan:enabled")) return "Error: -1 scan not enabled";
                        SetLockState(2); return "()";
                    case "laser1:dl:lock:select-lockpoint":
                        {
                            int st = I("laser1:dl:lock:state");
                            if (st >= 4) return "Error: -1 lock is closed";
                            if (st < 2) SetLockState(2);
                            double x = double.Parse(a[0], Inv);
                            List<float[]> c = FindCandidates();
                            if (c.Count == 0) return "Error: -1 no candidate";
                            float[] best = c.OrderBy(k => Math.Abs(k[0] - x)).First();
                            string tn = best[2] == 1 ? "top" : best[2] == 2 ? "bottom" : best[2] == 3 ? "positive-edge" : "negative-edge";
                            _p["laser1:dl:lock:lockpoint:position"] = "(" + F(best[0]) + " " + F(best[1]) + ")";
                            _p["laser1:dl:lock:lockpoint:type"] = "\"" + tn + "\"";
                            _p["laser1:dl:lock:setpoint"] = F(best[2] >= 3 ? best[1] : 0);
                            SetLockState(3); return "()";
                        }
                    case "laser1:dl:lock:close": return Close();
                    case "laser1:dl:lock:open": Open(); return "()";
                    case "laser1:dl:lock:lockin:auto-lir:start": return "()";
                    case "laser1:dl:lock:lockin:auto-lir:abort": return "()";
                    case "laser1:wide-scan:start":
                        if (B("laser1:scan:enabled")) return "()"; // 매뉴얼: scan:enabled가 #f일 때만 동작
                        _p["laser1:wide-scan:state"] = "2"; _p["laser1:wide-scan:state-txt"] = "\"scan active\""; _p["laser1:wide-scan:progress"] = "0"; _wsProg = 0;
                        return "()";
                    case "laser1:wide-scan:stop":
                        _p["laser1:wide-scan:state"] = "0"; _p["laser1:wide-scan:state-txt"] = "\"disabled\""; return "()";
                    case "laser1:recorder:data:clear-data": _rec.Clear(); _p["laser1:recorder:data:recorded-sample-count"] = "0"; return "()";
                    case "laser1:recorder:data:get-data":
                        {
                            int start = int.Parse(a[0], Inv), count = Math.Min(1024, int.Parse(a[1], Inv));
                            int got = Math.Max(0, Math.Min(count, _rec.Count - start));
                            float[] x = new float[got], y = new float[got], Y = new float[got];
                            for (int i = 0; i < got; i++) { x[i] = _rec[start + i][0]; y[i] = _rec[start + i][1]; Y[i] = _rec[start + i][2]; }
                            byte[] ib = new byte[8];
                            Array.Copy(BitConverter.GetBytes(start), 0, ib, 0, 4); Array.Copy(BitConverter.GetBytes(got), 0, ib, 4, 4);
                            var blocks = new List<KeyValuePair<char, byte[]>> { new KeyValuePair<char, byte[]>('i', ib), new KeyValuePair<char, byte[]>('x', ToBytes(x)), new KeyValuePair<char, byte[]>('y', ToBytes(y)), new KeyValuePair<char, byte[]>('Y', ToBytes(Y)) };
                            return "\"" + Convert.ToBase64String(Build(blocks)) + "\"";
                        }
                    case "system-messages:show-all":
                    case "system-messages:show-new":
                    case "system-messages:show-log":
                    case "system-messages:show-persistent":
                        return string.Join("\n", _messages) + "\n()";
                    case "system-messages:mark-as-read": return "()";
                    case "change-ul":
                        {
                            int ul = int.Parse(a[0], Inv);
                            if (ul < 3) return "Error: -9 wrong password";
                            _p["ul"] = ul.ToString(Inv); return ul.ToString(Inv);
                        }
                    default: return "Error: -3 no such command";
                }
            }
        }

        // ------------------------------------------------------------------
        // 모니터링 라인
        // ------------------------------------------------------------------

        public void ServeMonitor(TcpClient c)
        {
            NetworkStream s = c.GetStream();
            StreamReader r = new StreamReader(s, Encoding.UTF8);
            StreamWriter w = new StreamWriter(s, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };
            object wl = new object();
            Dictionary<string, int> subs = new Dictionary<string, int>();
            Dictionary<string, string> last = new Dictionary<string, string>();
            Dictionary<string, DateTime> lastSent = new Dictionary<string, DateTime>();
            bool alive = true;
            Thread push = new Thread(() =>
            {
                while (alive)
                {
                    Thread.Sleep(20);
                    List<string> outLines = new List<string>();
                    lock (subs)
                    {
                        foreach (var kv in subs)
                        {
                            DateTime ls; lastSent.TryGetValue(kv.Key, out ls);
                            if ((DateTime.UtcNow - ls).TotalMilliseconds < kv.Value) continue;
                            string v; lock (_lk) _p.TryGetValue(kv.Key, out v);
                            string lv; last.TryGetValue(kv.Key, out lv);
                            if (v == null || v == lv) continue;
                            last[kv.Key] = v; lastSent[kv.Key] = DateTime.UtcNow;
                            outLines.Add("(" + Ts() + " '" + kv.Key + " " + v + ")\n");
                        }
                    }
                    foreach (string ol in outLines)
                        try { lock (wl) w.Write(ol); } catch { alive = false; }
                }
            }) { IsBackground = true };
            push.Start();
            string line;
            try
            {
                while ((line = r.ReadLine()) != null)
                {
                    line = line.Trim();
                    Match m;
                    if ((m = AddRx.Match(line)).Success)
                    {
                        string n = m.Groups[1].Value;
                        int per = m.Groups[2].Success ? (int)double.Parse(m.Groups[2].Value, Inv) : 100;
                        string v; lock (_lk) _p.TryGetValue(n, out v);
                        if (v == null) { lock (wl) w.Write("(Error: -3 (" + Ts() + " '" + n + ") no such parameter)\n"); continue; }
                        lock (subs) { subs[n] = per; last[n] = v; lastSent[n] = DateTime.UtcNow; }
                        lock (wl) w.Write("(" + Ts() + " '" + n + " " + v + ")\n");
                    }
                    else if ((m = RemoveRx.Match(line)).Success) { lock (subs) subs.Remove(m.Groups[1].Value); }
                    else if (line == "(remove-all)") { lock (subs) subs.Clear(); }
                }
            }
            finally { alive = false; }
        }

        private static string Ts() { return DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", Inv); }
    }
}
