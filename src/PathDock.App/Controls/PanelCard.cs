using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace PathDock.App.Controls;

/// <summary>
/// 둥근 카드형 패널: 아이콘 + 제목 머리글(오른쪽에 보조 콘텐츠) 아래에 본문을 담는다.
/// 화면의 모든 영역(드라이브, 폴더 열, 파일 목록, 터미널)이 같은 시각 언어를 쓰도록 하는 공용 컨테이너이다.
/// Owner: 각 View(XAML). Lifetime: 뷰와 동일. 스타일은 Themes/Generic.xaml.
/// </summary>
public class PanelCard : HeaderedContentControl
{
    public static readonly DependencyProperty SymbolProperty = DependencyProperty.Register(
        nameof(Symbol), typeof(SymbolRegular), typeof(PanelCard), new PropertyMetadata(SymbolRegular.Empty));

    public static readonly DependencyProperty HeaderTrailingProperty = DependencyProperty.Register(
        nameof(HeaderTrailing), typeof(object), typeof(PanelCard), new PropertyMetadata(null));

    public static readonly DependencyProperty ContentPaddingProperty = DependencyProperty.Register(
        nameof(ContentPadding), typeof(Thickness), typeof(PanelCard), new PropertyMetadata(new Thickness(0)));

    static PanelCard()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(PanelCard), new FrameworkPropertyMetadata(typeof(PanelCard)));
    }

    /// <summary>머리글 왼쪽에 표시할 아이콘(없으면 생략).</summary>
    public SymbolRegular Symbol
    {
        get => (SymbolRegular)GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
    }

    /// <summary>머리글 오른쪽 보조 콘텐츠(예: 개수 배지, 작은 버튼).</summary>
    public object? HeaderTrailing
    {
        get => GetValue(HeaderTrailingProperty);
        set => SetValue(HeaderTrailingProperty, value);
    }

    /// <summary>본문 안쪽 여백(기본 0 - 목록처럼 가장자리까지 채우는 콘텐츠용).</summary>
    public Thickness ContentPadding
    {
        get => (Thickness)GetValue(ContentPaddingProperty);
        set => SetValue(ContentPaddingProperty, value);
    }
}
