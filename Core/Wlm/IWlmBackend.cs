using System;

namespace DLC_PRO.Core.Wlm {
    /// <summary>
    /// wlmData.dll 호출 추상화. 실제 구현(<see cref="WlmNativeBackend"/>)과 시뮬레이터(<see cref="WlmSimulatedBackend"/>)가 있다.
    /// 모든 메서드는 WavemeterService의 작업 스레드 하나에서만 호출된다 (콜백은 DLL 스레드).
    /// 반환값 규칙은 매뉴얼 4.1과 같다 (음수 = 오류 값).
    /// </summary>
    public interface IWlmBackend : IDisposable {
        /// <summary>표시용 이름 ("wlmData.dll" / "시뮬레이터").</summary>
        string Name { get; }

        /// <summary>Instantiate(cInstCheckForWLM) &gt; 0 — WLM 서버 프로그램이 실행 중인지.</summary>
        bool IsServerRunning();

        /// <summary>ControlWLM(cCtrlWLMShow) — 서버 프로그램 실행/앞으로 표시.</summary>
        int StartServer();

        /// <summary>
        /// CallbackProcEx 설치. 콜백은 DLL이 만든 스레드에서 (Ver, Mode, IntVal, DblVal, Res1)로 호출된다.
        /// 지원하지 않으면 false (서비스가 폴링으로 전환).
        /// </summary>
        bool InstallCallback(Action<int, int, int, double, int> callback);
        void RemoveCallback();

        /// <summary>Instantiate(cInstReturnMode, mode): 1이면 같은 값을 한 번만 반환 (폴링 모드용).</summary>
        void SetReturnMode(int mode);

        int GetWLMVersion(int detail);
        double GetWavelengthNum(int channel);
        double GetFrequencyNum(int channel);
        double ConvertUnit(double value, int from, int to);
        double GetTemperature();
        double GetPressure();
        bool GetLinkState();
        double GetPowerNum(int channel);

        int GetOperationState();
        int Operation(int op);

        int GetResultMode();
        int SetResultMode(int mode);
        int GetRange();
        int SetRange(int range);
        int GetPulseMode();
        int SetPulseMode(int mode);
        int GetWideMode();
        int SetWideMode(int mode);
        bool GetFastMode();
        int SetFastMode(bool fast);

        int GetExposureNum(int channel, int array);
        int SetExposureNum(int channel, int array, int ms);
        bool GetExposureModeNum(int channel);
        int SetExposureModeNum(int channel, bool auto);
        int GetExposureRange(int which);

        int GetInterval();
        int SetInterval(int ms);
        bool GetIntervalMode();
        int SetIntervalMode(bool on);

        int GetAutoCalMode();
        int SetAutoCalMode(int on);
        int GetAutoCalSetting(int what, out int value);
        int SetAutoCalSetting(int what, int value);

        int GetAveragingSettingNum(int channel, int what);
        int SetAveragingSettingNum(int channel, int what, int value);

        /// <summary>간섭 패턴 내보내기 사용/해제 (SetPattern).</summary>
        int SetPattern(int index, bool enable);
        int GetPatternItemCount(int index);
        int GetPatternItemSize(int index);

        /// <summary>간섭 패턴 복사 (GetPatternDataNum). 복사한 항목 수, 실패 시 0 이하.</summary>
        int GetPatternData(int channel, int index, double[] target);
    }
}
