using System.Windows.Controls;

namespace PathDock.App.Views;

/// <summary>터미널 패널(머리글 + <c>TerminalControl</c>). 표시 전용이며 창이 시작 포커스를 줄 수 있게 한다.</summary>
public partial class TerminalPanelView : UserControl
{
    public TerminalPanelView()
    {
        InitializeComponent();
        // 탭 줄 버튼(탭 선택/닫기/새 탭)을 누른 뒤에는 곧바로 입력할 수 있게 포커스를 터미널로 돌려준다.
        TabStrip.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent,
            new System.Windows.RoutedEventHandler((_, _) => Dispatcher.BeginInvoke(FocusTerminal)));
    }

    /// <summary>지우기 버튼을 누른 뒤 곧바로 입력할 수 있게 포커스를 터미널로 돌려준다.</summary>
    private void OnClearClick(object sender, System.Windows.RoutedEventArgs e) => FocusTerminal();

    /// <summary>키보드 포커스를 터미널 화면에 준다.</summary>
    public void FocusTerminal() => TerminalView.Focus();
}
