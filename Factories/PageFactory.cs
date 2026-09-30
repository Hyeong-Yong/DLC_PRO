using System;
using System.Collections.Generic;
using DLC_PRO.Services;
using DLC_PRO.ViewModels.Pages;

namespace DLC_PRO.Factories {
    /// <summary>페이지 ViewModel 생성기 (DI 컨테이너의 Func&lt;Type, PageViewModel&gt; 사용, BatchProcess3와 동일).</summary>
    public class PageFactory {
        private readonly Func<Type, PageViewModel> _factory;

        public PageFactory(Func<Type, PageViewModel> factory) {
            _factory = factory;
        }

        public static PageFactory ForLaser(DeviceService dev, DialogService dialogs, LogService log) {
            var pages = new Dictionary<Type, PageViewModel>();
            PageViewModel[] all = {
                new LaserPageViewModel(dev, dialogs, log), new ScanLockPageViewModel(dev, dialogs, log),
                new RelockPageViewModel(dev, log, dialogs), new StabilizationPageViewModel(dev),
                new WideScanPageViewModel(dev, dialogs, log), new RecorderPageViewModel(dev, dialogs, log),
                new SystemPageViewModel(dev, log), new ConsolePageViewModel(dev), new SettingsPageViewModel(dev, log)
            };
            foreach (var page in all) pages[page.GetType()] = page;
            return new PageFactory(type => pages[type]);
        }

        public PageViewModel GetPageViewModel<T>(Action<T>? afterCreation = null) where T : PageViewModel {
            PageViewModel viewModel = _factory(typeof(T));
            afterCreation?.Invoke((T)viewModel);
            return viewModel;
        }
    }
}
