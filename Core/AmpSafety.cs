#nullable disable
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace DLC_PRO.Core
{
    /// <summary>증폭기(TA) 소프트웨어 안전 설정.</summary>
    public sealed class AmpSafetySettings
    {
        /// <summary>증폭기 동작에 필요한 최소 seed 파워 (mW). 0 이하이면 장비 값(seed-limits:power-min) 사용.</summary>
        public double MinSeedPowerMw = 0;
        /// <summary>소프트웨어 최대 증폭기 전류 (mA). 0 이하이면 장비 current-clip 사용.</summary>
        public double MaxAmpCurrentMa = 0;
        /// <summary>전류 상승 시 1 스텝 크기 (mA). 1 미만이면 100으로 취급.</summary>
        public double RampStepMa = 100;
        /// <summary>스텝 간 대기 (ms).</summary>
        public int RampIntervalMs = 200;
        /// <summary>이 값 이상 한 번에 올리면 확인창 (mA).</summary>
        public double ConfirmDeltaMa = 300;
        /// <summary>seed 부족 감시 (증폭기 켜진 상태에서 seed가 기준 미만이면 증폭기 OFF).</summary>
        public bool WatchdogEnabled = true;
        /// <summary>seed 부족이 이 시간 이상 지속되면 차단 (ms).</summary>
        public int WatchdogDelayMs = 300;

        /// <summary>파일/입력값 보정 (NaN, 음수, 0 스텝 방지).</summary>
        public void Sanitize()
        {
            if (double.IsNaN(MinSeedPowerMw) || double.IsInfinity(MinSeedPowerMw) || MinSeedPowerMw < 0) MinSeedPowerMw = 0;
            if (double.IsNaN(MaxAmpCurrentMa) || double.IsInfinity(MaxAmpCurrentMa) || MaxAmpCurrentMa < 0) MaxAmpCurrentMa = 0;
            if (double.IsNaN(RampStepMa) || double.IsInfinity(RampStepMa) || RampStepMa < 1) RampStepMa = 100;
            if (RampIntervalMs < 20) RampIntervalMs = 20;
            if (RampIntervalMs > 10000) RampIntervalMs = 10000;
            if (double.IsNaN(ConfirmDeltaMa) || double.IsInfinity(ConfirmDeltaMa) || ConfirmDeltaMa < 0) ConfirmDeltaMa = 300;
            if (WatchdogDelayMs < 50) WatchdogDelayMs = 50;
            if (WatchdogDelayMs > 10000) WatchdogDelayMs = 10000;
        }
    }

    /// <summary>
    /// TA 증폭기 보호 로직 (장비 자체 보호 기능에 더한 소프트웨어 인터록).
    /// 1) 증폭기 ON 전 조건 검사: 인터록 닫힘, 마스터 CC ON, seed 파워 ≥ 기준, 증폭기 TC ready
    /// 2) ON은 낮은 전류에서 시작해 설정 전류까지 램프, 최대값 제한
    /// 3) 감시: 증폭기 ON 중 seed 부족/값 확인 불가/마스터 OFF가 지속되면 즉시 증폭기 OFF
    /// 4) 마스터 CC를 끄기 전 증폭기를 먼저 끔
    /// 안전 명령은 DlcDevice의 우선순위 큐로 보낸다.
    /// </summary>
    public sealed class AmpSafety : IDisposable
    {
        private readonly DlcDevice _dev;
        private readonly Timer _timer;
        private DateTime? _faultSince;

        private int _tickBusy;          // Tick 중복 실행 방지 (Interlocked)

        private bool _episodeReported;  // 한 번의 이상 상황에 Tripped 한 번만
        private CancellationTokenSource _rampCts;
        private int _rampCount;
        private readonly object _rampLock = new object();
        private long _operationVersion;
        private volatile bool _disposed;
        private long _watchSession = -1;
        public long OperationVersion => Interlocked.Read(ref _operationVersion);

        public AmpSafetySettings Settings { get; private set; }

        /// <summary>안전 차단 발생 (사유). 백그라운드 스레드에서 발생.</summary>
        public event Action<string> Tripped;

        public bool Ramping { get { return Volatile.Read(ref _rampCount) > 0; } }

        public AmpSafety(DlcDevice dev, AmpSafetySettings settings)
        {
            _dev = dev;
            Settings = settings ?? new AmpSafetySettings();
            Settings.Sanitize();
            _dev.WatchMany(100, P.AmpCcEnabled, P.AmpSeedPower, P.AmpCcCurrentSet, P.AmpCcCurrentAct);
            _dev.WatchMany(500, P.AmpCcCurrentClip, P.AmpSeedPowerMin, P.AmpTcReady, P.DlCcEnabled, P.InterlockOpen, P.LaserType);
            _timer = new Timer(Tick, null, 500, 100);
            _dev.ConnectionChanged += OnConnectionChanged;
        }

        private void OnConnectionChanged(bool connected) { CancelRamp(); }

        /// <summary>laser1:type에 TA가 있는지. 아직 모르면 null.</summary>
        public bool? AmplifierKnown
        {
            get
            {
                string t = _dev.GetString(P.LaserType);
                if (t == null) return null;
                return t.IndexOf("TA", StringComparison.OrdinalIgnoreCase) >= 0 || t.IndexOf("MOPA", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        public bool HasAmplifier { get { return AmplifierKnown ?? false; } }

        public double EffectiveMinSeed
        {
            get
            {
                if (Settings.MinSeedPowerMw > 0) return Settings.MinSeedPowerMw;
                double d; return _dev.TryGetDouble(P.AmpSeedPowerMin, out d) && d > 0 ? d : 1.0;
            }
        }

        /// <summary>허용 최대 증폭기 전류. 알 수 없으면 NaN.</summary>
        public double EffectiveMaxCurrent
        {
            get
            {
                double clip; bool hasClip = _dev.TryGetDouble(P.AmpCcCurrentClip, out clip) && clip > 0;
                if (Settings.MaxAmpCurrentMa > 0) return hasClip ? Math.Min(clip, Settings.MaxAmpCurrentMa) : Settings.MaxAmpCurrentMa;
                return hasClip ? clip : double.NaN;
            }
        }

        private double StepMa { get { return Settings.RampStepMa >= 1 ? Settings.RampStepMa : 100; } }

        /// <summary>증폭기를 켜도 되는지 검사. 불가하면 사유 반환, 가능하면 null.</summary>
        public string CheckEnableAmp()
        {
            bool b;
            if (!_dev.TryGetBool(P.InterlockOpen, out b)) return "인터록 상태를 읽을 수 없습니다.";
            if (b) return "인터록이 열려 있습니다.";
            if (!_dev.TryGetBool(P.DlCcEnabled, out b)) return "마스터 레이저 상태를 읽을 수 없습니다.";
            if (!b) return "마스터 레이저(CC) 전류가 꺼져 있습니다. 마스터를 먼저 켜세요.";
            double seed;
            if (!_dev.TryGetDouble(P.AmpSeedPower, out seed) || double.IsNaN(seed)) return "seed 파워 값을 읽을 수 없습니다 (" + P.AmpSeedPower + ").";
            if (seed < EffectiveMinSeed)
                return string.Format(CultureInfo.InvariantCulture, "seed 파워 부족: {0:F2} mW < 기준 {1:F2} mW", seed, EffectiveMinSeed);
            if (!_dev.TryGetBool(P.AmpTcReady, out b)) return "증폭기 온도 준비 상태를 읽을 수 없습니다.";
            if (!b) return "증폭기 온도가 아직 안정되지 않았습니다 (TC not ready).";
            if (double.IsNaN(EffectiveMaxCurrent)) return "최대 허용 전류(current-clip)를 읽을 수 없습니다.";
            return null;
        }

        /// <summary>
        /// 증폭기 ON: 조건 재확인 → 설정 전류가 한 스텝보다 크면 한 스텝 값으로 낮춘 뒤 ON → 원래 설정 전류까지 램프.
        /// </summary>
        public Task EnableAmpAsync(IProgress<double> progress = null) =>
            RunRampAsync(true, 0, progress);

        public Task SetAmpCurrentAsync(double target, IProgress<double> progress = null)
        {
            if (!double.IsFinite(target) || target < 0)
                throw new ArgumentOutOfRangeException(nameof(target), "전류는 유한한 0 이상의 값이어야 합니다.");
            return RunRampAsync(false, target, progress);
        }

        private async Task RunRampAsync(bool enable, double target, IProgress<double> progress)
        {
            CancellationTokenSource cts;
            long session = _dev.SessionVersion;
            lock (_rampLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _rampCts?.Cancel();
                cts = new CancellationTokenSource();
                _rampCts = cts;
                Interlocked.Increment(ref _rampCount);
            }
            var token = cts.Token;
            void CheckOperation()
            {
                token.ThrowIfCancellationRequested();
                if (!_dev.IsConnected || _dev.SessionVersion != session)
                    throw new OperationCanceledException("연결이 변경되어 작업을 취소했습니다.");
            }
            // 큐에 넣을 때뿐 아니라 실제 실행 직전에도 취소 여부를 검사한다.
            Task<T> Run<T>(Func<DecofClient, T> action) => _dev.RunAsync(c =>
            {
                CheckOperation();
                return action(c);
            });
            Task Write(string name, object value) => Run(c =>
            {
                return _dev.SetAndReadBack(c, name, value);
            });
            bool protectionStarted = false;
            try
            {
                double cur = await Run(c => c.GetDouble(P.AmpCcCurrentSet));
                bool ampOn = await Run(c => c.GetBool(P.AmpCcEnabled));
                if (enable) target = cur;
                if (!double.IsFinite(cur) || cur < 0 || !double.IsFinite(target) || target < 0)
                    throw new InvalidOperationException("증폭기 전류 값이 유효하지 않습니다.");
                if (enable || target > cur)
                    await Run(c => { CheckCurrentLimit(c, target); return 0; });
                protectionStarted = enable || (ampOn && target > cur);
                if (enable)
                {
                    await Run(c => { CheckLiveConditions(c); return 0; });
                    cur = Math.Min(target, StepMa);
                    await Write(P.AmpCcCurrentSet, Math.Round(cur, 3));
                    await Run(c =>
                    {
                        CheckLiveConditions(c);
                        CheckOperation();
                        _dev.SetAndReadBack(c, P.AmpCcEnabled, true);
                        if (!c.GetBool(P.AmpCcEnabled)) throw new InvalidOperationException("증폭기 ON 확인 실패");
                        return 0;
                    });
                    ampOn = true;
                    progress?.Report(cur);
                }
                if (target <= cur || !ampOn)
                {
                    await Write(P.AmpCcCurrentSet, target);
                    progress?.Report(target);
                    return;
                }
                while (cur < target - 1e-6)
                {
                    await Task.Delay(Math.Max(20, Settings.RampIntervalMs), token);
                    double next = Math.Min(target, cur + StepMa);
                    await Run(c =>
                    {
                        CheckLiveConditions(c);
                        CheckCurrentLimit(c, target);
                        if (!c.GetBool(P.AmpCcEnabled)) throw new InvalidOperationException("증폭기가 꺼져 램프를 중단합니다.");
                        CheckOperation();
                        _dev.SetAndReadBack(c, P.AmpCcCurrentSet, Math.Round(next, 3));
                        return 0;
                    });
                    cur = next;
                    progress?.Report(cur);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // 보호 조건/통신 확인 실패 후 중간 전류로 계속 동작하지 않게 한다.
                if (protectionStarted && _dev.IsConnected && _dev.SessionVersion == session && !token.IsCancellationRequested)
                {
                    try { await AmpOffAsync(); }
                    catch (Exception ex) { _dev.Log("ERROR", "증폭기 OFF 확인 실패: " + ex.Message); }
                }
                throw;
            }
            finally
            {
                lock (_rampLock)
                {
                    if (_rampCts == cts) _rampCts = null;
                    cts.Dispose();
                    Interlocked.Decrement(ref _rampCount);
                }
            }
        }

        private void CheckCurrentLimit(DecofClient c, double target)
        {
            double max = c.GetDouble(P.AmpCcCurrentClip);
            if (Settings.MaxAmpCurrentMa > 0) max = Math.Min(max, Settings.MaxAmpCurrentMa);
            if (!double.IsFinite(max) || max <= 0 || target > max)
                throw new InvalidOperationException("목표 전류가 최대 허용 전류를 초과하거나 제한값을 확인할 수 없습니다.");
        }

        private void CheckLiveConditions(DecofClient c)
        {
            if (c.GetBool(P.InterlockOpen)) throw new InvalidOperationException("인터록이 열려 있습니다.");
            if (!c.GetBool(P.DlCcEnabled)) throw new InvalidOperationException("마스터 전류가 꺼져 있습니다.");
            if (!c.GetBool(P.AmpTcReady)) throw new InvalidOperationException("증폭기 온도가 안정되지 않았습니다.");
            double min = Settings.MinSeedPowerMw > 0 ? Settings.MinSeedPowerMw : c.GetDouble(P.AmpSeedPowerMin);
            double seed = c.GetDouble(P.AmpSeedPower);
            if (!double.IsFinite(min) || min <= 0 || !double.IsFinite(seed) || seed < min)
                throw new InvalidOperationException("seed 파워가 부족하거나 기준값을 확인할 수 없습니다.");
        }

        public void CancelRamp()
        {
            lock (_rampLock)
            {
                Interlocked.Increment(ref _operationVersion);
                _rampCts?.Cancel();
            }
        }
        /// <summary>증폭기 즉시 OFF (비상, 우선순위 큐).</summary>
        public async Task AmpOffAsync()
        {
            CancelRamp();
            await _dev.RunPriorityAsync(c =>
            {
                _dev.SetAndReadBack(c, P.AmpCcEnabled, false);
                if (c.GetBool(P.AmpCcEnabled)) throw new InvalidOperationException("증폭기 OFF를 확인하지 못했습니다.");
                return 0;
            });
        }

        /// <summary>
        /// 안전 순서로 전체 OFF: 증폭기 → (꺼졌는지 확인) → 마스터.
        /// 증폭기 OFF에 실패하면 마스터는 끄지 않는다 (seed 없는 TA 보호).
        /// </summary>
        public Task AllOffAsync()
        {
            CancelRamp();
            return _dev.RunPriorityAsync(c =>
            {
                bool ampExists = true;
                try { _dev.SetAndReadBack(c, P.AmpCcEnabled, false); }
                catch (DecofException ex)
                {
                    if (ex.Code == -3 && AmplifierKnown == false) ampExists = false;
                    else throw new InvalidOperationException("증폭기 OFF 실패 — 마스터는 끄지 않았습니다: " + ex.Message, ex);
                }
                if (ampExists && c.GetBool(P.AmpCcEnabled))
                    throw new InvalidOperationException("증폭기가 아직 켜져 있습니다 — 마스터는 끄지 않았습니다.");
                return _dev.SetAndReadBack(c, P.DlCcEnabled, false);
            });
        }
        private async void Tick(object state)
        {
            if (Interlocked.Exchange(ref _tickBusy, 1) == 1) return;
            long session = _dev.SessionVersion;
            try
            {
                if (_watchSession != session) { _watchSession = session; _faultSince = null; _episodeReported = false; }
                if (_disposed || !_dev.IsConnected || !Settings.WatchdogEnabled || AmplifierKnown == false)
                { _faultSince = null; return; }
                string reason = null;
                try
                {
                    // 모니터링은 값이 바뀔 때만 전송된다. 오래된 캐시를 안전 판단에 사용하지 않는다.
                    await _dev.RunAsync(c =>
                    {
                        if (_disposed || _dev.SessionVersion != session) throw new OperationCanceledException();
                        if (!c.GetBool(P.AmpCcEnabled)) { _episodeReported = false; return 0; }
                        CheckLiveConditions(c);
                        return 0;
                    });
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { reason = "안전 상태 확인 실패: " + ex.Message; }
                if (_disposed || _dev.SessionVersion != session || !_dev.IsConnected) return;
                if (reason == null) { _faultSince = null; return; }
                if (_faultSince == null) { _faultSince = DateTime.UtcNow; return; }
                if ((DateTime.UtcNow - _faultSince.Value).TotalMilliseconds < Settings.WatchdogDelayMs) return;
                _faultSince = null;
                try
                {
                    await AmpOffAsync();
                    reason += "\n증폭기 OFF 확인 완료.";
                }
                catch (Exception ex) { reason += "\n증폭기 OFF 확인 실패 — 장비를 직접 확인하세요: " + ex.Message; }
                _dev.Log("ERROR", "[안전 차단] " + reason);
                if (!_episodeReported)
                {
                    _episodeReported = true;
                    try { Tripped?.Invoke(reason); } catch { }
                }
            }
            catch (Exception ex) { _dev.Log("ERROR", "안전 감시 오류: " + ex.Message); }
            finally { Volatile.Write(ref _tickBusy, 0); }
        }
        public void Dispose()
        {
            _disposed = true;
            _dev.ConnectionChanged -= OnConnectionChanged;
            CancelRamp();
            _timer.Dispose();
        }
    }
}
