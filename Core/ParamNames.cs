#nullable disable
namespace DLC_PRO.Core
{
    /// <summary>
    /// DLC pro 파라미터/명령 이름 (DLCpro Command Reference, Firmware 3.5.1 기준).
    /// laser1 기준. 두 번째 레이저를 쓰려면 Laser 접두사를 "laser2"로 바꾸면 된다.
    /// </summary>
    public static class P
    {
        // ---------- 시스템 ----------
        public const string Emission = "emission";
        public const string InterlockOpen = "interlock-open";
        public const string FrontkeyLocked = "frontkey-locked";
        public const string SystemHealthTxt = "system-health-txt";
        public const string SerialNumber = "serial-number";
        public const string FwVer = "fw-ver";
        public const string SswVer = "ssw-ver";
        public const string SystemType = "system-type";
        public const string SystemModel = "system-model";
        public const string SystemLabel = "system-label";
        public const string UptimeTxt = "uptime-txt";
        public const string UserLevel = "ul";
        public const string CmdChangeUl = "change-ul";
        public const string MsgCountNew = "system-messages:count-new";
        public const string MsgLatest = "system-messages:latest-message";
        public const string CmdMsgShowAll = "system-messages:show-all";
        public const string CmdMsgShowNew = "system-messages:show-new";
        public const string CmdMsgShowLog = "system-messages:show-log";
        public const string CmdMsgShowPersistent = "system-messages:show-persistent";
        public const string CmdMsgMarkAsRead = "system-messages:mark-as-read";
        public const string NetIp = "net-conf:ip-addr";

        // ---------- 레이저 공통 ----------
        public const string LaserType = "laser1:type";
        public const string LaserProductName = "laser1:product-name";
        public const string LaserLabel = "laser1:label";
        public const string LaserEmission = "laser1:emission";
        public const string LaserHealthTxt = "laser1:health-txt";

        // ---------- 마스터 레이저 (DL) ----------
        public const string DlSerial = "laser1:dl:serial-number";
        public const string DlCcEnabled = "laser1:dl:cc:enabled";
        public const string DlCcEmission = "laser1:dl:cc:emission";
        public const string DlCcCurrentSet = "laser1:dl:cc:current-set";
        public const string DlCcCurrentAct = "laser1:dl:cc:current-act";
        public const string DlCcCurrentClip = "laser1:dl:cc:current-clip";
        public const string DlCcVoltageAct = "laser1:dl:cc:voltage-act";
        public const string DlCcStatusTxt = "laser1:dl:cc:status-txt";
        public const string DlCcFeedforwardEnabled = "laser1:dl:cc:feedforward-enabled";
        public const string DlCcFeedforwardFactor = "laser1:dl:cc:feedforward-factor";

        public const string DlTcEnabled = "laser1:dl:tc:enabled";
        public const string DlTcTempSet = "laser1:dl:tc:temp-set";
        public const string DlTcTempAct = "laser1:dl:tc:temp-act";
        public const string DlTcReady = "laser1:dl:tc:ready";
        public const string DlTcStatusTxt = "laser1:dl:tc:status-txt";
        public const string DlTcTempSetMin = "laser1:dl:tc:temp-set-min";
        public const string DlTcTempSetMax = "laser1:dl:tc:temp-set-max";

        public const string DlPcEnabled = "laser1:dl:pc:enabled";
        public const string DlPcVoltageSet = "laser1:dl:pc:voltage-set";
        public const string DlPcVoltageAct = "laser1:dl:pc:voltage-act";
        public const string DlPcVoltageMin = "laser1:dl:pc:voltage-min";
        public const string DlPcVoltageMax = "laser1:dl:pc:voltage-max";
        public const string DlPcStatusTxt = "laser1:dl:pc:status-txt";

        // ---------- 증폭기 (TA) ----------
        public const string AmpSerial = "laser1:amp:serial-number";
        public const string AmpCcEnabled = "laser1:amp:cc:enabled";
        public const string AmpCcEmission = "laser1:amp:cc:emission";
        public const string AmpCcCurrentSet = "laser1:amp:cc:current-set";
        public const string AmpCcCurrentAct = "laser1:amp:cc:current-act";
        public const string AmpCcCurrentClip = "laser1:amp:cc:current-clip";
        public const string AmpCcStatusTxt = "laser1:amp:cc:status-txt";
        public const string AmpTcEnabled = "laser1:amp:tc:enabled";
        public const string AmpTcTempSet = "laser1:amp:tc:temp-set";
        public const string AmpTcTempAct = "laser1:amp:tc:temp-act";
        public const string AmpTcReady = "laser1:amp:tc:ready";
        public const string AmpSeedPower = "laser1:amp:pd:seed:power";
        public const string AmpOutputPower = "laser1:amp:pd:amp:power";
        public const string AmpSeedPowerMin = "laser1:amp:seed-limits:power-min";
        public const string AmpSeedLimitsStatusTxt = "laser1:amp:seed-limits:status-txt";
        public const string AmpOutputPowerMax = "laser1:amp:output-limits:power-max";
        public const string AmpOutputLimitsStatusTxt = "laser1:amp:output-limits:status-txt";

        // ---------- 스캔 ----------
        public const string ScanEnabled = "laser1:scan:enabled";
        public const string ScanHold = "laser1:scan:hold";
        public const string ScanSignalType = "laser1:scan:signal-type";
        public const string ScanFrequency = "laser1:scan:frequency";
        public const string ScanOutputChannel = "laser1:scan:output-channel";
        public const string ScanUnit = "laser1:scan:unit";
        public const string ScanAmplitude = "laser1:scan:amplitude";
        public const string ScanOffset = "laser1:scan:offset";
        public const string ScanStart = "laser1:scan:start";
        public const string ScanEnd = "laser1:scan:end";

        // ---------- 스코프 ----------
        public const string ScopeVariant = "laser1:scope:variant";
        public const string ScopeUpdateRate = "laser1:scope:update-rate";
        public const string ScopeCh1Signal = "laser1:scope:channel1:signal";
        public const string ScopeCh1Unit = "laser1:scope:channel1:unit";
        public const string ScopeCh1Name = "laser1:scope:channel1:name";
        public const string ScopeCh2Signal = "laser1:scope:channel2:signal";
        public const string ScopeCh2Unit = "laser1:scope:channel2:unit";
        public const string ScopeCh2Name = "laser1:scope:channel2:name";
        public const string ScopeXSignal = "laser1:scope:channelx:xy-signal";
        public const string ScopeXTimescale = "laser1:scope:channelx:scope-timescale";
        public const string ScopeXSpectrumRange = "laser1:scope:channelx:spectrum-range";
        public const string ScopeXUnit = "laser1:scope:channelx:unit";
        public const string ScopeXName = "laser1:scope:channelx:name";
        public const string ScopeData = "laser1:scope:data";

        // ---------- 락 ----------
        public const string LockType = "laser1:dl:lock:type";
        public const string LockWithoutLockpoint = "laser1:dl:lock:lock-without-lockpoint";
        public const string LockState = "laser1:dl:lock:state";
        public const string LockStateTxt = "laser1:dl:lock:state-txt";
        public const string LockEnabled = "laser1:dl:lock:lock-enabled";
        public const string LockHold = "laser1:dl:lock:hold";
        public const string LockSpectrumInput = "laser1:dl:lock:spectrum-input-channel";
        public const string LockErrorChannel = "laser1:dl:lock:error-channel";
        public const string LockPidSelection = "laser1:dl:lock:pid-selection";
        public const string LockSetpoint = "laser1:dl:lock:setpoint";
        public const string LockLockingDelay = "laser1:dl:lock:locking-delay";
        public const string LockLockpointPosition = "laser1:dl:lock:lockpoint:position";
        public const string LockLockpointType = "laser1:dl:lock:lockpoint:type";
        public const string LockCandidates = "laser1:dl:lock:candidates";
        public const string LockBackgroundTrace = "laser1:dl:lock:background-trace";
        public const string LockFilterTop = "laser1:dl:lock:candidate-filter:top";
        public const string LockFilterBottom = "laser1:dl:lock:candidate-filter:bottom";
        public const string LockFilterPosEdge = "laser1:dl:lock:candidate-filter:positive-edge";
        public const string LockFilterNegEdge = "laser1:dl:lock:candidate-filter:negative-edge";
        public const string LockFilterEdgeLevel = "laser1:dl:lock:candidate-filter:edge-level";
        public const string LockFilterNoiseTol = "laser1:dl:lock:candidate-filter:peak-noise-tolerance";
        public const string CmdLockFindCandidates = "laser1:dl:lock:find-candidates";
        public const string CmdLockSelectLockpoint = "laser1:dl:lock:select-lockpoint";
        public const string CmdLockClose = "laser1:dl:lock:close";
        public const string CmdLockOpen = "laser1:dl:lock:open";

        // PID (n = 1 또는 2)
        public static string Pid(int n, string sub) { return "laser1:dl:lock:pid" + n + ":" + sub; }
        public const string PidEnabled = "enabled";
        public const string PidGainAll = "gain:all";
        public const string PidGainP = "gain:p";
        public const string PidGainI = "gain:i";
        public const string PidGainD = "gain:d";
        public const string PidICutoff = "gain:i-cutoff";
        public const string PidICutoffEnabled = "gain:i-cutoff-enabled";
        public const string PidSign = "sign";
        public const string PidSlope = "slope";
        public const string PidOutputChannel = "output-channel";
        public const string PidOutLimitEnabled = "outputlimit:enabled";
        public const string PidOutLimitMax = "outputlimit:max";
        public const string PidHold = "hold";
        public const string PidLockState = "lock-state";
        public const string PidHoldState = "hold-state";
        public const string PidRegulatingState = "regulating-state";

        // Lock-In
        public const string LockinModEnabled = "laser1:dl:lock:lockin:modulation-enabled";
        public const string LockinModOutput = "laser1:dl:lock:lockin:modulation-output-channel";
        public const string LockinFrequency = "laser1:dl:lock:lockin:frequency";
        public const string LockinAmplitude = "laser1:dl:lock:lockin:amplitude";
        public const string LockinPhase = "laser1:dl:lock:lockin:phase-shift";
        public const string LockinLockLevel = "laser1:dl:lock:lockin:lock-level";
        public const string LockinAutoLirState = "laser1:dl:lock:lockin:auto-lir:state";
        public const string LockinAutoLirProgress = "laser1:dl:lock:lockin:auto-lir:progress";
        public const string CmdLockinAutoLirStart = "laser1:dl:lock:lockin:auto-lir:start";
        public const string CmdLockinAutoLirAbort = "laser1:dl:lock:lockin:auto-lir:abort";

        // ReLock / Window / Reset
        public const string RelockEnabled = "laser1:dl:lock:relock:enabled";
        public const string RelockOutput = "laser1:dl:lock:relock:output-channel";
        public const string RelockFrequency = "laser1:dl:lock:relock:frequency";
        public const string RelockAmplitude = "laser1:dl:lock:relock:amplitude";
        public const string RelockDelay = "laser1:dl:lock:relock:delay";
        public const string ResetEnabled = "laser1:dl:lock:reset:enabled";
        public const string WindowEnabled = "laser1:dl:lock:window:enabled";
        public const string WindowInput = "laser1:dl:lock:window:input-channel";
        public const string WindowHigh = "laser1:dl:lock:window:level-high";
        public const string WindowLow = "laser1:dl:lock:window:level-low";
        public const string WindowHysteresis = "laser1:dl:lock:window:level-hysteresis";

        // ---------- 출력 안정화 ----------
        public const string PsEnabled = "laser1:power-stabilization:enabled";
        public const string PsGainAll = "laser1:power-stabilization:gain:all";
        public const string PsGainP = "laser1:power-stabilization:gain:p";
        public const string PsGainI = "laser1:power-stabilization:gain:i";
        public const string PsGainD = "laser1:power-stabilization:gain:d";
        public const string PsSign = "laser1:power-stabilization:sign";
        public const string PsInput = "laser1:power-stabilization:input-channel";
        public const string PsSetpoint = "laser1:power-stabilization:setpoint";
        public const string PsWindowEnabled = "laser1:power-stabilization:window:enabled";
        public const string PsWindowLow = "laser1:power-stabilization:window:level-low";
        public const string PsWindowHyst = "laser1:power-stabilization:window:level-hysteresis";
        public const string PsHoldOnUnlock = "laser1:power-stabilization:hold-output-on-unlock";
        public const string PsOutput = "laser1:power-stabilization:output-channel";
        public const string PsValueAct = "laser1:power-stabilization:input-channel-value-act";
        public const string PsState = "laser1:power-stabilization:state";

        public const string PdExtInput = "laser1:pd-ext:input-channel";
        public const string PdExtPhotodiode = "laser1:pd-ext:photodiode";
        public const string PdExtPower = "laser1:pd-ext:power";
        public const string PdExtCalOffset = "laser1:pd-ext:cal-offset";
        public const string PdExtCalFactor = "laser1:pd-ext:cal-factor";

        // ---------- 와이드 스캔 ----------
        public const string WsState = "laser1:wide-scan:state";
        public const string WsStateTxt = "laser1:wide-scan:state-txt";
        public const string WsOutput = "laser1:wide-scan:output-channel";
        public const string WsBegin = "laser1:wide-scan:scan-begin";
        public const string WsEnd = "laser1:wide-scan:scan-end";
        public const string WsContinuous = "laser1:wide-scan:continuous-mode";
        public const string WsRestoreOnEnd = "laser1:wide-scan:restore-on-end";
        public const string WsShape = "laser1:wide-scan:shape";
        public const string WsOffset = "laser1:wide-scan:offset";
        public const string WsAmplitude = "laser1:wide-scan:amplitude";
        public const string WsSpeed = "laser1:wide-scan:speed";
        public const string WsSpeedMin = "laser1:wide-scan:speed-min";
        public const string WsSpeedMax = "laser1:wide-scan:speed-max";
        public const string WsDuration = "laser1:wide-scan:duration";
        public const string WsValueSet = "laser1:wide-scan:value-set";
        public const string WsValueAct = "laser1:wide-scan:value-act";
        public const string WsValueUnit = "laser1:wide-scan:value-unit";
        public const string WsRecorderStepsizeSet = "laser1:wide-scan:recorder-stepsize-set";
        public const string WsRecorderSampleCount = "laser1:wide-scan:recorder-sample-count";
        public const string WsProgress = "laser1:wide-scan:progress";
        public const string WsRemaining = "laser1:wide-scan:remaining-time";
        public const string CmdWsStart = "laser1:wide-scan:start";
        public const string CmdWsStop = "laser1:wide-scan:stop";

        // ---------- 레코더 ----------
        public const string RecState = "laser1:recorder:state";
        public const string RecStateTxt = "laser1:recorder:state-txt";
        public const string RecEnabled = "laser1:recorder:enabled";
        public const string RecTriggerMode = "laser1:recorder:trigger-mode";
        public const string RecCh1Signal = "laser1:recorder:inputs:channel1:signal";
        public const string RecCh2Signal = "laser1:recorder:inputs:channel2:signal";
        public const string RecChxSignal = "laser1:recorder:inputs:channelx:signal";
        public const string RecCh1LpEnabled = "laser1:recorder:inputs:channel1:low-pass-filter:enabled";
        public const string RecCh1LpCutoff = "laser1:recorder:inputs:channel1:low-pass-filter:cut-off-frequency";
        public const string RecCh2LpEnabled = "laser1:recorder:inputs:channel2:low-pass-filter:enabled";
        public const string RecCh2LpCutoff = "laser1:recorder:inputs:channel2:low-pass-filter:cut-off-frequency";
        public const string RecRecordingMode = "laser1:recorder:recording-mode";
        public const string RecRecordingTime = "laser1:recorder:recording-time";
        public const string RecSampleCountSet = "laser1:recorder:sample-count-set";
        public const string RecSampleCount = "laser1:recorder:sample-count";
        public const string RecSamplingRate = "laser1:recorder:sampling-rate";
        public const string RecSamplingInterval = "laser1:recorder:sampling-interval";
        public const string RecDataCount = "laser1:recorder:data:recorded-sample-count";
        public const string RecDataInterval = "laser1:recorder:data:recorded-sampling-interval";
        public const string RecDataCh1Name = "laser1:recorder:data:channel1:name";
        public const string RecDataCh1Unit = "laser1:recorder:data:channel1:unit";
        public const string RecDataCh2Name = "laser1:recorder:data:channel2:name";
        public const string RecDataCh2Unit = "laser1:recorder:data:channel2:unit";
        public const string RecDataChxName = "laser1:recorder:data:channelx:name";
        public const string RecDataChxUnit = "laser1:recorder:data:channelx:unit";
        public const string CmdRecGetData = "laser1:recorder:data:get-data";
        public const string CmdRecClearData = "laser1:recorder:data:clear-data";
    }

    /// <summary>laser1:dl:lock:state 값 (Command Reference 3.1).</summary>
    public enum LockStateCode
    {
        Idle = 0, Scanning = 1, Selecting = 2, Selected = 3, Locking = 4,
        Locked = 5, OnHold = 6, Resetting = 7, Reset = 8, Relocking = 9
    }
}
