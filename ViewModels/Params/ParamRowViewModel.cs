using CommunityToolkit.Mvvm.ComponentModel;
using DLC_PRO.Core;
using DLC_PRO.Services;

namespace DLC_PRO.ViewModels.Params {
    /// <summary>
    /// 화면의 한 행(라벨 | 값 | 단위)을 나타내는 ViewModel의 기반.
    /// 페이지 ViewModel이 100 ms 주기로 <see cref="Refresh"/>를 호출해 장비 캐시 값을 화면 속성으로 옮긴다.
    /// 행의 모양은 Views/Params/ParamTemplates.axaml의 DataTemplate이 결정한다.
    /// </summary>
    public abstract partial class ParamRowViewModel : ViewModelBase {
        protected ParamRowViewModel(DeviceService dev, string label) {
            Dev = dev;
            Label = label;
        }

        protected DeviceService Dev { get; }
        protected DlcDevice Device => Dev.Device;

        /// <summary>행 라벨.</summary>
        public string Label { get; }

        /// <summary>입력/조작 가능 여부 (연결됨 + 장비가 이 파라미터를 지원).</summary>
        [ObservableProperty]
        private bool _isAvailable;

        /// <summary>툴팁 (장비 파라미터 이름 등).</summary>
        public string? ToolTip { get; init; }

        /// <summary>장비 캐시 → 화면 (UI 스레드, 100 ms).</summary>
        public abstract void Refresh();
    }

    /// <summary>장비 파라미터 하나에 연결된 행.</summary>
    public abstract partial class DeviceParamViewModel : ParamRowViewModel {
        protected DeviceParamViewModel(DeviceService dev, string name, string label, int periodMs) : base(dev, label) {
            Name = name;
            ToolTip = name;
            dev.Device.Watch(name, periodMs);
        }

        /// <summary>DeCoF 파라미터 이름 (예: laser1:dl:cc:current-set).</summary>
        public string Name { get; }

        protected bool ComputeAvailable() => Device.IsConnected && !Device.IsUnavailable(Name);
    }
}
