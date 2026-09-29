using Avalonia;
using Avalonia.Controls.Primitives;

namespace DLC_PRO.Controls {
    /// <summary>
    /// 제목 + (오른쪽 헤더 영역) + 내용으로 구성된 카드 패널.
    /// WinForms 버전의 ParamGroup(GroupBox)에 해당. 테마는 Controls/Card.axaml.
    /// 내용 영역은 Grid.IsSharedSizeScope 이므로 행(라벨|값|단위)의 열 폭이 카드 안에서 맞춰진다.
    /// </summary>
    public class Card : HeaderedContentControl {
        public static readonly StyledProperty<object?> HeaderRightProperty =
            AvaloniaProperty.Register<Card, object?>(nameof(HeaderRight));

        public static readonly StyledProperty<string?> IconTextProperty =
            AvaloniaProperty.Register<Card, string?>(nameof(IconText));

        /// <summary>제목 오른쪽에 놓을 컨트롤 (Enable 버튼, LED 등).</summary>
        public object? HeaderRight {
            get => GetValue(HeaderRightProperty);
            set => SetValue(HeaderRightProperty, value);
        }

        /// <summary>제목 앞 Phosphor 아이콘 문자 (선택).</summary>
        public string? IconText {
            get => GetValue(IconTextProperty);
            set => SetValue(IconTextProperty, value);
        }
    }
}
