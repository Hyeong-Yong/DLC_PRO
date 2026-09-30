#nullable disable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DLC_PRO.Core
{
    public enum ConnectionKind { Tcp, Serial }

    /// <summary>스코프 한 프레임 (laser1:scope:data + laser1:dl:lock:candidates).</summary>
    public sealed class ScopeFrame
    {
        public float[] X;
        public float[] Y1;
        public float[] Y2;
        public LockCandidate[] Candidates;
        public LockCandidate? LockPoint;
        public LockCandidate? Tracking;
        public int? LockStateFromBlob;
        public float[] BackgroundX;
        public float[] BackgroundY;
        public DateTime Time;
        public int LaserId = 1;
    }

    /// <summary>
    /// DLC pro 장비 전체를 관리하는 클래스.
    /// - 명령 라인 연결(쓰기/명령용) + 보조 명령 라인(스코프 데이터 폴링용) + 모니터링 라인(값 구독)
    /// - 모든 장비 I/O는 백그라운드 스레드에서 수행 → UI는 절대 블로킹되지 않음
    /// - 구독한 파라미터 값은 캐시에 저장되고, UI는 캐시를 주기적으로 읽어 표시
    /// - 안전 명령(증폭기 OFF 등)은 우선순위 큐로 일반 작업보다 먼저 실행
    /// </summary>
    public sealed class DlcDevice : IDisposable
    {
        private sealed class WatchSpec { public string Name; public int PeriodMs; public double Threshold; public DateTime LastPoll; }

        private sealed class WorkItem
        {
            public Action<DecofClient> Run;
            public Action<Exception> Fail;
        }

        /// <summary>한 번의 연결(세션)에 속한 자원. 연결마다 새로 만들어 이전 세션 스레드와 섞이지 않게 한다.</summary>
        private sealed class Session
        {
            public volatile bool Running = true;
            public ConnectionKind Kind;
            public string Host;
            public int CmdPort, MonPort;
            public string SerialPort;
            public volatile DecofClient Cmd;
            public volatile DecofClient Aux;
            public volatile MonitorLine Mon;
            public readonly BlockingCollection<WorkItem> Priority = new BlockingCollection<WorkItem>();
            public readonly BlockingCollection<WorkItem> Normal = new BlockingCollection<WorkItem>();
            public BlockingCollection<WorkItem>[] Queues;
            public Thread Worker, Poller, Scope;
            public DateTime LastAuxAttempt, LastReconnectRequest;
        }

        private readonly ConcurrentDictionary<string, string> _cache = new ConcurrentDictionary<string, string>();
        private readonly ConcurrentDictionary<string, string> _unavailable = new ConcurrentDictionary<string, string>();
        /// <summary>모니터링 라인이 구독을 거부한 파라미터 → 폴링으로 대신 읽음.</summary>
        private readonly ConcurrentDictionary<string, byte> _monRefused = new ConcurrentDictionary<string, byte>();
        private readonly Dictionary<string, WatchSpec> _watches = new Dictionary<string, WatchSpec>();
        private readonly object _watchLock = new object();
        private readonly object _connLock = new object();
        private volatile Session _s;
        private readonly DlcDevice _parent;
        public int LaserId { get; } = 1;
        private readonly ConcurrentDictionary<int, DlcDevice> _views = new();
        public DlcDevice() { }
        private DlcDevice(DlcDevice parent, int id) { _parent = parent; LaserId = id; }
        public DlcDevice ForLaser(int id) => _parent != null ? _parent.ForLaser(id) : id == 1 ? this : _views.GetOrAdd(id, i => {
            LaserAddress.Map("laser1:type", i);
            return new DlcDevice(this, i);
        });
        private string Map(string name) => LaserAddress.Map(name, LaserId);
        private long _sessionVersion;
        public long SessionVersion => _parent?.SessionVersion ?? Interlocked.Read(ref _sessionVersion);
        private readonly Stopwatch _errThrottle = Stopwatch.StartNew();

        public ConnectionKind Kind { get { if (_parent != null) return _parent.Kind; Session s = _s; return s != null ? s.Kind : ConnectionKind.Tcp; } }
        public bool IsConnected { get { if (_parent != null) return _parent.IsConnected; Session s = _s; return s != null && s.Running; } }
        public bool MonitorAvailable { get { if (_parent != null) return _parent.MonitorAvailable; Session s = _s; return s != null && s.Mon != null; } }
        private string _endpoint;
        public string Endpoint { get => _parent?.Endpoint ?? _endpoint; private set => _endpoint = value; }

        /// <summary>스코프 데이터 스트리밍 ON/OFF (Scan&amp;Lock 탭이 보일 때 ON).</summary>
        private volatile bool _scopeStreaming;
        private volatile int _scopeLaserId = 1;
        public bool ScopeStreaming {
            get => _parent != null ? _parent._scopeStreaming && _parent._scopeLaserId == LaserId : _scopeStreaming && _scopeLaserId == 1;
            set {
                var root = _parent ?? this;
                if (value) { root._scopeLaserId = LaserId; root._scopeStreaming = true; }
                else if (root._scopeLaserId == LaserId) root._scopeStreaming = false;
            }
        }
        /// <summary>스코프와 함께 락 후보 데이터도 읽을지 여부.</summary>
        public volatile bool FetchCandidates = true;
        /// <summary>최대 스코프 갱신률 (Hz, 0.5~30).</summary>
        private double _maxScopeRate = 15;
        public double MaxScopeRate { get => _parent?.MaxScopeRate ?? _maxScopeRate; set { if (_parent != null) _parent.MaxScopeRate = value; else _maxScopeRate = value; } }

        public event Action<bool> ConnectionChanged;
        /// <summary>(레벨 "INFO"/"WARN"/"ERROR", 메시지)</summary>
        public event Action<string, string> LogMessage;
        /// <summary>(방향, 텍스트) — 통신 로그</summary>
        public event Action<string, string> Traffic;
        public event Action<ScopeFrame> ScopeFrameReceived;
        /// <summary>(이름, 원시 값) — 캐시 값이 갱신될 때 (백그라운드 스레드)</summary>
        public event Action<string, string> ParamChanged;

        private bool _logTraffic;
        public bool LogTraffic { get => _parent?.LogTraffic ?? _logTraffic; set { if (_parent != null) _parent.LogTraffic = value; else _logTraffic = value; } }

        // ==================================================================
        // 연결
        // ==================================================================

        public void ConnectTcp(string host, int cmdPort = 1998, int monPort = 1999)
        {
            if (_parent != null) throw new InvalidOperationException("컨트롤러에서 연결해 주세요.");
            lock (_connLock)
            {
                DisconnectCore();
                Session s = new Session { Kind = ConnectionKind.Tcp, Host = host, CmdPort = cmdPort, MonPort = monPort };
                try
                {
                    s.Cmd = OpenTcpClient(s);
                    try { s.Aux = OpenTcpClient(s); }
                    catch (Exception ex) { Log("WARN", "보조 명령 라인 연결 실패 (나중에 재시도): " + ex.Message); s.Aux = null; }
                    s.LastAuxAttempt = DateTime.UtcNow;
                    try
                    {
                        MonitorLine m = new MonitorLine(new TcpTransport(host, monPort));
                        HookMonitor(s, m);
                        s.Mon = m;
                        m.Start();
                    }
                    catch (Exception ex) { Log("WARN", "모니터링 라인 연결 실패 → 폴링 모드: " + ex.Message); s.Mon = null; }
                }
                catch
                {
                    DisposeSessionResources(s);
                    throw;
                }
                Endpoint = host;
                StartSession(s);
                Log("INFO", "연결됨: " + host + " (명령 " + cmdPort + ", 모니터 " + (s.Mon != null ? monPort.ToString() : "없음") + ")");
            }
        }

        public void ConnectSerial(string portName)
        {
            if (_parent != null) throw new InvalidOperationException("컨트롤러에서 연결해 주세요.");
            lock (_connLock)
            {
                DisconnectCore();
                Session s = new Session { Kind = ConnectionKind.Serial, SerialPort = portName };
                s.Cmd = OpenSerialClient(s);
                s.Aux = null;
                s.Mon = null; // USB에는 모니터링 라인이 없음 → 폴링
                Endpoint = portName;
                StartSession(s);
                Log("INFO", "연결됨: " + portName + " (USB, 폴링 모드)");
            }
        }

        private DecofClient OpenTcpClient(Session s)
        {
            DecofClient c = new DecofClient(new TcpTransport(s.Host, s.CmdPort), false);
            HookTraffic(c);
            return c;
        }

        private DecofClient OpenSerialClient(Session s)
        {
            DecofClient c = new DecofClient(new SerialTransport(s.SerialPort), true);
            HookTraffic(c);
            return c;
        }

        private void HookTraffic(DecofClient c)
        {
            c.Traffic += (d, t) =>
            {
                if (d == "WARN") { Log("WARN", t); return; }
                if (LogTraffic) RaiseTraffic(d, t);
            };
        }

        private void HookMonitor(Session s, MonitorLine m)
        {
            m.Traffic += (d, t) => { if (LogTraffic) RaiseTraffic(d, t); };
            m.ValueReceived += (name, raw, ts) => { if (_s == s && s.Running) UpdateCache(name, raw); };
            m.ErrorReceived += (name, msg) =>
            {
                if (_s != s) return;
                if (string.IsNullOrEmpty(name)) { Log("WARN", "모니터링 라인 오류: " + msg); return; }
                _monRefused[name] = 1;
                Log("WARN", "구독 불가 → 폴링으로 읽기 시도: " + name + " (" + msg + ")");
            };
            m.Closed += ex =>
            {
                if (!s.Running || s.Mon != m) return;
                Log("WARN", "모니터링 라인 끊김 → 폴링 모드로 전환: " + ex.Message);
                s.Mon = null;
                try { m.Dispose(); } catch { }
            };
        }

        private void StartSession(Session s)
        {
            Interlocked.Increment(ref _sessionVersion);
            s.Queues = new[] { s.Priority, s.Normal };
            _monRefused.Clear();
            _s = s;
            s.Worker = new Thread(() => WorkerLoop(s)) { IsBackground = true, Name = "DLCpro-Worker" };
            s.Poller = new Thread(() => PollLoop(s)) { IsBackground = true, Name = "DLCpro-Poller" };
            s.Scope = new Thread(() => ScopeLoop(s)) { IsBackground = true, Name = "DLCpro-Scope" };
            s.Worker.Start(); s.Poller.Start(); s.Scope.Start();

            List<WatchSpec> list;
            lock (_watchLock) list = new List<WatchSpec>(_watches.Values);
            foreach (WatchSpec w in list) SubscribeOnDevice(s, w);
            RaiseConnection(true);
        }

        public void Disconnect()
        {
            if (_parent != null) return;
            lock (_connLock) DisconnectCore();
        }

        private void DisconnectCore()
        {
            Session s = _s;
            if (s == null) return;
            _s = null;
            Interlocked.Increment(ref _sessionVersion);
            s.Running = false;
            s.Priority.CompleteAdding();
            s.Normal.CompleteAdding();
            // 전송 계층을 먼저 닫아 블로킹된 읽기를 즉시 풀어준다
            DisposeSessionResources(s);
            WorkItem wi;
            while (s.Priority.TryTake(out wi)) wi.Fail(new IOException("연결 종료됨"));
            while (s.Normal.TryTake(out wi)) wi.Fail(new IOException("연결 종료됨"));
            JoinQuietly(s.Worker); JoinQuietly(s.Poller); JoinQuietly(s.Scope);
            _cache.Clear();
            _unavailable.Clear();
            _monRefused.Clear();
            Log("INFO", "연결 해제됨");
            RaiseConnection(false);
        }

        private static void DisposeSessionResources(Session s)
        {
            MonitorLine m = s.Mon; s.Mon = null;
            DecofClient a = s.Aux; s.Aux = null;
            DecofClient c = s.Cmd; s.Cmd = null;
            if (m != null) { try { m.Dispose(); } catch { } }
            if (a != null) { try { a.Dispose(); } catch { } }
            if (c != null) { try { c.Dispose(); } catch { } }
        }

        private static void JoinQuietly(Thread t)
        {
            if (t == null || t == Thread.CurrentThread) return;
            try { t.Join(3000); } catch { }
        }

        public void Dispose() { Disconnect(); }

        // ==================================================================
        // 구독 / 캐시
        // ==================================================================

        /// <summary>파라미터를 구독 목록에 등록 (모니터링 라인 또는 폴링으로 캐시 갱신).</summary>
        public void Watch(string name, int periodMs = 250, double threshold = 0)
        {
            if (_parent != null) { _parent.Watch(Map(name), periodMs, threshold); return; }
            WatchSpec w;
            lock (_watchLock)
            {
                if (_watches.TryGetValue(name, out w))
                {
                    if (periodMs >= w.PeriodMs) return;
                    w.PeriodMs = periodMs;
                }
                else
                {
                    w = new WatchSpec { Name = name, PeriodMs = periodMs, Threshold = threshold };
                    _watches[name] = w;
                }
            }
            Session s = _s;
            if (s != null && s.Running) SubscribeOnDevice(s, w);
        }

        public void WatchMany(int periodMs, params string[] names)
        {
            foreach (string n in names) Watch(n, periodMs);
        }

        private void SubscribeOnDevice(Session s, WatchSpec w)
        {
            MonitorLine m = s.Mon;
            if (m == null) return; // 폴링 스레드가 처리
            try { m.Add(w.Name, Math.Max(20, w.PeriodMs), w.Threshold); }
            catch (Exception ex) { Log("WARN", "구독 실패 " + w.Name + ": " + ex.Message); _monRefused[w.Name] = 1; }
        }

        private void UpdateCache(string name, string raw)
        {
            if (_parent != null) { _parent.UpdateCache(Map(name), raw); return; }
            string old;
            bool changed = !_cache.TryGetValue(name, out old) || old != raw;
            _cache[name] = raw;
            string dummy;
            _unavailable.TryRemove(name, out dummy);
            if (changed)
            {
                Action<string, string> h = ParamChanged;
                if (h != null) { try { h(name, raw); } catch { } }
            }
        }

        public bool TryGetRaw(string name, out string raw) { return _parent != null ? _parent.TryGetRaw(Map(name), out raw) : _cache.TryGetValue(name, out raw); }

        public bool TryGetDouble(string name, out double v)
        {
            string raw; v = double.NaN;
            return TryGetRaw(name, out raw) && DecofValue.TryDouble(raw, out v);
        }

        public bool TryGetBool(string name, out bool v)
        {
            string raw; v = false;
            return TryGetRaw(name, out raw) && DecofValue.TryBool(raw, out v);
        }

        public bool TryGetInt(string name, out int v)
        {
            string raw; v = 0;
            return TryGetRaw(name, out raw) && DecofValue.TryInt(raw, out v);
        }

        public double GetDouble(string name, double fallback)
        {
            double v; return TryGetDouble(name, out v) ? v : fallback;
        }

        public bool GetBool(string name, bool fallback)
        {
            bool v; return TryGetBool(name, out v) ? v : fallback;
        }

        public int GetInt(string name, int fallback)
        {
            int v; return TryGetInt(name, out v) ? v : fallback;
        }

        public string GetString(string name)
        {
            string raw; return TryGetRaw(name, out raw) ? DecofValue.Str(raw) : null;
        }

        public bool IsUnavailable(string name) { return _parent != null ? _parent.IsUnavailable(Map(name)) : _unavailable.ContainsKey(name); }

        // ==================================================================
        // 비동기 명령 (작업 큐 → 워커 스레드)
        // ==================================================================

        /// <summary>일반 작업. 결과/예외는 원래 형식 그대로 전달된다 (AggregateException 아님).</summary>
        public Task<T> RunAsync<T>(Func<DecofClient, T> func) { return Enqueue(func, false); }

        public Task<T> RunForSessionAsync<T>(long version, Func<DecofClient, T> func)
        {
            return RunAsync(c =>
            {
                if (SessionVersion != version) throw new OperationCanceledException("연결이 변경되어 작업을 취소했습니다.");
                return func(c);
            });
        }

        /// <summary>우선 작업 (안전 명령용): 대기 중인 일반 작업보다 먼저 실행.</summary>
        public Task<T> RunPriorityAsync<T>(Func<DecofClient, T> func) { return Enqueue(func, true); }

        private Task<T> Enqueue<T>(Func<DecofClient, T> func, bool priority)
        {
            if (_parent != null) return _parent.Enqueue(c => func(c.ForLaser(LaserId)), priority);
            TaskCompletionSource<T> tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Session s = _s;
            if (s == null || !s.Running)
            {
                tcs.SetException(new InvalidOperationException("장비에 연결되어 있지 않습니다."));
                return tcs.Task;
            }
            WorkItem wi = new WorkItem
            {
                Run = c => tcs.TrySetResult(func(c)),
                Fail = ex => tcs.TrySetException(ex),
            };
            try { (priority ? s.Priority : s.Normal).Add(wi); }
            catch (InvalidOperationException) { tcs.TrySetException(new InvalidOperationException("연결 종료 중")); }
            return tcs.Task;
        }

        /// <summary>파라미터 설정 후 즉시 다시 읽어 캐시를 갱신 (clip 경고 시 실제 값 반영).</summary>
        public Task<int> SetAsync(string name, object value) { return RunAsync(c => SetAndReadBack(c, name, value)); }

        /// <summary>안전 명령용 우선 설정.</summary>
        public Task<int> SetPriorityAsync(string name, object value) { return RunPriorityAsync(c => SetAndReadBack(c, name, value)); }

        internal int SetAndReadBack(DecofClient c, string name, object value)
        {
            int code = c.ParamSet(name, value);
            if (code == 2) Log("WARN", name + ": 값이 허용 범위로 제한됨(clip)");
            try { UpdateCache(name, c.ParamRef(name)); }
            catch { (_parent ?? this)._cache.TryRemove(Map(name), out _); throw; }
            return code;
        }

        public Task<string> ExecAsync(string name, params object[] args)
        {
            return RunAsync(c => c.Exec(name, args, 15000));
        }

        public Task<string> RefAsync(string name)
        {
            return RunAsync(c =>
            {
                string raw = c.ParamRef(name);
                UpdateCache(name, raw);
                return raw;
            });
        }

        /// <summary>임의 명령 전송 (콘솔용). 응답 원문 반환. 여러 줄이면 공백으로 합친다.</summary>
        public Task<string> SendRawAsync(string command)
        {
            string one = (command ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return RunAsync(c => c.Send(one, 15000));
        }

        private void WorkerLoop(Session s)
        {
            while (s.Running)
            {
                WorkItem wi;
                try { BlockingCollection<WorkItem>.TakeFromAny(s.Queues, out wi); }
                catch (ArgumentException) { break; }          // 모든 큐가 닫힘
                catch (InvalidOperationException) { break; }
                try
                {
                    DecofClient c = EnsureCmd(s);
                    wi.Run(c);
                }
                catch (Exception ex)
                {
                    wi.Fail(ex);
                    if (!(ex is DecofException) && !(ex is ArgumentException) && s.Running) Log("ERROR", "통신 오류: " + ex.Message);
                }
            }
        }

        /// <summary>주 명령 라인이 깨졌으면 재접속 (TCP/USB). 워커 스레드에서만 호출.</summary>
        private DecofClient EnsureCmd(Session s)
        {
            DecofClient c = s.Cmd;
            if (c != null && !c.IsBroken) return c;
            if (!s.Running) throw new IOException("연결 종료됨");
            Log("WARN", "명령 라인 재접속 시도...");
            if (c != null) { s.Cmd = null; try { c.Dispose(); } catch { } }
            DecofClient n = s.Kind == ConnectionKind.Tcp ? OpenTcpClient(s) : OpenSerialClient(s);
            if (!s.Running) { try { n.Dispose(); } catch { } throw new IOException("연결 종료됨"); }
            s.Cmd = n;
            Log("INFO", "명령 라인 재접속 성공");
            return n;
        }

        // ==================================================================
        // 폴링 (모니터링 라인이 없거나, 모니터링 라인이 거부한 파라미터)
        // ==================================================================

        private void PollLoop(Session s)
        {
            while (s.Running)
            {
                try
                {
                    bool monitor = s.Mon != null;
                    List<WatchSpec> due = new List<WatchSpec>();
                    DateTime now = DateTime.UtcNow;
                    lock (_watchLock)
                    {
                        foreach (WatchSpec w in _watches.Values)
                        {
                            if (monitor && !_monRefused.ContainsKey(w.Name)) continue; // 모니터링 라인이 처리
                            if (_unavailable.ContainsKey(w.Name) && (now - w.LastPoll).TotalSeconds < 10) continue;
                            if ((now - w.LastPoll).TotalMilliseconds >= Math.Max(200, w.PeriodMs)) due.Add(w);
                        }
                    }
                    foreach (WatchSpec w in due)
                    {
                        if (!s.Running) break;
                        DecofClient c = s.Cmd;
                        if (c == null || c.IsBroken) { RequestReconnect(s); break; }
                        w.LastPoll = DateTime.UtcNow;
                        try
                        {
                            // 폴링도 워커에 한 건씩 양보하여 OFF 우선순위 큐를 우회하지 않는다.
                            RunAsync(client =>
                            {
                                if (!s.Running || _s != s) throw new OperationCanceledException();
                                string raw = client.ParamRef(w.Name);
                                if (s.Running && _s == s) UpdateCache(w.Name, raw);
                                return 0;
                            }).GetAwaiter().GetResult();
                        }
                        catch (DecofException ex) { if (_s == s) { _cache.TryRemove(w.Name, out _); _unavailable[w.Name] = ex.Message; } }
                    }
                }
                catch (Exception ex) { if (s.Running) ThrottledLog("폴링 오류: " + ex.Message); }
                Thread.Sleep(50);
            }
        }

        /// <summary>명령 라인이 깨졌을 때 워커에게 재접속을 맡긴다 (2초에 한 번).</summary>
        private void RequestReconnect(Session s)
        {
            if ((DateTime.UtcNow - s.LastReconnectRequest).TotalSeconds < 2) return;
            s.LastReconnectRequest = DateTime.UtcNow;
            RunAsync(c => 0).ContinueWith(t => { var ignore = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        }

        // ==================================================================
        // 스코프 데이터 스트리밍
        // ==================================================================

        /// <summary>스코프용 연결 선택: TCP는 보조 라인(끊기면 2초마다 재접속 시도), 없으면 주 라인.</summary>
        private DecofClient ScopeClient(Session s, out bool usingMain)
        {
            usingMain = false;
            if (s.Kind == ConnectionKind.Tcp)
            {
                DecofClient a = s.Aux;
                if (a != null && !a.IsBroken) return a;
                if ((DateTime.UtcNow - s.LastAuxAttempt).TotalSeconds >= 2)
                {
                    s.LastAuxAttempt = DateTime.UtcNow;
                    if (a != null) { s.Aux = null; try { a.Dispose(); } catch { } }
                    try
                    {
                        DecofClient n = OpenTcpClient(s);
                        if (!s.Running) { n.Dispose(); return null; }
                        s.Aux = n;
                        return n;
                    }
                    catch (Exception ex) { ThrottledLog("보조 명령 라인 재접속 실패: " + ex.Message); }
                }
            }
            usingMain = true;
            DecofClient c = s.Cmd;
            return c != null && !c.IsBroken ? c : null;
        }

        private void ScopeLoop(Session s)
        {
            bool bgFetched = false;
            int bgLaserId = 0;
            float[] bgX = null, bgY = null;
            while (s.Running)
            {
                if (!_scopeStreaming) { Thread.Sleep(100); continue; }
                int laserId = _scopeLaserId;
                var view = ForLaser(laserId);
                if (bgLaserId != laserId) { bgFetched = false; bgX = bgY = null; bgLaserId = laserId; }
                Stopwatch sw = Stopwatch.StartNew();
                double rate = view.GetDouble(P.ScopeUpdateRate, 10);
                double max = MaxScopeRate;
                if (double.IsNaN(max) || max < 0.5) max = 0.5;
                if (max > 30) max = 30;
                if (double.IsNaN(rate) || rate <= 0) rate = 10;
                rate = Math.Min(rate, max);
                bool usingMain = false;
                try
                {
                    DecofClient c = ScopeClient(s, out usingMain);
                    if (c == null) { Thread.Sleep(200); continue; }
                    // 주 명령 라인을 같이 쓸 때는 다른 명령이 밀리지 않도록 갱신률을 제한
                    if (usingMain) rate = Math.Min(rate, s.Kind == ConnectionKind.Serial ? 3 : 5);

                    c = c.ForLaser(laserId);
                    ScopeFrame f = new ScopeFrame { Time = DateTime.Now, LaserId = laserId };
                    BinaryBlob blob = BinaryBlob.Parse(c.GetBinary(P.ScopeData));
                    f.X = blob.Floats('x'); f.Y1 = blob.Floats('y'); f.Y2 = blob.Floats('Y');

                    if (view.FetchCandidates && !view.IsUnavailable(P.LockCandidates))
                    {
                        try
                        {
                            BinaryBlob cb = BinaryBlob.Parse(c.GetBinary(P.LockCandidates));
                            f.Candidates = cb.Candidates('c');
                            LockCandidate[] l = cb.Candidates('l');
                            if (l != null && l.Length > 0 && l[0].IsValid) f.LockPoint = l[0];
                            LockCandidate[] t = cb.Candidates('t');
                            if (t != null && t.Length > 0) f.Tracking = t[0];
                            f.LockStateFromBlob = cb.StateByte('s');
                        }
                        catch (DecofException ex) { _unavailable[LaserAddress.Map(P.LockCandidates, laserId)] = ex.Message; }
                    }

                    // 락이 걸린 동안에는 락 직전 트레이스(background-trace)를 한 번 읽어 함께 표시
                    int st = f.LockStateFromBlob ?? view.GetInt(P.LockState, 0);
                    if (st >= (int)LockStateCode.Locking)
                    {
                        if (!bgFetched && !view.IsUnavailable(P.LockBackgroundTrace))
                        {
                            try
                            {
                                BinaryBlob bb = BinaryBlob.Parse(c.GetBinary(P.LockBackgroundTrace));
                                bgX = bb.Floats('x'); bgY = bb.Floats('y');
                                bgFetched = bgX != null && bgX.Length > 0;
                            }
                            catch (DecofException) { }
                        }
                    }
                    else { bgFetched = false; bgX = bgY = null; }
                    f.BackgroundX = bgX; f.BackgroundY = bgY;

                    Action<ScopeFrame> h = view.ScopeFrameReceived;
                    if (h != null && s.Running && laserId == _scopeLaserId) { try { h(f); } catch { } }
                }
                catch (Exception ex)
                {
                    if (s.Running) ThrottledLog("스코프 데이터 오류: " + ex.Message);
                    Thread.Sleep(500);
                }
                int wait = (int)Math.Max(0, Math.Min(2000, 1000.0 / rate - sw.ElapsedMilliseconds));
                if (wait > 0) Thread.Sleep(wait);
            }
        }

        // ==================================================================
        // 로그
        // ==================================================================

        public void Log(string level, string msg)
        {
            if (_parent != null) { _parent.Log(level, "[Laser " + LaserId + "] " + msg); return; }
            Action<string, string> h = LogMessage;
            if (h != null) { try { h(level, msg); } catch { } }
        }

        private void ThrottledLog(string msg)
        {
            lock (_errThrottle)
            {
                if (_errThrottle.ElapsedMilliseconds < 3000) return;
                _errThrottle.Restart();
            }
            Log("ERROR", msg);
        }

        private void RaiseTraffic(string d, string t)
        {
            foreach (var view in _views.Values) view.RaiseTraffic(d, t);
            Action<string, string> h = Traffic;
            if (h != null) { try { h(d, t); } catch { } }
        }

        private void RaiseConnection(bool c)
        {
            foreach (var view in _views.Values) view.RaiseConnection(c);
            Action<bool> h = ConnectionChanged;
            if (h != null) { try { h(c); } catch { } }
        }
    }
}
