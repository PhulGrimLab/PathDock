using System.ComponentModel;
using System.Windows;
using PathDock.App.ViewModels;
using Wpf.Ui.Controls;

namespace PathDock.App.Views;

/// <summary>
/// 메인 창. code-behind에는 UI 전용 코드(창 위치/크기/분할 복원·저장, 시작 포커스)만 둔다.
/// 화면은 뷰(UserControl)들로 나뉘어 있고 로직은 모두 <see cref="MainViewModel"/>이 가진다.
/// </summary>
public partial class MainWindow : FluentWindow
{
    private const double MinExplorerRatio = 0.15;
    private const double MaxExplorerRatio = 0.85;
    private const double OffscreenMarginX = 100;
    private const double OffscreenMarginTop = 20;
    private const double OffscreenMarginBottom = 100;

    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;
        RestoreWindowState();
        Closing += OnClosing;
        Loaded += (_, _) => TerminalPanel.FocusTerminal();
    }

    /// <summary>저장된 창 위치/크기/최대화/분할 비율을 복원한다(화면 밖이면 위치는 OS 기본값).</summary>
    private void RestoreWindowState()
    {
        var s = _vm.Settings;
        Width = Math.Max(MinWidth, s.WindowWidth);
        Height = Math.Max(MinHeight, s.WindowHeight);
        if (s.WindowLeft is { } left && s.WindowTop is { } top && IsVisibleOnScreen(left, top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        if (s.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }
        double ratio = Math.Clamp(s.ExplorerRatio, MinExplorerRatio, MaxExplorerRatio);
        ExplorerRow.Height = new GridLength(ratio, GridUnitType.Star);
        TerminalRow.Height = new GridLength(1 - ratio, GridUnitType.Star);
    }

    private static bool IsVisibleOnScreen(double left, double top)
    {
        return left > SystemParameters.VirtualScreenLeft - OffscreenMarginX
            && left < SystemParameters.VirtualScreenWidth + SystemParameters.VirtualScreenLeft - OffscreenMarginX
            && top > SystemParameters.VirtualScreenTop - OffscreenMarginTop
            && top < SystemParameters.VirtualScreenHeight + SystemParameters.VirtualScreenTop - OffscreenMarginBottom;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        var s = _vm.Settings;
        s.WindowMaximized = WindowState == WindowState.Maximized;
        Rect bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        s.WindowLeft = bounds.Left;
        s.WindowTop = bounds.Top;
        s.WindowWidth = bounds.Width;
        s.WindowHeight = bounds.Height;
        double total = ExplorerRow.ActualHeight + TerminalRow.ActualHeight;
        if (total > 0)
        {
            s.ExplorerRatio = ExplorerRow.ActualHeight / total;
        }
        else
        {
            // 레이아웃 전 종료: 비율은 기존 값 유지.
        }
        _vm.SaveSettings();
    }
}
