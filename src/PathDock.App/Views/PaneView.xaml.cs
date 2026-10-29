using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PathDock.App.ViewModels;

namespace PathDock.App.Views;

/// <summary>
/// 폴더 영역 한 칸(파란 경로 탭 + 상위 폴더 열 + 파일 목록). 눌렀거나 포커스가 들어오면 이 영역의 선택을 요청한다.
/// 보일 상위 열 개수는 <see cref="AncestorColumns"/>(0, 1, 3)로 정하고, 여러 영역이 보이는 배치에서만 선택 테두리를 그린다.
/// </summary>
public partial class PaneView : UserControl
{
    private const double NarrowColumnWidth = 140;
    private const double ColumnWidth = 170;
    private const double SplitterWidth = 6;

    public static readonly DependencyProperty AncestorColumnsProperty = DependencyProperty.Register(
        nameof(AncestorColumns), typeof(int), typeof(PaneView),
        new PropertyMetadata(0, (d, _) => ((PaneView)d).ApplyAncestorColumns()));

    public static readonly DependencyProperty ShowAddressBarProperty = DependencyProperty.Register(
        nameof(ShowAddressBar), typeof(bool), typeof(PaneView),
        new PropertyMetadata(true, (d, e) => ((PaneView)d).PathTab.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed));

    public static readonly DependencyProperty HighlightActiveProperty = DependencyProperty.Register(
        nameof(HighlightActive), typeof(bool), typeof(PaneView), new PropertyMetadata(true));

    public PaneView()
    {
        InitializeComponent();
        ApplyAncestorColumns();
        // 클릭은 안쪽 컨트롤이 처리해도(handledEventsToo) 선택으로 취급하고, 키보드로 포커스가 들어와도 마찬가지다.
        AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler((_, _) => Vm?.RequestActivation()), handledEventsToo: true);
        AddHandler(GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, _) => Vm?.RequestActivation()), handledEventsToo: true);
    }

    /// <summary>현재 폴더 왼쪽에 보일 상위 폴더 열 개수: 0(없음), 1(부모), 3(부모·조부모·증조부모).</summary>
    public int AncestorColumns
    {
        get => (int)GetValue(AncestorColumnsProperty);
        set => SetValue(AncestorColumnsProperty, value);
    }

    /// <summary>이 영역 위에 파란 경로 탭을 그릴지(끄면 호출 측이 다른 곳에 경로 탭을 둔다).</summary>
    public bool ShowAddressBar
    {
        get => (bool)GetValue(ShowAddressBarProperty);
        set => SetValue(ShowAddressBarProperty, value);
    }

    /// <summary>선택된 영역에 테두리 색을 입힐지(한 영역만 보이는 배치에서는 끈다).</summary>
    public bool HighlightActive
    {
        get => (bool)GetValue(HighlightActiveProperty);
        set => SetValue(HighlightActiveProperty, value);
    }

    private ExplorerViewModel? Vm => DataContext as ExplorerViewModel;

    private void ApplyAncestorColumns()
    {
        int count = AncestorColumns;
        double width = count == 1 ? NarrowColumnWidth : ColumnWidth;
        Show(ParentColumn, ParentSplitColumn, ParentView, ParentSplitter, count >= 1, width);
        Show(GrandColumn, GrandSplitColumn, GrandView, GrandSplitter, count >= 2, width);
        Show(GreatColumn, GreatSplitColumn, GreatView, GreatSplitter, count >= 3, width);
    }

    /// <summary>열과 그 오른쪽 분할 막대를 보이거나 접는다(접힌 열은 폭 0이라 자리를 차지하지 않는다).</summary>
    private static void Show(ColumnDefinition column, ColumnDefinition split, UIElement view, UIElement splitter, bool visible, double width)
    {
        column.Width = visible ? new GridLength(width) : new GridLength(0);
        column.MinWidth = visible ? 80 : 0;
        split.Width = visible ? new GridLength(SplitterWidth) : new GridLength(0);
        view.Visibility = splitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }
}
