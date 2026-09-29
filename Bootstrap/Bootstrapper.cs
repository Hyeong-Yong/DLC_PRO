using System;
using DLC_PRO.Factories;
using DLC_PRO.Interfaces;
using DLC_PRO.Services;
using DLC_PRO.ViewModels;
using DLC_PRO.ViewModels.Dialogs;
using DLC_PRO.ViewModels.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace DLC_PRO.Bootstrap {
    /// <summary>DI 등록 (BatchProcess3 Bootstrapper와 같은 구조).</summary>
    public static class Bootstrapper {
        public static void RegisterCommonServices(IServiceCollection collection) {
            // Model / 서비스 (싱글톤)
            collection.AddSingleton<LogService>();
            collection.AddSingleton<DeviceService>();
            collection.AddSingleton<DialogService>();
            collection.AddSingleton<Func<IDialogProvider>>(sp => () => sp.GetRequiredService<MainViewModel>());

            // 메인 / 페이지 ViewModel (싱글톤: 장비 구독/기록 상태를 유지)
            collection.AddSingleton<MainViewModel>();
            collection.AddSingleton<LaserPageViewModel>();
            collection.AddSingleton<ScanLockPageViewModel>();
            collection.AddSingleton<RelockPageViewModel>();
            collection.AddSingleton<StabilizationPageViewModel>();
            collection.AddSingleton<WideScanPageViewModel>();
            collection.AddSingleton<RecorderPageViewModel>();
            collection.AddSingleton<SystemPageViewModel>();
            collection.AddSingleton<ConsolePageViewModel>();
            collection.AddSingleton<SettingsPageViewModel>();

            // Page Factory Callback
            collection.AddSingleton<Func<Type, PageViewModel>>(x => type => type switch {
                _ when type == typeof(LaserPageViewModel) => x.GetRequiredService<LaserPageViewModel>(),
                _ when type == typeof(ScanLockPageViewModel) => x.GetRequiredService<ScanLockPageViewModel>(),
                _ when type == typeof(RelockPageViewModel) => x.GetRequiredService<RelockPageViewModel>(),
                _ when type == typeof(StabilizationPageViewModel) => x.GetRequiredService<StabilizationPageViewModel>(),
                _ when type == typeof(WideScanPageViewModel) => x.GetRequiredService<WideScanPageViewModel>(),
                _ when type == typeof(RecorderPageViewModel) => x.GetRequiredService<RecorderPageViewModel>(),
                _ when type == typeof(SystemPageViewModel) => x.GetRequiredService<SystemPageViewModel>(),
                _ when type == typeof(ConsolePageViewModel) => x.GetRequiredService<ConsolePageViewModel>(),
                _ when type == typeof(SettingsPageViewModel) => x.GetRequiredService<SettingsPageViewModel>(),
                _ => throw new InvalidOperationException($"Page of type {type?.FullName} has no view model"),
            });

            // Page Factory
            collection.AddSingleton<PageFactory>();

            // Transient
            collection.AddTransient<ConfirmDialogViewModel>();

            // Top Level (파일 선택 창용)
            collection.AddTopLevelProvider();
        }
    }
}
