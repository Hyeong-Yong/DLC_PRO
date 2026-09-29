using System.Collections.Generic;
using DLC_PRO.Core;
using DLC_PRO.Models;
using DLC_PRO.Services;
using DLC_PRO.ViewModels.Params;

namespace DLC_PRO.ViewModels {
    /// <summary>락 모듈 PID1/PID2 설정 묶음 (Scan &amp; Lock 페이지의 PID 탭).</summary>
    public sealed class PidViewModel {
        public PidViewModel(DeviceService dev, int n) {
            Number = n;
            Title = "PID " + n;
            Enable = new ToggleParamViewModel(dev, P.Pid(n, P.PidEnabled));
            OutputChannel = ChoiceParamViewModel.Channels(dev, P.Pid(n, P.PidOutputChannel), "Output Channel", SignalChannels.Outputs);
            GainAll = new NumberParamViewModel(dev, P.Pid(n, P.PidGainAll), "Gain (all)", null, 4, 0.1);
            GainP = new NumberParamViewModel(dev, P.Pid(n, P.PidGainP), "P", "out/in", 4, 0.01);
            GainI = new NumberParamViewModel(dev, P.Pid(n, P.PidGainI), "I", "out/(in·ms)", 4, 0.01);
            GainD = new NumberParamViewModel(dev, P.Pid(n, P.PidGainD), "D", "out·µs/in", 4, 0.01);
            ICutoffEnabled = new CheckParamViewModel(dev, P.Pid(n, P.PidICutoffEnabled), "I-cutoff 사용");
            ICutoff = new NumberParamViewModel(dev, P.Pid(n, P.PidICutoff), "I-cutoff", "Hz", 2, 1);
            Sign = new CheckParamViewModel(dev, P.Pid(n, P.PidSign), "Sign (+)");
            Slope = new CheckParamViewModel(dev, P.Pid(n, P.PidSlope), "Slope (+)");
            OutLimitEnabled = new CheckParamViewModel(dev, P.Pid(n, P.PidOutLimitEnabled), "Output limit 사용");
            OutLimitMax = new NumberParamViewModel(dev, P.Pid(n, P.PidOutLimitMax), "Output limit (±)", null, 3, 0.1);
            Hold = new CheckParamViewModel(dev, P.Pid(n, P.PidHold), "Hold");
            InLock = LedRowViewModel.FromBool(dev, "In-Lock", P.Pid(n, P.PidLockState), LedState.On);
            OnHold = LedRowViewModel.FromBool(dev, "On Hold", P.Pid(n, P.PidHoldState), LedState.Warn);
            Regulating = LedRowViewModel.FromBool(dev, "Regulating", P.Pid(n, P.PidRegulatingState), LedState.On);
        }

        public int Number { get; }
        public string Title { get; }
        public ToggleParamViewModel Enable { get; }
        public ChoiceParamViewModel OutputChannel { get; }
        public NumberParamViewModel GainAll { get; }
        public NumberParamViewModel GainP { get; }
        public NumberParamViewModel GainI { get; }
        public NumberParamViewModel GainD { get; }
        public CheckParamViewModel ICutoffEnabled { get; }
        public NumberParamViewModel ICutoff { get; }
        public CheckParamViewModel Sign { get; }
        public CheckParamViewModel Slope { get; }
        public CheckParamViewModel OutLimitEnabled { get; }
        public NumberParamViewModel OutLimitMax { get; }
        public CheckParamViewModel Hold { get; }
        public LedRowViewModel InLock { get; }
        public LedRowViewModel OnHold { get; }
        public LedRowViewModel Regulating { get; }

        /// <summary>페이지가 Track으로 등록할 행 목록.</summary>
        public IEnumerable<ParamRowViewModel> Rows => new ParamRowViewModel[] {
            Enable, OutputChannel, GainAll, GainP, GainI, GainD, ICutoffEnabled, ICutoff, Sign, Slope,
            OutLimitEnabled, OutLimitMax, Hold, InLock, OnHold, Regulating,
        };
    }
}
