using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Metadata;
using DLC_PRO.Bootstrap;
using DLC_PRO.Services;
using DLC_PRO.ViewModels;
using DLC_PRO.Views;
using Microsoft.Extensions.DependencyInjection;

[assembly: XmlnsDefinition("https://github.com/avaloniaui", "DLC_PRO.Controls")]

namespace DLC_PRO {
    public partial class App : Application {
        private ServiceProvider? _services;

        public override void Initialize() {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted() {
            ServiceCollection collection = new ServiceCollection();
            Bootstrapper.RegisterCommonServices(collection);
            _services = collection.BuildServiceProvider();

            MainViewModel main = _services.GetRequiredService<MainViewModel>();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
                desktop.MainWindow = new MainWindow { DataContext = main };
                // ShutdownRequested는 창 닫기 확인보다 먼저 발생하므로 여기서 정리하면 안 된다 (증폭기 ON 종료 확인이 무력화됨)
                desktop.Exit += (_, _) => DisposeServices();
                HandleArgs(main, desktop.Args ?? Array.Empty<string>());
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView) {
                singleView.MainView = new MainView { DataContext = main };
            }

            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>명령줄: --connect &lt;IP&gt; (시작 시 자동 연결), --page &lt;페이지 이름&gt;</summary>
        private static void HandleArgs(MainViewModel main, string[] args) {
            string? host = null, page = null;
            for (int i = 0; i + 1 < args.Length; i++) {
                if (args[i] == "--connect") host = args[i + 1];
                if (args[i] == "--page" || args[i] == "--tab") page = args[i + 1];
            }
            if (page != null) main.NavigateByName(page);
            if (host != null) Avalonia.Threading.Dispatcher.UIThread.Post(async () => await main.AutoConnectAsync(host));
        }

        private void DisposeServices() {
            ServiceProvider? sp = _services;
            _services = null;
            if (sp == null) return;
            try {
                sp.GetService<DeviceService>()?.Dispose();
                sp.GetService<LogService>()?.Dispose();
            }
            catch {
                // 종료 중 오류 무시
            }
        }
    }
}
