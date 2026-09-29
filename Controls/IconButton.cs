using Avalonia;
using Avalonia.Controls;

namespace DLC_PRO.Controls {
    /// <summary>아이콘(Phosphor 글꼴 문자) + 내용을 가진 버튼 (BatchProcess3 IconButton과 동일).</summary>
    public class IconButton : Button {
        public static readonly StyledProperty<string> IconTextProperty =
            AvaloniaProperty.Register<IconButton, string>(nameof(IconText));

        public string IconText {
            get => GetValue(IconTextProperty);
            set => SetValue(IconTextProperty, value);
        }
    }
}
