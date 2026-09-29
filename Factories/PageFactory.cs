using System;
using DLC_PRO.ViewModels.Pages;

namespace DLC_PRO.Factories {
    /// <summary>페이지 ViewModel 생성기 (DI 컨테이너의 Func&lt;Type, PageViewModel&gt; 사용, BatchProcess3와 동일).</summary>
    public class PageFactory {
        private readonly Func<Type, PageViewModel> _factory;

        public PageFactory(Func<Type, PageViewModel> factory) {
            _factory = factory;
        }

        public PageViewModel GetPageViewModel<T>(Action<T>? afterCreation = null) where T : PageViewModel {
            PageViewModel viewModel = _factory(typeof(T));
            afterCreation?.Invoke((T)viewModel);
            return viewModel;
        }
    }
}
