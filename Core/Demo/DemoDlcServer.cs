#nullable disable
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DLC_PRO.Core.Calibration;

namespace DLC_PRO.Core.Demo
{
    /// <summary>
    /// 데모 모드: 레이저 2대가 달린 가상 DLC pro (하드웨어 없이 제어 UI 확인·개발용).
    /// - 루프백(127.0.0.1)의 빈 포트에서만 명령 라인/모니터링 라인을 연다 → 실제 장비·네트워크와 무관
    /// - Laser 1 "TA852": Cs D2 F=4 → F'=3,4,5 편광 분광 스펙트럼 (328 MHz/V)
    /// - Laser 2 "TA1470": Cs 6P3/2 → 7S1/2 F''=3,4 스펙트럼 (328 MHz/V)
    /// - laser2: 로 시작하는 요청은 두 번째 시뮬레이터의 laser1: 로 바꿔 전달 (실제 장비의 레이저 2 주소 체계)
    /// </summary>
    public sealed class DemoDlcServer : IDisposable
    {
        /// <summary>데모 스펙트럼의 튜닝 계수 [MHz/V].</summary>
        public const double DemoMHzPerV = 328;

        private readonly DlcSim.Sim _first = new DlcSim.Sim(), _second = new DlcSim.Sim();
        private readonly TcpListener _cmd = new TcpListener(IPAddress.Loopback, 0), _mon = new TcpListener(IPAddress.Loopback, 0);
        private readonly ConcurrentBag<TcpClient> _clients = new ConcurrentBag<TcpClient>();
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private static readonly Regex Laser2 = new Regex(@"^\((?:param-ref|param-set!|exec|param-disp) '(laser2:[^\s)]+)");
        private static readonly Regex AddRx = new Regex(@"^\(add '([^\s)]+)");

        public int CommandPort => ((IPEndPoint)_cmd.LocalEndpoint).Port;
        public int MonitorPort => ((IPEndPoint)_mon.LocalEndpoint).Port;

        public static DemoDlcServer Start() => new DemoDlcServer();

        private DemoDlcServer()
        {
            Configure(_first, "\"TA852 (DEMO)\"", "\"TApro DEMO-852\"", PolarizationSpectrum, 70.0, 3.0);
            Configure(_second, "\"TA1470 (DEMO)\"", "\"TApro DEMO-1470\"", LadderSpectrum, 70.0, 10.0);
            Set(_first, "system-label", "\"DEMO (가상 장비)\"");
            Set(_first, "serial-number", "\"DEMO-0001\"");
            Set(_second, "laser1:dl:cc:current-set", "82.0");
            foreach (var s in new[] { _first, _second })
                new Thread(s.Physics) { IsBackground = true, Name = "DLC demo physics" }.Start();
            _cmd.Start();
            _mon.Start();
            _ = Accept(_cmd, ServeCommand);
            _ = Accept(_mon, ServeMonitor);
        }

        private static void Configure(DlcSim.Sim sim, string label, string product, Func<double, double> spectrum, double offset, double amplitude)
        {
            Set(sim, "laser1:label", label);
            Set(sim, "laser1:product-name", product);
            Set(sim, "laser1:scan:offset", offset.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            Set(sim, "laser1:scan:amplitude", amplitude.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            Set(sim, "laser1:scan:start", (offset - amplitude / 2).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            Set(sim, "laser1:scan:end", (offset + amplitude / 2).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            sim.SpectrumOverride = spectrum;
        }

        private static void Set(DlcSim.Sim sim, string name, string raw) => sim.Force(name, raw);

        // ------------------------------------------------------------------
        // 데모 스펙트럼 (x = 피에조 전압 V)
        // ------------------------------------------------------------------

        private static double Lor(double x, double c, double g, double absorptive, double dispersive)
        {
            double u = (x - c) / g, d = 1 / (1 + u * u);
            return absorptive * d + dispersive * u * d;
        }

        /// <summary>Cs D2 F=4 편광 분광: F'=5가 가장 크고, 교차 공명 CO 4-5·CO 3-5가 뚜렷한 분산형 신호.</summary>
        private static double PolarizationSpectrum(double x)
        {
            double f5 = 70.45, k = DemoMHzPerV, g = 9.0 / k;
            double[] amp = { 0.04, 0.07, 0.05, 0.12, 0.16, 0.30 };
            double s = 0.01 * Math.Sin((x - 69) * 1.3);   // 약한 배경
            var lines = CalibrationPresets.CsD2.Lines;
            for (int i = 0; i < lines.Count; i++)
                s += Lor(x, f5 + lines[i].OffsetMHz / k, g * (i == 5 ? 1.0 : 1.2), -0.15 * amp[i], amp[i]);
            return s;
        }

        /// <summary>Cs 6P3/2 → 7S1/2 (EIT/OODR 유사): F''=4, F''=3 두 선 + 다른 중간 준위에서 온 작은 선.</summary>
        private static double LadderSpectrum(double x)
        {
            double f4 = 73.2, k = DemoMHzPerV, g = 12.0 / k;
            double s = 0.02 * (x - 70) / 5;
            s += Lor(x, f4, g, -0.05, 0.25);
            s += Lor(x, f4 + CalibrationPresets.Cs7S.Lines[0].OffsetMHz / k, g, -0.04, 0.16);
            s += Lor(x, f4 - 145.0 / k, g, -0.01, 0.04);
            return s;
        }

        // ------------------------------------------------------------------
        // 통신 (DualLaserRig와 같은 구조)
        // ------------------------------------------------------------------

        private string Evaluate(string request)
        {
            Match target = Laser2.Match(request);
            if (!target.Success) return _first.Eval(request);
            Group g = target.Groups[1];
            string routed = request.Substring(0, g.Index) + "laser1:" + g.Value.Substring(7) + request.Substring(g.Index + g.Length);
            return _second.Eval(routed);
        }

        private async Task Accept(TcpListener listener, Func<TcpClient, Task> serve)
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    TcpClient c = await listener.AcceptTcpClientAsync(_stop.Token);
                    c.NoDelay = true;
                    _clients.Add(c);
                    _ = Task.Run(async () => { using (c) { try { await serve(c); } catch { } } });
                }
            }
            catch (OperationCanceledException) { }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        }

        private async Task ServeCommand(TcpClient client)
        {
            using var reader = new StreamReader(client.GetStream(), Encoding.UTF8);
            using var writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            await writer.WriteAsync("DeCoF Command Line (DLC pro DEMO)\n> ");
            while (await reader.ReadLineAsync(_stop.Token) is string line)
            {
                line = line.Trim();
                if (line.Length == 0) { await writer.WriteAsync("> "); continue; }
                if (line == "(quit)") break;
                string resp;
                try { resp = Evaluate(line); }
                catch (Exception ex) { resp = "Error: -1 " + ex.Message; }
                await writer.WriteAsync(resp + "\n> ");
            }
        }

        private async Task ServeMonitor(TcpClient client)
        {
            var watches = new ConcurrentDictionary<string, string>();
            using var reader = new StreamReader(client.GetStream(), Encoding.UTF8);
            using var writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            using var done = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            Task send = Task.Run(async () =>
            {
                try
                {
                    while (!done.IsCancellationRequested)
                    {
                        foreach (string name in watches.Keys)
                        {
                            string raw = Evaluate("(param-ref '" + name + ")");
                            if (raw.StartsWith("Error"))
                            {
                                watches.TryRemove(name, out _);
                                await writer.WriteLineAsync("(Error: -3 (add '" + name + ") no such parameter)");
                            }
                            else if (watches.TryGetValue(name, out string old) && old != raw)
                            {
                                watches[name] = raw;
                                await writer.WriteLineAsync("(" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") + " '" + name + " " + raw + ")");
                            }
                        }
                        await Task.Delay(50, done.Token);
                    }
                }
                catch { }
            });
            try
            {
                while (await reader.ReadLineAsync(done.Token) is string line)
                {
                    Match m = AddRx.Match(line);
                    if (m.Success) watches.TryAdd(m.Groups[1].Value, "");
                    else if (line.StartsWith("(remove '")) watches.TryRemove(line.Substring(9).TrimEnd(')', ' '), out _);
                    else if (line.Trim() == "(remove-all)") watches.Clear();
                }
            }
            catch { }
            finally
            {
                done.Cancel();
                try { await send; } catch { }
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _first.Stopped = _second.Stopped = true;
            try { _cmd.Stop(); } catch { }
            try { _mon.Stop(); } catch { }
            foreach (TcpClient c in _clients) { try { c.Dispose(); } catch { } }
        }
    }
}
