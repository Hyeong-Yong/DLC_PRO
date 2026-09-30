using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DLC_PRO.Core.Wlm;
using DLC_PRO.Models;

namespace DLC_PRO.Services {
    /// <summary>
    /// HighFinesse 파장계(WS/6) 접근의 단일 창구 (DI 싱글톤, DLC pro 연결과 독립).
    /// - wlmData.dll 호출은 전용 작업 스레드 하나에서만 한다 (UI 멈춤 방지, 호출 직렬화).
    /// - 측정값은 DLL 콜백(CallbackProcEx, cmiWavelength1)으로 받고, 콜백을 설치할 수 없으면 폴링으로 전환한다.
    /// - 측정값/패턴/상태는 UI 스레드에서 이벤트·속성으로 전달된다.
    /// - 앱을 종료하거나 연결을 끊어도 WLM 측정은 그대로 둔다 (WLM 프로그램은 사용자가 따로 사용 중).
    /// </summary>
    public sealed partial class WavemeterService : ObservableObject, IDisposable {
        private const int Channel = 1;
        private readonly LogService _log;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly BlockingCollection<Action> _jobs = new BlockingCollection<Action>();
        private readonly ConcurrentQueue<WlmSample> _samples = new ConcurrentQueue<WlmSample>();
        private readonly DispatcherTimer _uiTimer;
        private Thread? _worker;
        private IWlmBackend? _backend;          // 작업 스레드 전용
        private volatile bool _statusDirty, _patternDirty, _disposed, _serverLost;
        private bool _polling, _patternOn, _wantPattern, _wasRunning;
        private long _nextStatusMs, _nextPatternMs, _nextErrorMs;
        private double _lastPolledError;
        private double _lastVac = double.NaN;   // 작업 스레드 (공기 변환 비율 계산용)
        private double[]? _patternBuf;

        public WavemeterService(LogService log) {
            _log = log;
            Settings = WavemeterSettings.Load();
            _uiTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background, (_, _) => DrainSamples());
            _uiTimer.Start();
        }

        public WavemeterSettings Settings { get; }

        /// <summary>true면 실제 DLL 대신 시뮬레이터 사용 (명령줄 --wlm-sim).</summary>
        public bool UseSimulator { get; set; }

        /// <summary>테스트용 백엔드 생성기 (null이면 UseSimulator에 따라 선택).</summary>
        public Func<IWlmBackend>? BackendFactory { get; set; }

        [ObservableProperty] private bool _isConnected;
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string _backendName = "";
        [ObservableProperty] private bool _callbackMode;
        [ObservableProperty] private WlmStatus _status = new WlmStatus();
        /// <summary>마지막 측정값 (진공 nm, 오류 값 포함).</summary>
        [ObservableProperty] private double _lastVacuumNm = double.NaN;
        [ObservableProperty] private DateTime _lastSampleTime;
        [ObservableProperty] private string _message = "파장계에 연결되어 있지 않습니다.";
        /// <summary>마지막 설정 쓰기 오류 (성공하면 지워짐).</summary>
        [ObservableProperty] private string _lastError = "";

        public bool IsMeasuring => IsConnected && Status.ServerRunning && Status.OperationState == WlmConst.cMeasurement;
        partial void OnStatusChanged(WlmStatus value) => OnPropertyChanged(nameof(IsMeasuring));
        partial void OnIsConnectedChanged(bool value) => OnPropertyChanged(nameof(IsMeasuring));

        /// <summary>새 측정값 묶음 (UI 스레드, 최대 50 ms마다).</summary>
        public event Action<IReadOnlyList<WlmSample>>? SamplesReceived;

        /// <summary>간섭 패턴 (UI 스레드). 배열은 호출 동안만 유효.</summary>
        public event Action<double[], int>? PatternReceived;

        /// <summary>측정 시작 성공 (UI 스레드).</summary>
        public event Action? MeasurementStarted;

        // ------------------------------------------------------------------
        // 작업 스레드
        // ------------------------------------------------------------------

        private void EnsureWorker() {
            if (_worker != null) return;
            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "WLM worker" };
            _worker.Start();
        }

        private Task<T> Run<T>(Func<IWlmBackend, T> f) {
            EnsureWorker();
            TaskCompletionSource<T> tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            try {
                _jobs.Add(() => {
                    try {
                        IWlmBackend b = _backend ?? throw new InvalidOperationException("파장계에 연결되어 있지 않습니다.");
                        tcs.SetResult(f(b));
                    }
                    catch (Exception ex) { tcs.SetException(ex); }
                });
            }
            catch (InvalidOperationException) { tcs.TrySetException(new ObjectDisposedException(nameof(WavemeterService))); }
            return tcs.Task;
        }

        private Task RunRaw(Action a) {
            EnsureWorker();
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            try {
                _jobs.Add(() => {
                    try { a(); tcs.SetResult(true); }
                    catch (Exception ex) { tcs.SetException(ex); }
                });
            }
            catch (InvalidOperationException) { tcs.TrySetException(new ObjectDisposedException(nameof(WavemeterService))); }
            return tcs.Task;
        }

        private void WorkerLoop() {
            while (!_disposed) {
                try {
                    if (_jobs.TryTake(out Action? job, _polling ? 5 : 20)) job();
                    if (_backend != null) Periodic(_backend);
                }
                catch (InvalidOperationException) { break; }   // CompleteAdding
                catch (Exception ex) {
                    _log.Error("[WLM] 작업 스레드 오류: " + ex.Message);
                    Thread.Sleep(200);
                }
            }
        }

        private void Periodic(IWlmBackend b) {
            long now = _clock.ElapsedMilliseconds;
            if (_polling) {
                double v = b.GetWavelengthNum(Channel);
                if (WlmUnits.IsValid(v)) Enqueue(0, v);
                else if (v != WlmConst.ErrNoValue && v != WlmConst.ErrWlmMissing && (v != _lastPolledError || now >= _nextErrorMs)) {
                    // 오류 값은 반복 반환될 수 있으므로 같은 오류는 0.25 s에 한 번만 기록
                    _lastPolledError = v;
                    _nextErrorMs = now + 250;
                    Enqueue(0, v);
                }
            }
            if (_statusDirty || now >= _nextStatusMs) {
                _statusDirty = false;
                _nextStatusMs = now + 500;
                ReadStatus(b);
            }
            if (_patternOn && (_patternDirty || _polling) && now >= _nextPatternMs) {
                _patternDirty = false;
                _nextPatternMs = now + 150;
                int n = b.GetPatternItemCount(WlmConst.cSignal1Interferometers);
                if (n > 0 && n < 1_000_000) {
                    if (_patternBuf == null || _patternBuf.Length != n) _patternBuf = new double[n];
                    double[] buf = _patternBuf;
                    int got = b.GetPatternData(Channel, WlmConst.cSignal1Interferometers, buf);
                    if (got > 0) {
                        double[] copy = new double[got];
                        Array.Copy(buf, copy, got);
                        Dispatcher.UIThread.Post(() => PatternReceived?.Invoke(copy, got));
                    }
                }
            }
        }

        private void Enqueue(int wlmMs, double vac) {
            _samples.Enqueue(new WlmSample(DateTime.Now, _clock.Elapsed.TotalSeconds, wlmMs, vac));
            if (WlmUnits.IsValid(vac)) _lastVac = vac;
        }

        /// <summary>DLL 콜백 (DLL 스레드): 값만 모아 두고 바로 반환한다 (매뉴얼 CallbackProc 권장 사항).</summary>
        private void OnWlmCallback(int ver, int mode, int intVal, double dblVal, int res1) {
            switch (mode) {
                case WlmConst.cmiWavelength1:
                    Enqueue(intVal, dblVal);
                    break;
                case WlmConst.cmiPatternAnalysisWritten:
                    _patternDirty = true;
                    break;
                case WlmConst.cmiDLLDetach:
                    _serverLost = true;
                    _statusDirty = true;
                    break;
                case WlmConst.cmiTemperature:
                case WlmConst.cmiPressure:
                    break;   // 상태 주기 읽기에 포함
                default:
                    _statusDirty = true;   // 설정/동작 상태 변경 → 즉시 다시 읽기
                    break;
            }
        }

        private void ReadStatus(IWlmBackend b) {
            bool running = b.IsServerRunning();
            if (!running) {
                if (_wasRunning) _log.Warn("[WLM] 파장계 서버 프로그램이 종료되었습니다.");
                _wasRunning = false;
                PostStatus(new WlmStatus { ServerRunning = false }, "WLM 서버 프로그램이 실행 중이 아닙니다. 프로그램을 다시 실행하면 자동으로 이어서 연결합니다.");
                return;
            }
            if (!_wasRunning || _serverLost) {
                // 서버가 (다시) 켜짐 → 범위 모델과 콜백을 다시 설정
                _serverLost = false;
                PrepareSession(b);
                _log.Info("[WLM] 파장계 서버에 다시 연결했습니다.");
            }
            _wasRunning = true;

            int acPeriod = 0, acUnit = 0;
            b.GetAutoCalSetting(WlmConst.cmiAutoCalPeriod, out acPeriod);
            b.GetAutoCalSetting(WlmConst.cmiAutoCalUnit, out acUnit);
            double air = 0;
            double vac = _lastVac;
            if (WlmUnits.IsValid(vac)) {
                double a = b.ConvertUnit(vac, WlmConst.cReturnWavelengthVac, WlmConst.cReturnWavelengthAir);
                if (WlmUnits.IsValid(a)) air = vac / a;
            }
            int eMin = b.GetExposureRange(WlmConst.cExpoMin), eMax = b.GetExposureRange(WlmConst.cExpoMax);
            WlmStatus s = new WlmStatus {
                ServerRunning = true,
                WlmType = b.GetWLMVersion(0),
                WlmVersion = b.GetWLMVersion(1),
                OperationState = b.GetOperationState(),
                ResultMode = b.GetResultMode(),
                Range = b.GetRange(),
                PulseMode = b.GetPulseMode(),
                WideMode = b.GetWideMode(),
                FastMode = b.GetFastMode(),
                Exposure = b.GetExposureNum(Channel, 1),
                ExposureAuto = b.GetExposureModeNum(Channel),
                ExposureMin = eMin > 0 ? eMin : 1,
                ExposureMax = eMax > 0 ? eMax : 9999,
                IntervalMode = b.GetIntervalMode(),
                Interval = b.GetInterval(),
                AutoCal = b.GetAutoCalMode() == 1,
                AutoCalPeriod = acPeriod,
                AutoCalUnit = acUnit,
                AverageCount = b.GetAveragingSettingNum(Channel, WlmConst.cmiAveragingCount),
                AverageMode = b.GetAveragingSettingNum(Channel, WlmConst.cmiAveragingMode),
                AverageType = b.GetAveragingSettingNum(Channel, WlmConst.cmiAveragingType),
                Temperature = b.GetTemperature(),
                Pressure = b.GetPressure(),
                Link = b.GetLinkState(),
                AirRatio = air,
            };
            PostStatus(s, null);
        }

        private void PostStatus(WlmStatus s, string? message) {
            Dispatcher.UIThread.Post(() => {
                Status = s;
                if (message != null) Message = message;
                else if (s.ServerRunning) Message = "WS/" + s.WlmType + "-" + s.WlmVersion + " · " + BackendName + (CallbackMode ? " (콜백)" : " (폴링)");
            });
        }

        /// <summary>서버 연결 직후/재시작 후 설정 (작업 스레드).</summary>
        private void PrepareSession(IWlmBackend b) {
            // 범위는 순서(0 = 가장 짧은 파장 범위)로 다룬다 (매뉴얼 SetRange: cRangeModelByOrder 권장)
            b.SetRange(WlmConst.cRangeModelByOrder);
            bool cb = false;
            try { cb = b.InstallCallback(OnWlmCallback); }
            catch (EntryPointNotFoundException) { cb = false; }
            _polling = !cb;
            if (_polling) b.SetReturnMode(1);   // 같은 측정값은 한 번만 반환 → 폴링으로 중복 없이 수집
            if (_wantPattern) _patternOn = b.SetPattern(WlmConst.cSignal1Interferometers, true) >= 0;
            Dispatcher.UIThread.Post(() => CallbackMode = cb);
        }

        // ------------------------------------------------------------------
        // UI 스레드 전달
        // ------------------------------------------------------------------

        private void DrainSamples() {
            if (_samples.IsEmpty) return;
            List<WlmSample> list = new List<WlmSample>();
            while (list.Count < 100000 && _samples.TryDequeue(out WlmSample s)) list.Add(s);
            if (list.Count == 0) return;
            WlmSample last = list[list.Count - 1];
            LastVacuumNm = last.VacuumNm;
            LastSampleTime = last.Time;
            try { SamplesReceived?.Invoke(list); }
            catch (Exception ex) { _log.Error("[WLM] 측정값 처리 오류: " + ex.Message); }
        }

        // ------------------------------------------------------------------
        // 연결
        // ------------------------------------------------------------------

        private IWlmBackend CreateBackend() =>
            BackendFactory?.Invoke() ?? (UseSimulator ? new WlmSimulatedBackend() : new WlmNativeBackend());

        /// <summary>연결. 실패하면 사용자에게 보여 줄 메시지, 성공하면 null.</summary>
        public async Task<string?> ConnectAsync() {
            if (IsConnected) return null;
            IsBusy = true;
            try {
                string? err = await RunConnect(false);
                if (err == null) {
                    OnConnected();
                    _log.Info("[WLM] 파장계 연결: " + BackendName + (CallbackMode ? " (콜백 수신)" : " (폴링 수신)"));
                }
                else {
                    Message = err.Split('\n')[0];
                    _log.Warn("[WLM] 연결 실패: " + err.Replace('\n', ' '));
                }
                return err;
            }
            finally { IsBusy = false; }
        }

        /// <summary>WLM 서버 프로그램을 실행(ControlWLM Show)하고 연결. 실패 메시지 또는 null.</summary>
        public async Task<string?> StartServerAndConnectAsync() {
            if (IsConnected) return null;
            IsBusy = true;
            try {
                Message = "WLM 서버 프로그램을 실행하는 중…";
                string? err = await RunConnect(true);
                if (err == null) {
                    OnConnected();
                    _log.Info("[WLM] 서버 실행 후 연결: " + BackendName);
                }
                else Message = err.Split('\n')[0];
                return err;
            }
            finally { IsBusy = false; }
        }

        /// <summary>연결 성공 직후 (UI 스레드, 작업 스레드 결과를 받은 뒤).</summary>
        private void OnConnected() {
            BackendName = _backend?.Name ?? "";
            CallbackMode = !_polling;
            IsConnected = true;
        }

        private Task<string?> RunConnect(bool startServer) {
            EnsureWorker();
            TaskCompletionSource<string?> tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _jobs.Add(() => {
                IWlmBackend? b = null;
                try {
                    b = CreateBackend();
                    bool running = b.IsServerRunning();
                    if (!running && startServer) {
                        b.StartServer();
                        for (int i = 0; i < 100 && !running; i++) {
                            Thread.Sleep(200);
                            running = b.IsServerRunning();
                        }
                    }
                    if (!running) {
                        b.Dispose();
                        tcs.SetResult("WLM 서버 프로그램이 실행 중이 아닙니다.\n" +
                            "'Wavelength Meter WS/6 VisIR' 프로그램을 먼저 실행하거나 [WLM 프로그램 실행]을 눌러 주세요.\n" +
                            "이 앱은 같은 PC에서 실행 중인 WLM 프로그램과 wlmData.dll로 통신합니다.");
                        return;
                    }
                    _backend = b;
                    _wasRunning = true;
                    _serverLost = false;
                    PrepareSession(b);
                    ReadStatus(b);
                    tcs.SetResult(null);
                }
                catch (DllNotFoundException) {
                    b?.Dispose();
                    tcs.SetResult("wlmData.dll을 찾을 수 없습니다.\nHighFinesse WLM 소프트웨어가 설치된 PC에서 실행해 주세요 (Windows\\System32\\wlmData.dll).");
                }
                catch (BadImageFormatException) {
                    b?.Dispose();
                    tcs.SetResult("wlmData.dll의 32/64비트 형식이 앱과 맞지 않습니다.\nWLM 설치 프로그램으로 64비트 DLL(System32)을 설치해 주세요.");
                }
                catch (EntryPointNotFoundException ex) {
                    b?.Dispose();
                    tcs.SetResult("wlmData.dll 버전이 맞지 않습니다: " + ex.Message + "\nWLM 서버 프로그램과 같은 버전의 DLL이 필요합니다.");
                }
                catch (Exception ex) {
                    try { b?.Dispose(); } catch { }
                    tcs.SetResult("파장계 연결 실패: " + ex.Message);
                }
            });
            return tcs.Task;
        }

        /// <summary>연결 해제 (WLM 측정 상태는 바꾸지 않음).</summary>
        public async Task DisconnectAsync() {
            if (!IsConnected) return;
            IsBusy = true;
            try {
                await RunRaw(() => {
                    IWlmBackend? b = _backend;
                    _backend = null;
                    if (b == null) return;
                    try { if (_patternOn) b.SetPattern(WlmConst.cSignal1Interferometers, false); } catch { }
                    _patternOn = false;
                    _polling = false;
                    try { b.Dispose(); } catch { }
                });
            }
            finally {
                IsConnected = false;
                CallbackMode = false;
                LastError = "";
                Status = new WlmStatus();
                Message = "파장계 연결을 끊었습니다. (WLM 측정은 계속될 수 있습니다)";
                _log.Info("[WLM] 연결 해제");
                IsBusy = false;
            }
        }

        // ------------------------------------------------------------------
        // 측정 / 설정
        // ------------------------------------------------------------------

        /// <summary>측정 시작 (Operation cCtrlStartMeasurement).</summary>
        public async Task<bool> StartMeasurementAsync() {
            bool ok = await ApplyAsync("측정 시작", b => b.Operation(WlmConst.cMeasurement));
            if (ok) {
                _log.Info("[WLM] 측정 시작");
                MeasurementStarted?.Invoke();
            }
            return ok;
        }

        /// <summary>측정 정지 (Operation cCtrlStopAll).</summary>
        public async Task<bool> StopMeasurementAsync() {
            bool ok = await ApplyAsync("측정 정지", b => b.Operation(WlmConst.cStop));
            if (ok) _log.Info("[WLM] 측정 정지");
            return ok;
        }

        /// <summary>
        /// WLM 설정 쓰기 (작업 스레드). 반환 코드가 음수면 로그/상태 표시 후 false.
        /// 성공/실패와 관계없이 상태를 다시 읽어 화면을 실제 값으로 맞춘다.
        /// </summary>
        public async Task<bool> ApplyAsync(string what, Func<IWlmBackend, int> set) {
            if (!IsConnected) {
                LastError = what + ": 파장계에 연결되어 있지 않습니다.";
                return false;
            }
            try {
                int rc = await Run(set);
                _statusDirty = true;
                if (rc < 0) {
                    string msg = what + " 실패: " + WlmUnits.SetErrorText(rc);
                    _log.Warn("[WLM] " + msg);
                    _log.SetStatus("[WLM] " + msg);
                    LastError = msg;
                    return false;
                }
                LastError = "";
                return true;
            }
            catch (Exception ex) {
                _statusDirty = true;
                string msg = what + " 실패: " + ex.Message;
                _log.Error("[WLM] " + msg);
                _log.SetStatus("[WLM] " + msg);
                LastError = msg;
                return false;
            }
        }

        /// <summary>간섭 패턴 내보내기 (Show signal). 측정 속도를 조금 낮추므로 필요할 때만 켠다.</summary>
        public async Task SetPatternExportAsync(bool on) {
            _wantPattern = on;
            if (!IsConnected) return;
            try {
                await RunRaw(() => {
                    IWlmBackend? b = _backend;
                    if (b == null) return;
                    int rc = b.SetPattern(WlmConst.cSignal1Interferometers, on);
                    _patternOn = on && rc >= 0;
                    _patternDirty = on;
                });
            }
            catch (Exception ex) { _log.Warn("[WLM] 패턴 표시 설정 실패: " + ex.Message); }
        }

        public void SaveSettings() => Settings.Save();

        public void Dispose() {
            if (_disposed) return;
            _uiTimer.Stop();
            Settings.Save();
            try {
                // 콜백 제거와 DLL 정리는 작업 스레드에서 (최대 2초 대기)
                Task t = RunRaw(() => {
                    IWlmBackend? b = _backend;
                    _backend = null;
                    if (b == null) return;
                    try { if (_patternOn) b.SetPattern(WlmConst.cSignal1Interferometers, false); } catch { }
                    b.Dispose();
                });
                t.Wait(2000);
            }
            catch {
                // 종료 중 오류 무시
            }
            _disposed = true;
            _jobs.CompleteAdding();
        }
    }
}
