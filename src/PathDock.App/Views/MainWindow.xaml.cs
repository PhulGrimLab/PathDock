using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
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

    private const double TerminalMinHeight = 120;

    private readonly MainViewModel _vm;
    private double _expandedRatio = 0.5;

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;
        ApplyMinSize(vm.UiFontSize);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.UiFontSize))
            {
                ApplyMinSize(vm.UiFontSize);
            }
        };
        RestoreWindowState();
        _vm.Terminal.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TerminalViewModel.IsCollapsed))
            {
                ApplyTerminalCollapsed();
            }
        };
        ApplyTerminalCollapsed();
        Closing += OnClosing;
        Loaded += (_, _) => TerminalPanel.FocusTerminal();
        StartClock();
    }

    /// <summary>
    /// 화면 글자가 커지면 즐겨찾기 줄 수와 목록 행 높이가 늘어 같은 창 크기로는 탐색기가 사라지므로, 창/탐색기 최소 크기를 글자 크기에 비례해 키운다.
    /// 작업 영역보다 커지지는 않는다.
    /// </summary>
    private void ApplyMinSize(double fontSize)
    {
        double extra = Math.Max(0, fontSize - 12);
        MinWidth = Math.Min(760 + extra * 25, SystemParameters.WorkArea.Width);
        MinHeight = Math.Min(600 + extra * 25, SystemParameters.WorkArea.Height);
        ExplorerRow.MinHeight = 260 + extra * 12;
        if (Width < MinWidth)
        {
            Width = MinWidth;
        }
        if (Height < MinHeight)
        {
            Height = MinHeight;
        }
    }

    /// <summary>제목 표시줄 시계를 시작한다. 창과 수명이 같고, 창이 닫히면 멈춘다.</summary>
    private void StartClock()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => UpdateClock();
        Closed += (_, _) => timer.Stop();
        UpdateClock();
        timer.Start();
    }

    private void UpdateClock()
    {
        string text = DateTime.Now.ToString("yyyy-MM-dd (ddd) HH:mm:ss", CultureInfo.CurrentCulture);
        if (ClockText.Text != text)
        {
            ClockText.Text = text;
        }
    }

    /// <summary>시계를 더블 클릭하면 Windows 알림 센터(달력 포함)를 연다. 제목 표시줄의 더블 클릭 최대화는 막는다.</summary>
    private void OnClockMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }
        e.Handled = true;
        try
        {
            Process.Start(new ProcessStartInfo("ms-actioncenter:") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            _vm.InfoMessage = $"달력을 열 수 없습니다: {ex.Message}";
        }
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
        _expandedRatio = ratio;
        ExplorerRow.Height = new GridLength(ratio, GridUnitType.Star);
        TerminalRow.Height = new GridLength(1 - ratio, GridUnitType.Star);
    }

    /// <summary>터미널을 접으면 머리글 높이만 남기고 탐색기가 나머지를 채운다. 펼치면 접기 전 비율로 되돌린다.</summary>
    private void ApplyTerminalCollapsed()
    {
        if (_vm.Terminal.IsCollapsed)
        {
            double total = ExplorerRow.ActualHeight + TerminalRow.ActualHeight;
            if (total > 0 && TerminalRow.Height.IsStar)
            {
                _expandedRatio = Math.Clamp(ExplorerRow.ActualHeight / total, MinExplorerRatio, MaxExplorerRatio);
            }
            else
            {
                // 레이아웃 전(시작 시 접힌 상태): 복원해 둔 비율을 유지한다.
            }
            ExplorerRow.Height = new GridLength(1, GridUnitType.Star);
            TerminalRow.MinHeight = 0;
            TerminalRow.Height = GridLength.Auto;
            TerminalSplitter.Visibility = Visibility.Collapsed;
        }
        else
        {
            TerminalRow.MinHeight = TerminalMinHeight;
            ExplorerRow.Height = new GridLength(_expandedRatio, GridUnitType.Star);
            TerminalRow.Height = new GridLength(1 - _expandedRatio, GridUnitType.Star);
            TerminalSplitter.Visibility = Visibility.Visible;
        }
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
        if (_vm.Terminal.IsCollapsed)
        {
            s.ExplorerRatio = _expandedRatio;
        }
        else if (total > 0)
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
