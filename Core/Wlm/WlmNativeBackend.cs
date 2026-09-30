using System;
using System.Runtime.InteropServices;

namespace DLC_PRO.Core.Wlm {
    /// <summary>
    /// 실제 HighFinesse wlmData.dll 호출 (P/Invoke).
    /// - wlmData.dll은 WLM 설치 시 Windows\System32에 설치된 64비트 DLL을 사용한다 (서버 프로그램과 버전이 같아야 함).
    ///   앱 폴더에 복사하지 않는다.
    /// - 이 DLL은 실행 중인 WLM 서버 프로그램("Wavelength Meter WS/6 …")과 프로세스 간 통신한다.
    ///   즉 WLM 프로그램을 켜 둔 상태에서 같은 PC에서 이 앱을 실행해야 한다.
    /// - 형식은 C 헤더(wlmData.h) 기준: long = 32비트, 포인터 인자 = IntPtr, bool = 1바이트.
    ///   (동봉된 C# 헤더의 Instantiate/ControlWLM 64비트 long 선언은 쓰지 않는다.)
    /// </summary>
    public sealed class WlmNativeBackend : IWlmBackend {
        private const string Dll = "wlmData.dll";

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void CallbackProcEx(int ver, int mode, int intVal, double dblVal, int res1);

        [DllImport(Dll)] private static extern IntPtr Instantiate(int rfc, int mode, IntPtr p1, int p2);
        [DllImport(Dll)] private static extern int ControlWLM(int action, IntPtr app, int ver);
        [DllImport(Dll)] private static extern int GetWLMVersion(int ver);
        [DllImport(Dll)] private static extern double GetWavelengthNum(int num, double wl);
        [DllImport(Dll)] private static extern double GetFrequencyNum(int num, double f);
        [DllImport(Dll)] private static extern double ConvertUnit(double val, int uFrom, int uTo);
        [DllImport(Dll)] private static extern double GetTemperature(double t);
        [DllImport(Dll)] private static extern double GetPressure(double p);
        [DllImport(Dll)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool GetLinkState([MarshalAs(UnmanagedType.U1)] bool ls);
        [DllImport(Dll)] private static extern double GetPowerNum(int num, double p);
        [DllImport(Dll)] private static extern ushort GetOperationState(ushort op);
        [DllImport(Dll)] private static extern int Operation(ushort op);
        [DllImport(Dll)] private static extern ushort GetResultMode(ushort rm);
        [DllImport(Dll)] private static extern int SetResultMode(ushort rm);
        [DllImport(Dll)] private static extern ushort GetRange(ushort r);
        [DllImport(Dll)] private static extern int SetRange(ushort r);
        [DllImport(Dll)] private static extern ushort GetPulseMode(ushort pm);
        [DllImport(Dll)] private static extern int SetPulseMode(ushort pm);
        [DllImport(Dll)] private static extern ushort GetWideMode(ushort wm);
        [DllImport(Dll)] private static extern int SetWideMode(ushort wm);
        [DllImport(Dll)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool GetFastMode([MarshalAs(UnmanagedType.U1)] bool fm);
        [DllImport(Dll)] private static extern int SetFastMode([MarshalAs(UnmanagedType.U1)] bool fm);
        [DllImport(Dll)] private static extern int GetExposureNum(int num, int arr, int e);
        [DllImport(Dll)] private static extern int SetExposureNum(int num, int arr, int e);
        [DllImport(Dll)] private static extern int GetExposureModeNum(int num, [MarshalAs(UnmanagedType.U1)] bool em);
        [DllImport(Dll)] private static extern int SetExposureModeNum(int num, [MarshalAs(UnmanagedType.U1)] bool em);
        [DllImport(Dll)] private static extern int GetExposureRange(int er);
        [DllImport(Dll)] private static extern int GetInterval(int i);
        [DllImport(Dll)] private static extern int SetInterval(int i);
        [DllImport(Dll)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool GetIntervalMode([MarshalAs(UnmanagedType.U1)] bool im);
        [DllImport(Dll)] private static extern int SetIntervalMode([MarshalAs(UnmanagedType.U1)] bool im);
        [DllImport(Dll)] private static extern int GetAutoCalMode(int acm);
        [DllImport(Dll)] private static extern int SetAutoCalMode(int acm);
        [DllImport(Dll)] private static extern int GetAutoCalSetting(int acs, ref int val, int res1, ref int res2);
        [DllImport(Dll)] private static extern int SetAutoCalSetting(int acs, int val, int res1, int res2);
        [DllImport(Dll)] private static extern int GetAveragingSettingNum(int num, int avs, int value);
        [DllImport(Dll)] private static extern int SetAveragingSettingNum(int num, int avs, int value);
        [DllImport(Dll)] private static extern int SetPattern(int index, int iEnable);
        [DllImport(Dll)] private static extern int GetPatternItemCount(int index);
        [DllImport(Dll)] private static extern int GetPatternItemSize(int index);
        [DllImport(Dll)] private static extern int GetPatternDataNum(int chn, int index, IntPtr pArray);

        private CallbackProcEx? _callback;   // GC 방지: 설치되어 있는 동안 참조 유지
        private Action<int, int, int, double, int>? _target;
        private bool _callbackInstalled;

        public string Name => "wlmData.dll";

        public bool IsServerRunning() => Instantiate(WlmConst.cInstCheckForWLM, 0, IntPtr.Zero, 0) != IntPtr.Zero;

        public int StartServer() => ControlWLM(WlmConst.cCtrlWLMShow, IntPtr.Zero, 0);

        public bool InstallCallback(Action<int, int, int, double, int> callback) {
            RemoveCallback();
            _target = callback;
            _callback = OnCallback;
            IntPtr fp = Marshal.GetFunctionPointerForDelegate(_callback);
            _callbackInstalled = Instantiate(WlmConst.cInstNotification, WlmConst.cNotifyInstallCallbackEx, fp, 0) != IntPtr.Zero;
            if (!_callbackInstalled) {
                _callback = null;
                _target = null;
            }
            return _callbackInstalled;
        }

        private void OnCallback(int ver, int mode, int intVal, double dblVal, int res1) {
            try { _target?.Invoke(ver, mode, intVal, dblVal, res1); }
            catch {
                // 네이티브 스레드로 예외가 넘어가면 프로세스가 종료되므로 무시
            }
        }

        public void RemoveCallback() {
            if (!_callbackInstalled) return;
            _callbackInstalled = false;
            try { Instantiate(WlmConst.cInstNotification, WlmConst.cNotifyRemoveCallback, IntPtr.Zero, 0); }
            finally {
                _target = null;
                // 제거 호출이 끝나면 DLL 콜백 스레드가 종료되므로 이제 대리자를 놓아도 된다
                _callback = null;
            }
        }

        public void SetReturnMode(int mode) => Instantiate(WlmConst.cInstReturnMode, mode, IntPtr.Zero, 0);

        int IWlmBackend.GetWLMVersion(int detail) => GetWLMVersion(detail);
        double IWlmBackend.GetWavelengthNum(int channel) => GetWavelengthNum(channel, 0);
        double IWlmBackend.GetFrequencyNum(int channel) => GetFrequencyNum(channel, 0);
        double IWlmBackend.ConvertUnit(double value, int from, int to) => ConvertUnit(value, from, to);
        double IWlmBackend.GetTemperature() => GetTemperature(0);
        double IWlmBackend.GetPressure() => GetPressure(0);
        bool IWlmBackend.GetLinkState() => GetLinkState(false);
        double IWlmBackend.GetPowerNum(int channel) => GetPowerNum(channel, 0);
        int IWlmBackend.GetOperationState() => (short)GetOperationState(0);
        int IWlmBackend.Operation(int op) => Operation((ushort)op);
        int IWlmBackend.GetResultMode() => (short)GetResultMode(0);
        int IWlmBackend.SetResultMode(int mode) => SetResultMode((ushort)mode);
        int IWlmBackend.GetRange() => (short)GetRange(0);
        int IWlmBackend.SetRange(int range) => SetRange((ushort)range);
        int IWlmBackend.GetPulseMode() => (short)GetPulseMode(0);
        int IWlmBackend.SetPulseMode(int mode) => SetPulseMode((ushort)mode);
        int IWlmBackend.GetWideMode() => (short)GetWideMode(0);
        int IWlmBackend.SetWideMode(int mode) => SetWideMode((ushort)mode);
        bool IWlmBackend.GetFastMode() => GetFastMode(false);
        int IWlmBackend.SetFastMode(bool fast) => SetFastMode(fast);
        int IWlmBackend.GetExposureNum(int channel, int array) => GetExposureNum(channel, array, 0);
        int IWlmBackend.SetExposureNum(int channel, int array, int ms) => SetExposureNum(channel, array, ms);
        bool IWlmBackend.GetExposureModeNum(int channel) => GetExposureModeNum(channel, false) == 1;
        int IWlmBackend.SetExposureModeNum(int channel, bool auto) => SetExposureModeNum(channel, auto);
        int IWlmBackend.GetExposureRange(int which) => GetExposureRange(which);
        int IWlmBackend.GetInterval() => GetInterval(0);
        int IWlmBackend.SetInterval(int ms) => SetInterval(ms);
        bool IWlmBackend.GetIntervalMode() => GetIntervalMode(false);
        int IWlmBackend.SetIntervalMode(bool on) => SetIntervalMode(on);
        int IWlmBackend.GetAutoCalMode() => GetAutoCalMode(0);
        int IWlmBackend.SetAutoCalMode(int on) => SetAutoCalMode(on);

        int IWlmBackend.GetAutoCalSetting(int what, out int value) {
            int v = 0, r2 = 0;
            int rc = GetAutoCalSetting(what, ref v, 0, ref r2);
            value = v;
            return rc;
        }

        int IWlmBackend.SetAutoCalSetting(int what, int value) => SetAutoCalSetting(what, value, 0, 0);
        int IWlmBackend.GetAveragingSettingNum(int channel, int what) => GetAveragingSettingNum(channel, what, 0);
        int IWlmBackend.SetAveragingSettingNum(int channel, int what, int value) => SetAveragingSettingNum(channel, what, value);
        int IWlmBackend.SetPattern(int index, bool enable) => SetPattern(index, enable ? WlmConst.cPatternEnable : WlmConst.cPatternDisable);
        int IWlmBackend.GetPatternItemCount(int index) => GetPatternItemCount(index);
        int IWlmBackend.GetPatternItemSize(int index) => GetPatternItemSize(index);

        int IWlmBackend.GetPatternData(int channel, int index, double[] target) {
            int total = GetPatternItemCount(index);
            int size = GetPatternItemSize(index);
            if (total <= 0 || (size != 2 && size != 4 && size != 8)) return 0;
            // DLL은 항상 전체 배열(total × size)을 복사하므로 버퍼는 전체 크기로 잡는다
            byte[] raw = new byte[total * size];
            int count = Math.Min(total, target.Length);
            GCHandle h = GCHandle.Alloc(raw, GCHandleType.Pinned);
            try {
                if (GetPatternDataNum(channel, index, h.AddrOfPinnedObject()) <= 0) return 0;
            }
            finally { h.Free(); }
            for (int i = 0; i < count; i++) {
                target[i] = size switch {
                    2 => BitConverter.ToInt16(raw, i * 2),
                    4 => BitConverter.ToInt32(raw, i * 4),
                    _ => BitConverter.ToDouble(raw, i * 8),
                };
            }
            return count;
        }

        public void Dispose() {
            try { RemoveCallback(); }
            catch {
                // DLL이 없거나 서버가 이미 종료된 경우
            }
        }
    }
}
