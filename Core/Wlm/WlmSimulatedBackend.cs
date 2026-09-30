using System;
using System.Threading;

namespace DLC_PRO.Core.Wlm {
    /// <summary>
    /// WS/6 동작을 흉내 내는 시뮬레이터 (--wlm-sim 옵션, 테스트/화면 확인용).
    /// 실제 장비와 통신하지 않는다. 측정 중이면 노출 시간 간격으로 852.3595 nm 부근 값을 콜백으로 보낸다.
    /// </summary>
    public sealed class WlmSimulatedBackend : IWlmBackend {
        private readonly object _lk = new object();
        private readonly Random _rnd = new Random(7294);
        private Thread? _thread;
        private volatile bool _stop;
        private Action<int, int, int, double, int>? _cb;
        private readonly DateTime _t0 = DateTime.UtcNow;

        private int _op = WlmConst.cStop, _resultMode, _range, _pulse, _wide, _expo = 20, _interval = 0, _autoCal = 1,
            _acPeriod = 30, _acUnit = WlmConst.cACDays, _avgCount = 1, _avgMode = WlmConst.cAvrgFloating, _avgType = WlmConst.cAvrgSimple;
        private bool _fast, _expoAuto = true, _intervalMode, _pattern;
        private double _last = WlmConst.ErrNoValue;
        private bool _returnOnce;
        private bool _lastRead;

        /// <summary>시뮬레이션할 기본 파장 (진공, nm).</summary>
        public double BaseWavelength { get; set; } = 852.35952;

        /// <summary>테스트용: 신호 세기 (1 = 적정, &lt;0.2 = 약함, &gt;3 = 과다).</summary>
        public double SignalLevel { get; set; } = 1.0;

        public string Name => "시뮬레이터";

        public bool IsServerRunning() => true;
        public int StartServer() => 1;

        public bool InstallCallback(Action<int, int, int, double, int> callback) {
            RemoveCallback();
            _cb = callback;
            _stop = false;
            _thread = new Thread(Run) { IsBackground = true, Name = "WLM sim" };
            _thread.Start();
            return true;
        }

        public void RemoveCallback() {
            _stop = true;
            _thread?.Join(1000);
            _thread = null;
            _cb = null;
        }

        public void SetReturnMode(int mode) => _returnOnce = mode == 1;

        private int Ms => (int)(DateTime.UtcNow - _t0).TotalMilliseconds;

        private double Level() {
            double effExpo = _expoAuto ? 20 : _expo;
            return SignalLevel * effExpo / 20.0;
        }

        private double Measure() {
            double lvl = Level();
            if (lvl < 0.05) return WlmConst.ErrNoSignal;
            if (lvl < 0.2) return WlmConst.ErrLowSignal;
            if (lvl > 3) return WlmConst.ErrBigSignal;
            double t = (DateTime.UtcNow - _t0).TotalSeconds;
            double drift = 2e-6 * Math.Sin(t / 20.0) + 4e-7 * Math.Sin(t / 3.1);
            double noise = (_rnd.NextDouble() - 0.5) * (_wide == 1 ? 4e-6 : 6e-7);
            return BaseWavelength + drift + noise;
        }

        private void Run() {
            int lastT = 0;
            while (!_stop) {
                int wait;
                lock (_lk) wait = Math.Max(5, (_expoAuto ? 20 : _expo) + 5 + (_intervalMode ? _interval : 0));
                Thread.Sleep(Math.Min(wait, 200));
                if (_stop) break;
                Action<int, int, int, double, int>? cb = _cb;
                double v;
                bool measuring;
                lock (_lk) {
                    measuring = _op == WlmConst.cMeasurement;
                    v = measuring ? Measure() : 0;
                    if (measuring) { _last = v; _lastRead = false; }
                }
                if (measuring) {
                    cb?.Invoke(7294, WlmConst.cmiWavelength1, Ms, v, 0);
                    if (_pattern) cb?.Invoke(7294, WlmConst.cmiPatternAnalysisWritten, 1, 0, 0);
                }
                if (Ms - lastT > 1000) {
                    lastT = Ms;
                    cb?.Invoke(7294, WlmConst.cmiTemperature, Ms, GetTemperature(), 0);
                    cb?.Invoke(7294, WlmConst.cmiPressure, Ms, GetPressure(), 0);
                }
            }
        }

        private void Notify(int mode, int value) => _cb?.Invoke(7294, mode, value, 0, 0);

        public int GetWLMVersion(int detail) => detail switch { 0 => 6, 1 => 7294, 2 => 0, _ => 0 };

        public double GetWavelengthNum(int channel) {
            lock (_lk) {
                if (_op != WlmConst.cMeasurement) return _last;
                if (_returnOnce) {
                    if (_lastRead) return WlmConst.ErrNoValue;
                    _lastRead = true;
                }
                return _last;
            }
        }

        public double GetFrequencyNum(int channel) {
            double wl = GetWavelengthNum(channel);
            return wl > 0 ? 299792.458 / wl : wl;
        }

        public double ConvertUnit(double value, int from, int to) => WlmUnits.ConvertAnalytic(value, from, to, GetTemperature(), GetPressure());

        public double GetTemperature() => 26.3 + 0.02 * Math.Sin((DateTime.UtcNow - _t0).TotalSeconds / 60);
        public double GetPressure() => 1011.0;
        public bool GetLinkState() => false;
        public double GetPowerNum(int channel) => WlmConst.ErrNotAvailable;

        public int GetOperationState() { lock (_lk) return _op; }

        public int Operation(int op) {
            lock (_lk) _op = op == WlmConst.cMeasurement ? WlmConst.cMeasurement : op == WlmConst.cAdjustment ? WlmConst.cAdjustment : WlmConst.cStop;
            Notify(WlmConst.cmiOperation, _op);
            return 0;
        }

        public int GetResultMode() => _resultMode;
        public int SetResultMode(int mode) { if (mode < 0 || mode > 4) return -3; _resultMode = mode; Notify(WlmConst.cmiResultMode, mode); return 0; }
        public int GetRange() => _range;
        public int SetRange(int range) { if (range == WlmConst.cRangeModelByOrder) return 0; if (range < 0 || range > 1) return -3; _range = range; Notify(WlmConst.cmiRange, range); return 0; }
        public int GetPulseMode() => _pulse;
        public int SetPulseMode(int mode) { if (mode < 0 || mode > 1) return -3; _pulse = mode; Notify(WlmConst.cmiPulse, mode); return 0; }
        public int GetWideMode() => _wide;
        public int SetWideMode(int mode) { if (mode < 0 || mode > 1) return -3; _wide = mode; Notify(WlmConst.cmiWideLine, mode); return 0; }
        public bool GetFastMode() => _fast;
        public int SetFastMode(bool fast) { _fast = fast; Notify(WlmConst.cmiFast, fast ? 1 : 0); return 0; }

        public int GetExposureNum(int channel, int array) => array == 1 ? _expo : WlmConst.ErrNotAvailable;
        public int SetExposureNum(int channel, int array, int ms) {
            if (array != 1) return -6;
            if (ms < 1 || ms > 2000) return -3;
            lock (_lk) _expo = ms;
            Notify(WlmConst.cmiExposureValue1, ms);
            return 0;
        }
        public bool GetExposureModeNum(int channel) => _expoAuto;
        public int SetExposureModeNum(int channel, bool auto) { lock (_lk) _expoAuto = auto; Notify(WlmConst.cmiExposureMode, auto ? 1 : 0); return 0; }
        public int GetExposureRange(int which) => which == WlmConst.cExpoMin ? 1 : which == WlmConst.cExpoMax ? 2000 : WlmConst.ErrNotAvailable;

        public int GetInterval() => _interval;
        public int SetInterval(int ms) { if (ms < 0 || ms > 9999990) return -3; lock (_lk) _interval = ms; Notify(WlmConst.cmiInterval, ms); return 0; }
        public bool GetIntervalMode() => _intervalMode;
        public int SetIntervalMode(bool on) { lock (_lk) _intervalMode = on; Notify(WlmConst.cmiIntervalMode, on ? 1 : 0); return 0; }

        public int GetAutoCalMode() => _autoCal;
        public int SetAutoCalMode(int on) { _autoCal = on != 0 ? 1 : 0; Notify(WlmConst.cmiAutoCalMode, _autoCal); return 0; }
        public int GetAutoCalSetting(int what, out int value) {
            value = what == WlmConst.cmiAutoCalPeriod ? _acPeriod : what == WlmConst.cmiAutoCalUnit ? _acUnit : 0;
            return what == WlmConst.cmiAutoCalPeriod || what == WlmConst.cmiAutoCalUnit ? 1 : WlmConst.ErrNotAvailable;
        }
        public int SetAutoCalSetting(int what, int value) {
            if (what == WlmConst.cmiAutoCalPeriod) { if (value < 1) return -3; _acPeriod = value; return 0; }
            if (what == WlmConst.cmiAutoCalUnit) { if (value < 0 || value > 4) return -3; _acUnit = value; return 0; }
            return -6;
        }

        public int GetAveragingSettingNum(int channel, int what) => what switch {
            WlmConst.cmiAveragingCount => _avgCount,
            WlmConst.cmiAveragingMode => _avgMode,
            WlmConst.cmiAveragingType => _avgType,
            _ => WlmConst.ErrNotAvailable,
        };

        public int SetAveragingSettingNum(int channel, int what, int value) {
            switch (what) {
                case WlmConst.cmiAveragingCount: if (value < 1 || value > 1000) return -3; _avgCount = value; return 0;
                case WlmConst.cmiAveragingMode: _avgMode = value; return 0;
                case WlmConst.cmiAveragingType: _avgType = value; return 0;
                default: return -6;
            }
        }

        public int SetPattern(int index, bool enable) { _pattern = enable; return 0; }
        public int GetPatternItemCount(int index) => 2048;
        public int GetPatternItemSize(int index) => 2;

        public int GetPatternData(int channel, int index, double[] target) {
            if (!_pattern) return 0;
            double lvl = Math.Min(Level(), 4);
            double t = (DateTime.UtcNow - _t0).TotalSeconds;
            int n = Math.Min(2048, target.Length);
            for (int i = 0; i < n; i++) {
                // 4개 Fizeau 간섭계를 흉내 낸 줄무늬 (각 512 픽셀)
                int seg = i / 512, x = i % 512;
                double env = Math.Exp(-Math.Pow((x - 256) / 180.0, 2));
                double period = 18 + seg * 7;
                double v = 400 + 1800 * lvl * env * (0.5 + 0.5 * Math.Cos(2 * Math.PI * x / period + t * 0.3 + seg));
                target[i] = Math.Min(4095, v + _rnd.Next(-15, 15));
            }
            return n;
        }

        public void Dispose() => RemoveCallback();
    }
}
