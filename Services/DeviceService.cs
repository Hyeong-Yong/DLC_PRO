using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Avalonia.Threading;
using DLC_PRO.Core;
using DLC_PRO.Models;

namespace DLC_PRO.Services {
    /// <summary>
    /// 장비(DLC pro) 접근의 단일 창구 (Model 계층 서비스, DI 싱글톤).
    /// - DlcDevice(통신) + AmpSafety(TA 안전 인터록) + AppSettings(설정)를 소유
    /// - 100 ms 주기 <see cref="Tick"/> (UI 스레드): ViewModel들이 장비 캐시 값을 화면 속성으로 옮기는 시점
    /// - 백그라운드 스레드 이벤트(연결 변화, 안전 차단)를 UI 스레드로 전달
    /// WinForms 버전의 AppCtx + Binder 역할을 합친 것.
    /// </summary>
    public sealed class DeviceService : IDisposable {
        private readonly LogService _log;
        private readonly DispatcherTimer _timer;
        private readonly ConcurrentQueue<Action> _uiQueue = new ConcurrentQueue<Action>();
        private bool _disposed;

        public DlcDevice Device { get; }
        public AmpSafety Safety { get; }
        public AppSettings Settings { get; }

        /// <summary>100 ms마다 (UI 스레드).</summary>
        public event Action? Tick;

        /// <summary>주파수 축 설정이 바뀌었을 때 (UI 스레드) — 모든 그래프/입력란 동기화용.</summary>
        public event Action? FreqSettingsChanged;

        /// <summary>연결 상태 변화 (UI 스레드).</summary>
        public event Action<bool>? ConnectionChanged;

        /// <summary>소프트웨어 안전 인터록이 증폭기를 차단함 (UI 스레드, 사유).</summary>
        public event Action<string>? SafetyTripped;

        public DeviceService(LogService log) {
            _log = log;
            Settings = AppSettings.Load();
            Device = new DlcDevice { LogTraffic = Settings.LogTraffic, MaxScopeRate = Settings.ScopeMaxRate };
            Safety = new AmpSafety(Device, Settings.Safety);

            Device.LogMessage += (level, msg) => _log.Add(level, msg);
            Device.ConnectionChanged += c => _uiQueue.Enqueue(() => ConnectionChanged?.Invoke(c));
            Safety.Tripped += reason => _uiQueue.Enqueue(() => {
                _log.Error("[안전 차단] " + reason);
                SafetyTripped?.Invoke(reason);
            });

            // 상태/표시용 공통 파라미터
            Device.WatchMany(300, P.Emission, P.InterlockOpen, P.SystemHealthTxt, P.LockState, P.LockStateTxt,
                P.UserLevel, P.MsgCountNew, P.LaserType);

            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => OnTick());
            _timer.Start();
        }

        public bool IsConnected => Device.IsConnected;

        private void OnTick() {
            int n = 0;
            while (n++ < 100 && _uiQueue.TryDequeue(out Action? a)) {
                try { a(); }
                catch (Exception ex) { _log.Error("UI 처리 오류: " + ex.Message); }
            }
            Action? t = Tick;
            if (t == null) return;
            // 한 구독자의 예외가 다른 화면 갱신을 막지 않도록 개별 호출
            foreach (Delegate d in t.GetInvocationList()) {
                try { ((Action)d)(); }
                catch (Exception ex) { _log.Error("화면 갱신 오류: " + ex.Message); }
            }
        }

        // ------------------------------------------------------------------
        // 연결
        // ------------------------------------------------------------------

        public Task ConnectTcpAsync(string host) =>
            Task.Run(() => Device.ConnectTcp(host, Settings.CmdPort, Settings.MonPort));

        public Task ConnectSerialAsync(string port) => Task.Run(() => Device.ConnectSerial(port));

        public Task DisconnectAsync() => Task.Run(() => Device.Disconnect());

        // ------------------------------------------------------------------
        // 쓰기 도우미 (실패 시 로그 + 상태 표시줄)
        // ------------------------------------------------------------------

        /// <summary>장비에 값 쓰기. 성공 여부 반환 (실패는 오류로 보고).</summary>
        public async Task<bool> SetAsync(string param, object value) {
            if (!Device.IsConnected) {
                ReportError("장비에 연결되어 있지 않습니다.");
                return false;
            }
            try {
                await Device.SetAsync(param, value);
                return true;
            }
            catch (Exception ex) {
                ReportError(param + " 설정 실패: " + Unwrap(ex).Message);
                return false;
            }
        }

        /// <summary>명령 실행. 실패 시 null.</summary>
        public async Task<string?> ExecAsync(string cmd, params object[] args) {
            if (!Device.IsConnected) {
                ReportError("장비에 연결되어 있지 않습니다.");
                return null;
            }
            try { return await Device.ExecAsync(cmd, args); }
            catch (Exception ex) {
                ReportError(cmd + " 실행 실패: " + Unwrap(ex).Message);
                return null;
            }
        }

        public void ReportError(string msg) {
            _log.Error(msg);
            _log.SetStatus(msg);
        }

        public void ReportError(Exception ex) => ReportError(Unwrap(ex).Message);

        public static Exception Unwrap(Exception ex) => ex is AggregateException ae ? ae.GetBaseException() : ex;

        // ------------------------------------------------------------------
        // 설정
        // ------------------------------------------------------------------

        public void SaveSettings() => Settings.Save();

        /// <summary>주파수 축 설정을 저장하고 모든 화면에 알림.</summary>
        public void NotifyFreqChanged() {
            Settings.Save();
            FreqSettingsChanged?.Invoke();
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
            try { Safety.Dispose(); }
            catch { }
            try { Device.Disconnect(); }
            catch { }
            Settings.Save();
        }
    }
}
