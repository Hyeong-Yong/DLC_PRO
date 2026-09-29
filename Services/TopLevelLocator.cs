using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;

namespace DLC_PRO.Services {
    /// <summary>파일 선택 창 등에 필요한 TopLevel(메인 창)을 DI로 제공 (BatchProcess3 TopLevelLocator 간소화).</summary>
    public static class TopLevelLocator {
        public static void AddTopLevelProvider(this IServiceCollection collection) {
            collection.AddSingleton<Func<TopLevel?>>(_ => () => {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    return TopLevel.GetTopLevel(desktop.MainWindow);
                if (Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime single)
                    return TopLevel.GetTopLevel(single.MainView);
                return null;
            });
        }
    }
}
