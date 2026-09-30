using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DLC_PRO.Core;

/// <summary>
/// Application-wide, non-configurable protection for device limits and factory/calibration data.
/// Only reviewed operation writes/commands are allowed. New parameters are read-only by default.
/// This is an application boundary, not a replacement for the controller's own access control.
/// </summary>
public static class HardwareAccessPolicy
{
    private const string Symbol = "[A-Za-z][A-Za-z0-9:_-]*";
    private static readonly Regex NamePattern = new(@"\A" + Symbol + @"\z", RegexOptions.CultureInvariant);
    private static readonly Regex ReadPattern = new(@"\A\s*\((param-ref|param-disp)\s+'(" + Symbol + @")\s*\)\s*\z", RegexOptions.CultureInvariant);
    private static readonly Regex DiagnosticPattern = new(@"\A\s*\(exec\s+'(" + Symbol + @")\s*\)\s*\z", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> Diagnostics = new(StringComparer.Ordinal) {
        P.CmdMsgShowNew, P.CmdMsgShowAll, P.CmdMsgShowLog, P.CmdMsgShowPersistent
    };
    private static readonly HashSet<string> Writes = BuildWrites();
    private static readonly HashSet<string> Commands = new(StringComparer.Ordinal) {
        P.CmdLockFindCandidates, P.CmdLockSelectLockpoint, P.CmdLockClose, P.CmdLockOpen,
        P.CmdLockinAutoLirStart, P.CmdLockinAutoLirAbort, P.CmdWsStart, P.CmdWsStop,
        P.CmdRecGetData, P.CmdRecClearData, P.CmdMsgMarkAsRead,
        P.CmdMsgShowNew, P.CmdMsgShowAll, P.CmdMsgShowLog, P.CmdMsgShowPersistent
    };

    private static HashSet<string> BuildWrites() {
        var names = new HashSet<string>(StringComparer.Ordinal) {
            P.LaserLabel,
            P.DlCcCurrentSet, P.DlCcEnabled, P.DlTcEnabled, P.DlTcTempSet, P.DlPcEnabled, P.DlPcVoltageSet,
            P.AmpCcCurrentSet, P.AmpCcEnabled, P.AmpTcEnabled, P.AmpTcTempSet,
            P.ScanEnabled, P.ScanHold, P.ScanSignalType, P.ScanFrequency, P.ScanOutputChannel,
            P.ScanAmplitude, P.ScanOffset, P.ScanStart, P.ScanEnd,
            P.ScopeVariant, P.ScopeCh1Signal, P.ScopeCh2Signal, P.ScopeXSignal, P.ScopeUpdateRate,
            P.ScopeXTimescale, P.ScopeXSpectrumRange,
            P.LockType, P.LockSpectrumInput, P.LockPidSelection, P.LockWithoutLockpoint,
            P.LockLockingDelay, P.LockSetpoint, P.LockHold,
            P.LockFilterBottom, P.LockFilterEdgeLevel, P.LockFilterNegEdge, P.LockFilterNoiseTol,
            P.LockFilterPosEdge, P.LockFilterTop,
            P.LockinAmplitude, P.LockinFrequency, P.LockinLockLevel, P.LockinModEnabled,
            P.LockinModOutput, P.LockinPhase,
            P.RelockAmplitude, P.RelockDelay, P.RelockEnabled, P.RelockFrequency, P.RelockOutput,
            P.ResetEnabled, P.WindowEnabled, P.WindowHigh, P.WindowHysteresis, P.WindowInput, P.WindowLow,
            P.PsEnabled, P.PsGainAll, P.PsGainD, P.PsGainI, P.PsGainP, P.PsHoldOnUnlock,
            P.PsInput, P.PsSetpoint, P.PsSign, P.PsWindowEnabled, P.PsWindowHyst, P.PsWindowLow,
            P.PdExtInput,
            P.WsBegin, P.WsContinuous, P.WsDuration, P.WsEnd, P.WsOutput, P.WsRecorderStepsizeSet,
            P.WsRestoreOnEnd, P.WsShape, P.WsSpeed, P.WsValueSet,
            P.RecCh1LpCutoff, P.RecCh1LpEnabled, P.RecCh1Signal, P.RecCh2LpCutoff,
            P.RecCh2LpEnabled, P.RecCh2Signal, P.RecChxSignal, P.RecEnabled,
            P.RecRecordingMode, P.RecRecordingTime, P.RecSampleCountSet, P.RecTriggerMode
        };
        foreach (int n in new[] { 1, 2 })
            foreach (string field in new[] { P.PidEnabled, P.PidOutputChannel, P.PidGainAll, P.PidGainP,
                P.PidGainI, P.PidGainD, P.PidICutoffEnabled, P.PidICutoff, P.PidSign, P.PidSlope,
                P.PidOutLimitEnabled, P.PidOutLimitMax, P.PidHold })
                names.Add(P.Pid(n, field));
        return names;
    }

    public static void ValidateName(string name) {
        if (string.IsNullOrEmpty(name) || name.Length > 512 || !NamePattern.IsMatch(name))
            throw new UnauthorizedAccessException("허용되지 않는 파라미터/명령 이름입니다.");
    }

    private static string Canonical(string name) {
        ValidateName(name);
        return name.StartsWith("laser2:", StringComparison.Ordinal) ? "laser1:" + name[7..] : name;
    }

    public static void ValidateWrite(string name, object value) {
        if (!Writes.Contains(Canonical(name)))
            throw new UnauthorizedAccessException("읽기 전용 보호: " + name + " 값은 이 프로그램에서 변경할 수 없습니다.");
        ValidateValue(value);
    }

    private static void ValidateValue(object value) {
        // RawValue and arbitrary objects could embed executable Scheme expressions.
        if (value is not (bool or string or double or float or decimal or int or long or short or byte))
            throw new UnauthorizedAccessException("원시 표현식은 장비에 전달할 수 없습니다.");
    }

    public static void ValidateUserLevel(int level) {
        if (level != 3 && level != 4)
            throw new UnauthorizedAccessException("Maintenance/Service 권한 상승은 잠겨 있습니다. Normal(3) 또는 Read-only(4)만 사용할 수 있습니다.");
    }

    public static void ValidateExec(string name, object[]? args) {
        string canonical = Canonical(name);
        if (canonical == P.CmdChangeUl) {
            if (args is not { Length: 2 } || args[0] is not int level || args[1] is not string)
                throw new UnauthorizedAccessException("허용되지 않는 권한 변경 요청입니다.");
            ValidateUserLevel(level);
        }
        else if (!Commands.Contains(canonical))
            throw new UnauthorizedAccessException("보호된 명령 또는 허용되지 않은 명령입니다: " + name);
        if (args != null) foreach (object value in args) ValidateValue(value);
    }

    public static void ValidateConsole(string command) {
        if (command != null && command.Length <= 8192) {
            if (ReadPattern.IsMatch(command)) return;
            var diagnostic = DiagnosticPattern.Match(command);
            if (diagnostic.Success && Diagnostics.Contains(diagnostic.Groups[1].Value)) return;
        }
        throw new UnauthorizedAccessException("Console은 읽기 전용입니다. 단일 param-ref / param-disp 또는 시스템 메시지 조회만 허용됩니다. 쓰기·권한 변경·복합 명령은 차단됩니다.");
    }
}
