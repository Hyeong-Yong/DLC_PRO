namespace DLC_PRO.Core.Wlm {
    /// <summary>
    /// HighFinesse wlmData.dll 상수 (Projects/Headers/C/wlmData.h, 매뉴얼 4.1.4에서 필요한 것만 발췌).
    /// </summary>
    public static class WlmConst {
        // Instantiate
        public const int cInstCheckForWLM = -1;
        public const int cInstReturnMode = 0;
        public const int cInstNotification = 1;
        public const int cNotifyInstallCallback = 0;
        public const int cNotifyRemoveCallback = 1;
        public const int cNotifyInstallCallbackEx = 4;

        // ControlWLM
        public const int cCtrlWLMShow = 1;
        public const int cCtrlWLMStartSilent = 0x0020;

        // Operation / GetOperationState
        public const int cStop = 0;
        public const int cAdjustment = 1;
        public const int cMeasurement = 2;

        // Result mode (단위)
        public const int cReturnWavelengthVac = 0;
        public const int cReturnWavelengthAir = 1;
        public const int cReturnFrequency = 2;
        public const int cReturnWavenumber = 3;
        public const int cReturnPhotonEnergy = 4;

        // Range model
        public const int cRangeModelByOrder = 65534;

        // Exposure range
        public const int cExpoMin = 0;
        public const int cExpoMax = 1;

        // Autocalibration
        public const int cmiAutoCalPeriod = 1120;
        public const int cmiAutoCalUnit = 1121;
        public const int cACOnceOnStart = 0;
        public const int cACMeasurements = 1;
        public const int cACDays = 2;
        public const int cACHours = 3;
        public const int cACMinutes = 4;

        // Averaging
        public const int cmiAveragingCount = 1524;
        public const int cmiAveragingMode = 1525;
        public const int cmiAveragingType = 1526;
        public const int cAvrgFloating = 1;
        public const int cAvrgSucceeding = 2;
        public const int cAvrgSimple = 0;
        public const int cAvrgPattern = 1;

        // Pattern export
        public const int cSignal1Interferometers = 0;
        public const int cPatternDisable = 0;
        public const int cPatternEnable = 1;

        // Callback mode (cmi…)
        public const int cmiResultMode = 1;
        public const int cmiRange = 2;
        public const int cmiPulse = 3;
        public const int cmiWideLine = 4;
        public const int cmiFast = 5;
        public const int cmiExposureMode = 6;
        public const int cmiExposureValue1 = 7;
        public const int cmiTemperature = 14;
        public const int cmiLink = 15;
        public const int cmiOperation = 16;
        public const int cmiDLLDetach = 30;
        public const int cmiAutoCalMode = 37;
        public const int cmiWavelength1 = 42;
        public const int cmiDLLAttach = 121;
        public const int cmiPatternAnalysisWritten = 202;
        public const int cmiServerInitialized = 1124;
        public const int cmiPressure = 1465;
        public const int cmiInterval = 1477;
        public const int cmiIntervalMode = 1478;

        // 측정값 오류 (GetWavelength/GetFrequency 반환값)
        public const int ErrNoValue = 0;
        public const int ErrNoSignal = -1;
        public const int ErrBadSignal = -2;
        public const int ErrLowSignal = -3;
        public const int ErrBigSignal = -4;
        public const int ErrWlmMissing = -5;
        public const int ErrNotAvailable = -6;
        public const int ErrNoPulse = -8;
        public const int ErrChannelNotAvailable = -10;
        public const int ErrDiv0 = -13;
        public const int ErrOutOfRange = -14;
        public const int ErrUnitNotAvailable = -15;
        public const int ErrTemperature = -1000;

        // Set… 함수 반환값
        public const int ResERR_NoErr = 0;
    }
}
