using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using DLC_PRO.Interfaces;
using DLC_PRO.ViewModels.Dialogs;

namespace DLC_PRO.Services {
    /// <summary>
    /// 대화상자/파일 선택 서비스 (BatchProcess3 DialogService 기반).
    /// ViewModel은 이 서비스만 호출하고 View(창, MessageBox)를 직접 다루지 않는다.
    /// 대화상자는 MainViewModel(IDialogProvider)의 Dialog 속성에 올려 메인 창 위에 겹쳐 표시된다.
    /// 여러 요청이 겹치면 순서대로 하나씩 표시한다.
    /// </summary>
    public sealed class DialogService {
        private readonly Func<IDialogProvider> _host;
        private readonly Func<TopLevel?> _topLevel;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        public DialogService(Func<IDialogProvider> host, Func<TopLevel?> topLevel) {
            _host = host;
            _topLevel = topLevel;
        }

        /// <summary>현재 대화상자가 떠 있는지.</summary>
        public bool IsBusy => _gate.CurrentCount == 0;

        public async Task ShowDialog<TDialogViewModel>(TDialogViewModel dialog) where TDialogViewModel : DialogViewModel {
            await _gate.WaitAsync();
            try {
                IDialogProvider host = _host();
                host.Dialog = dialog;
                dialog.Show();
                await dialog.WaitAsync();
            }
            finally {
                _gate.Release();
            }
        }

        /// <summary>확인/취소 대화상자. 확인이면 true.</summary>
        public async Task<bool> ConfirmAsync(string title, string message, DialogKind kind = DialogKind.Question,
                                             string confirmText = "확인", string cancelText = "취소") {
            ConfirmDialogViewModel vm = new ConfirmDialogViewModel {
                Title = title,
                Message = message,
                Kind = kind,
                ConfirmText = confirmText,
                CancelText = cancelText,
            };
            await ShowDialog(vm);
            return vm.Confirmed;
        }

        /// <summary>알림 (확인 버튼만).</summary>
        public async Task AlertAsync(string title, string message, DialogKind kind = DialogKind.Info) {
            ConfirmDialogViewModel vm = new ConfirmDialogViewModel {
                Title = title,
                Message = message,
                Kind = kind,
                ShowCancelButton = false,
                ConfirmText = "확인",
            };
            await ShowDialog(vm);
        }

        /// <summary>세 가지 선택 (예/아니오/취소). 반환: true=첫 번째, false=두 번째, null=취소.</summary>
        public async Task<bool?> ChooseAsync(string title, string message, string firstText, string secondText,
                                             DialogKind kind = DialogKind.Warning) {
            ChoiceDialogViewModel vm = new ChoiceDialogViewModel {
                Title = title,
                Message = message,
                Kind = kind,
                FirstText = firstText,
                SecondText = secondText,
            };
            await ShowDialog(vm);
            return vm.Result;
        }

        /// <summary>저장 파일 선택. 취소 시 null.</summary>
        public async Task<string?> SaveFilePickerAsync(string title, string suggestedName, string typeName, string pattern) {
            TopLevel? top = _topLevel();
            if (top == null) return null;
            IStorageFile? file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
                Title = title,
                SuggestedFileName = suggestedName,
                FileTypeChoices = new[] { new FilePickerFileType(typeName) { Patterns = new[] { pattern } } },
            });
            return file?.TryGetLocalPath();
        }
    }
}
