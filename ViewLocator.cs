using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using DLC_PRO.ViewModels.Dialogs;
using DLC_PRO.ViewModels.Pages;

namespace DLC_PRO {
    /// <summary>
    /// ViewModel → View 자동 연결 (BatchProcess3와 동일한 규칙).
    /// DLC_PRO.ViewModels.Pages.LaserPageViewModel → DLC_PRO.Views.Pages.LaserPageView
    /// </summary>
    [RequiresUnreferencedCode(
        "Default implementation of ViewLocator involves reflection which may be trimmed away.",
        Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
    public class ViewLocator : IDataTemplate {
        public Control? Build(object? data) {
            if (data is null) return null;
            string viewName = data.GetType().FullName!.Replace("ViewModel", "View", StringComparison.InvariantCulture);
            Type? type = Type.GetType(viewName);
            if (type is null) return new TextBlock { Text = "View not found: " + viewName };
            Control control = (Control)Activator.CreateInstance(type)!;
            control.DataContext = data;
            return control;
        }

        public bool Match(object? data) => data is PageViewModel or DialogViewModel;
    }
}
