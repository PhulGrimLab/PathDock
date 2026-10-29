using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using PathDock.App.ViewModels;
using PathDock.Core.Localization;
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
    private double _expandedRatio = 0.6;
    // 탐색기 높이 비율은 배치마다 따로 기억한다(4분할은 폴더가 네 개라 탐색기를 더 크게). _expandedRatio는 지금 배치의 값이다.
    private double _otherLayoutRatio = 0.6;
    private bool _ratioIsForQuad;

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;
        UpdateVersionLabel();
        Loc.LanguageChanged += UpdateVersionLabel;
        Closed += (_, _) => Loc.LanguageChanged -= UpdateVersionLabel;
        ApplyMinSize(vm.Preferences.UiFontSize);
        vm.Preferences.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.UiFontSize))
            {
                ApplyMinSize(vm.Preferences.UiFontSize);
            }
        };
        RestoreWindowState();
        LayoutButtons.LayoutUpdated += (_, _) => CenterLayoutButtons();
        ApplyPaneLayout();
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.Layout) or nameof(MainViewModel.Explorer))
            {
                ApplyPaneLayout();
            }
        };
        _vm.TerminalFocusRequested += () => TerminalPanel.FocusTerminal();
        TerminalPanel.AllowDrop = true;
        TerminalPanel.DragOver += OnTerminalDragOver;
        TerminalPanel.Drop += OnTerminalDrop;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Layout))
            {
                SwitchLayoutRatio();
            }
        };
        _vm.Terminal.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TerminalViewModel.IsCollapsed))
            {
                ApplyTerminalCollapsed();
            }
        };
        ApplyTerminalCollapsed();
        Closing += OnClosing;
        Application.Current.SessionEnding += (_, _) => _sessionEnding = true;
        Loaded += (_, _) => TerminalPanel.FocusTerminal();
        StartClock();
    }


    /// <summary>
    /// 폴더 영역 4칸과 윗줄 경로 탭 4개의 위치·보임·상위 폴더 열 수를 현재 배치에 맞춘다(뷰는 새로 만들지 않는다).
    /// 4분할: 2열 2행(영역 사이 16px, 십자 선), 위 두 영역의 경로 탭은 윗줄에·아래 두 영역은 PaneView 안에. 2묶음: 앞 두 영역(상위 열 1개). 한 경로: 선택된 영역 하나(상위 열 3개).
    /// </summary>
    private void ApplyPaneLayout()
    {
        var layout = _vm.Layout;
        bool quad = layout == PathDock.Core.Settings.ExplorerLayout.Quad;
        bool dual = layout == PathDock.Core.Settings.ExplorerLayout.Dual;
        bool single = !quad && !dual;

        // 폴더 영역 칸 사이 간격은 PaneView 안쪽 여백 8px(양쪽 4px씩)을 더해 카드 사이 간격이 된다. 윗줄 경로 탭은 카드 사이 간격 그대로.
        PaneRow1.Height = new GridLength(quad ? 8 : 0);
        PaneRow2.Height = quad ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        PaneCol1.Width = new GridLength(quad ? 8 : 0);
        TabCol1.Width = new GridLength(quad ? 16 : 8);
        QuadVLine.Visibility = QuadHLine.Visibility = QuadTabLine.Visibility = quad ? Visibility.Visible : Visibility.Collapsed;

        PaneView[] panes = { Pane0, Pane1, Pane2, Pane3 };
        PathDock.App.Controls.AddressBar[] tabs = { Tab0, Tab1, Tab2, Tab3 };
        for (int i = 0; i < panes.Length; i++)
        {
            bool selectedInSingle = single && ReferenceEquals(_vm.Panes[i], _vm.Explorer);
            PaneView pane = panes[i];
            pane.Visibility = quad || (dual && i < 2) || selectedInSingle ? Visibility.Visible : Visibility.Collapsed;
            System.Windows.Controls.Grid.SetRow(pane, quad && i >= 2 ? 2 : 0);
            System.Windows.Controls.Grid.SetColumn(pane, single ? 0 : (i % 2 == 0 ? 0 : 2));
            System.Windows.Controls.Grid.SetColumnSpan(pane, single ? 3 : 1);
            pane.AncestorColumns = quad ? 0 : dual ? 1 : 3;
            pane.HighlightActive = !single;
            pane.ShowAddressBar = quad && i >= 2;

            PathDock.App.Controls.AddressBar tab = tabs[i];
            tab.Visibility = (quad || dual) ? (i < 2 ? Visibility.Visible : Visibility.Collapsed) : (selectedInSingle ? Visibility.Visible : Visibility.Collapsed);
            System.Windows.Controls.Grid.SetColumn(tab, single ? 0 : (i % 2 == 0 ? 0 : 2));
            System.Windows.Controls.Grid.SetColumnSpan(tab, single ? 3 : 1);
        }
    }

    /// <summary>
    /// 배치 버튼(TitleBar.CenterContent)을 창의 가로 가운데로 옮긴다. 제목 표시줄은 앞뒤 내용 때문에 가운데 칸이 한쪽으로 치우쳐
    /// 있으므로, 지금 위치와 창 가운데의 차이만큼 렌더 변환을 더한다(레이아웃은 바뀌지 않아 반복되지 않는다).
    /// </summary>
    private void CenterLayoutButtons()
    {
        if (!LayoutButtons.IsVisible || LayoutButtons.ActualWidth <= 0 || ActualWidth <= 0)
        {
            return;
        }
        double current = LayoutButtons.TransformToAncestor(this).Transform(new Point(LayoutButtons.ActualWidth / 2, 0)).X; // 지금의 이동량이 포함된 값
        double delta = ActualWidth / 2 - current;
        if (Math.Abs(delta) > 0.5)
        {
            LayoutButtonsShift.X += delta;
        }
    }

    /// <summary>제목 표시줄의 버전·빌드 날짜 문구와 툴팁을 현재 언어로 만든다.</summary>
    private void UpdateVersionLabel()
    {
        VersionLabel.Text = Services.AppInfo.TitleBarText;
        VersionLabel.ToolTip = Services.AppInfo.BuildTime is { } built
            ? Loc.T($"버전 {Services.AppInfo.Version} · 빌드 {built}", $"Version {Services.AppInfo.Version} · built {built}")
            : Loc.T($"버전 {Services.AppInfo.Version}", $"Version {Services.AppInfo.Version}");
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
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
        // 초가 바뀌는 순간에 맞춰 깨어난다(250ms마다 깨어 같은 글자를 확인하던 것보다 유휴 깨움이 1/4).
        timer.Tick += (_, _) =>
        {
            UpdateClock();
            timer.Interval = TimeSpan.FromMilliseconds(1000 - DateTime.Now.Millisecond + 5);
        };
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

    // ----- 터미널로 끌어 놓기: 파일을 터미널에 놓으면 경로를 입력줄에 넣는다(Enter는 누르지 않는다). -----

    private void OnTerminalDragOver(object sender, DragEventArgs e)
    {
        e.Effects = FileDragDrop.GetPaths(e.Data).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnTerminalDrop(object sender, DragEventArgs e)
    {
        IReadOnlyList<string> paths = FileDragDrop.GetPaths(e.Data);
        if (paths.Count > 0)
        {
            _vm.SendPathsToTerminal(paths);
        }
        e.Handled = true;
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
            _vm.Info.Show(Loc.T($"달력을 열 수 없습니다: {ex.Message}", $"Could not open the calendar: {ex.Message}"));
        }
    }

    /// <summary>
    /// 터미널 탭 단축키(Ctrl+Shift+T 새 탭, Ctrl+Shift+W 닫기, Ctrl+Tab / Ctrl+Shift+Tab 전환).
    /// 터미널이 키를 셸로 보내기 전에 창에서 먼저 가로챈다(Preview). 처리한 뒤에는 입력이 이어지도록 포커스를 터미널로 준다.
    /// </summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        ModifierKeys mods = Keyboard.Modifiers;
        TerminalViewModel t = _vm.Terminal;
        if (e.Key == Key.T && mods == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            t.NewTabCommand.Execute(null);
        }
        else if (e.Key == Key.W && mods == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            t.CloseTabCommand.Execute(null);
        }
        else if (e.Key == Key.Tab && mods == ModifierKeys.Control)
        {
            t.NextTabCommand.Execute(null);
        }
        else if (e.Key == Key.Tab && mods == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            t.PreviousTabCommand.Execute(null);
        }
        else
        {
            return;
        }
        e.Handled = true;
        TerminalPanel.FocusTerminal();
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
        double normal = Math.Clamp(s.ExplorerRatio, MinExplorerRatio, MaxExplorerRatio);
        double quad = Math.Clamp(s.QuadExplorerRatio, MinExplorerRatio, MaxExplorerRatio);
        _ratioIsForQuad = _vm.IsQuadLayout;
        double ratio = _ratioIsForQuad ? quad : normal;
        _otherLayoutRatio = _ratioIsForQuad ? normal : quad;
        _expandedRatio = ratio;
        ExplorerRow.Height = new GridLength(ratio, GridUnitType.Star);
        TerminalRow.Height = new GridLength(1 - ratio, GridUnitType.Star);
    }

    /// <summary>지금 화면의 탐색기 비율(터미널이 접혀 있으면 펼쳤을 때의 비율).</summary>
    private double CurrentRatio()
    {
        double total = ExplorerRow.ActualHeight + TerminalRow.ActualHeight;
        return !_vm.Terminal.IsCollapsed && total > 0 && TerminalRow.Height.IsStar
            ? Math.Clamp(ExplorerRow.ActualHeight / total, MinExplorerRatio, MaxExplorerRatio)
            : _expandedRatio;
    }

    /// <summary>4분할로 들어가거나 나올 때, 떠나는 배치의 비율을 기억하고 들어가는 배치의 비율로 바꾼다.</summary>
    private void SwitchLayoutRatio()
    {
        bool toQuad = _vm.IsQuadLayout;
        if (toQuad == _ratioIsForQuad)
        {
            return; // 2묶음 <-> 한 경로: 같은 비율을 쓴다.
        }
        (_expandedRatio, _otherLayoutRatio) = (_otherLayoutRatio, CurrentRatio());
        _ratioIsForQuad = toQuad;
        if (!_vm.Terminal.IsCollapsed)
        {
            ExplorerRow.Height = new GridLength(_expandedRatio, GridUnitType.Star);
            TerminalRow.Height = new GridLength(1 - _expandedRatio, GridUnitType.Star);
        }
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

    private bool _sessionEnding;

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // 실행 중인 명령이 있으면 종료 전에 확인한다(앱을 닫으면 모든 탭의 셸과 그 작업이 끝난다). Windows 로그오프/종료 중에는 묻지 않는다.
        int running = _vm.Terminal.RunningTabCount;
        if (running > 0 && !_sessionEnding)
        {
            string message = Loc.T(
                $"명령이 실행 중인 터미널 탭이 {running}개 있습니다. 종료하면 그 작업이 모두 끝납니다. 종료할까요?",
                $"{running} terminal tab(s) have a command running. Exiting will end all of them. Exit anyway?");
            if (!ConfirmDialog.Ask(this, Loc.T("종료 확인", "Confirm exit"), message))
            {
                e.Cancel = true;
                return;
            }
        }
        var s = _vm.Settings;
        s.WindowMaximized = WindowState == WindowState.Maximized;
        Rect bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        s.WindowLeft = bounds.Left;
        s.WindowTop = bounds.Top;
        s.WindowWidth = bounds.Width;
        s.WindowHeight = bounds.Height;
        double current = CurrentRatio(); // 레이아웃 전 종료나 접힌 터미널이면 기억해 둔 값 그대로
        s.ExplorerRatio = _ratioIsForQuad ? _otherLayoutRatio : current;
        s.QuadExplorerRatio = _ratioIsForQuad ? current : _otherLayoutRatio;
        _vm.SaveSettings();
    }
}
