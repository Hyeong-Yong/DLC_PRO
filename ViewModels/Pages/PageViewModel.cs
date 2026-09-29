using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using DLC_PRO.Data;
using DLC_PRO.Services;
using DLC_PRO.ViewModels.Params;

namespace DLC_PRO.ViewModels.Pages {
    /// <summary>
    /// 사이드 메뉴 페이지 ViewModel의 기반 (BatchProcess3 PageViewModel + 장비 갱신 기능).
    /// - <see cref="Track{T}"/>로 등록한 행(ParamRowViewModel)은 페이지가 보일 때 100 ms마다 갱신된다.
    /// - <see cref="OnTick"/>는 페이지가 보이지 않아도 호출된다 (락 이벤트 기록, 추세 샘플링 등).
    /// </summary>
    public abstract partial class PageViewModel : ViewModelBase {
        private readonly List<ParamRowViewModel> _rows = new List<ParamRowViewModel>();

        [ObservableProperty]
        private ApplicationPageNames _pageName;

        /// <summary>현재 화면에 표시 중인 페이지인지 (MainViewModel이 설정).</summary>
        [ObservableProperty]
        private bool _isActive;

        protected PageViewModel(ApplicationPageNames pageName, DeviceService dev) {
            _pageName = pageName;
            Dev = dev;
            dev.Tick += Tick;
        }

        protected DeviceService Dev { get; }

        /// <summary>페이지 제목 (헤더).</summary>
        public abstract string Title { get; }

        /// <summary>페이지 부제 (헤더 아래 설명).</summary>
        public virtual string Subtitle => "";

        /// <summary>행 ViewModel을 등록 (페이지가 보일 때 자동 갱신).</summary>
        protected T Track<T>(T row) where T : ParamRowViewModel {
            _rows.Add(row);
            return row;
        }

        private void Tick() {
            OnTick();
            if (!IsActive) return;
            for (int i = 0; i < _rows.Count; i++) _rows[i].Refresh();
            OnActiveTick();
        }

        /// <summary>항상 호출 (100 ms).</summary>
        protected virtual void OnTick() {
        }

        /// <summary>페이지가 보일 때만 호출 (100 ms, 행 갱신 후).</summary>
        protected virtual void OnActiveTick() {
        }

        partial void OnIsActiveChanged(bool value) {
            if (value) {
                for (int i = 0; i < _rows.Count; i++) _rows[i].Refresh();
            }
            OnActivated(value);
        }

        /// <summary>페이지 표시/숨김 시.</summary>
        protected virtual void OnActivated(bool active) {
        }
    }
}
