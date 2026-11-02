using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PathDock.Core.Localization;
using PathDock.Core.Terminal;

namespace PathDock.App.Controls;

/// <summary>
/// 터미널 화면 커스텀 컨트롤. <see cref="TerminalSession"/>의 화면 버퍼를 보여주고 키보드/마우스 입력을 셸로 전달한다.
/// 역할 분담: 그리기는 <see cref="TerminalRenderer"/>, 마우스 선택은 <see cref="TerminalSelection"/>(Core), 키 → VT 시퀀스 변환은
/// <see cref="TerminalKeyMap"/>이 맡고, 이 컨트롤은 속성/타이머/입력 이벤트 연결/UI Automation만 가진다.
/// Owner: MainWindow(XAML). Lifetime: 창과 동일. 스레드: UI 스레드 전용 - 버퍼 읽기는 SyncRoot로 보호하되 락 안에서는 화면을 복사만 하고
/// 그리기는 락 밖에서 한다. 33ms 타이머가 버퍼 Version 변화를 감지했을 때만 다시 그린다.
/// 크기가 바뀌면 열/행을 계산해 세션을 리사이즈한다. UI Automation에는 화면 텍스트를 ValuePattern(읽기 전용)으로 노출한다.
/// </summary>
public sealed class TerminalControl : FrameworkElement
{
    private const int RefreshIntervalMs = 33;
    private const int WheelLines = 3;
    private const int MinCols = 20;
    private const int MinRows = 3;

    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session), typeof(TerminalSession), typeof(TerminalControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((TerminalControl)d).OnSessionChanged()));

    public static readonly DependencyProperty ColorSchemeProperty = DependencyProperty.Register(
        nameof(ColorScheme), typeof(TerminalColorScheme), typeof(TerminalControl),
        new FrameworkPropertyMetadata(TerminalThemes.All[0], FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((TerminalControl)d).OnColorSchemeChanged()));

    public static readonly DependencyProperty TerminalFontSizeProperty = DependencyProperty.Register(
        nameof(TerminalFontSize), typeof(double), typeof(TerminalControl),
        new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((TerminalControl)d).OnFontSizeChanged()));

    private readonly TerminalRenderer _renderer = new();
    private readonly TerminalSelection _selection = new();
    private readonly TerminalFrame _frame = new();
    private readonly DispatcherTimer _timer;
    private long _renderedVersion = -1;
    private int _scrollOffset;

    /// <summary>표시할 세션(ViewModel에서 바인딩).</summary>
    public TerminalSession? Session
    {
        get => (TerminalSession?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    /// <summary>색 구성(테마 + 사용자 지정 글자/배경색).</summary>
    public TerminalColorScheme ColorScheme
    {
        get => (TerminalColorScheme)GetValue(ColorSchemeProperty);
        set => SetValue(ColorSchemeProperty, value);
    }

    /// <summary>글자 크기(DIP). 바뀌면 셀 크기를 다시 재고 열/행 수를 다시 계산한다.</summary>
    public double TerminalFontSize
    {
        get => (double)GetValue(TerminalFontSizeProperty);
        set => SetValue(TerminalFontSizeProperty, value);
    }

    public TerminalControl()
    {
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        InputMethod.SetIsInputMethodEnabled(this, true);
        MeasureCell();
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(RefreshIntervalMs) };
        _timer.Tick += (_, _) => RefreshIfChanged();
        // 접혀 있거나 가려진 동안에는 깨우지 않는다(Unloaded는 Collapsed일 때 오지 않는다).
        Loaded += (_, _) => UpdateTimer();
        IsVisibleChanged += (_, _) => UpdateTimer();
        Unloaded += (_, _) => _timer.Stop();
    }

    private void UpdateTimer()
    {
        if (IsLoaded && IsVisible)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    private void OnSessionChanged()
    {
        _renderedVersion = -1;
        _scrollOffset = 0;
        // 선택 영역은 이전 탭 화면의 좌표다: 남겨 두면 새 탭에서 엉뚱한 곳이 강조되고 Ctrl+C가 중단 대신 복사로 처리된다.
        _selection.Clear();
        _selection.EndDrag();
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }
        ApplySize();
    }

    private void OnColorSchemeChanged()
    {
        _renderer.ApplyColorScheme(ColorScheme);
        _renderedVersion = -1;
        InvalidateVisual();
    }

    private void OnFontSizeChanged()
    {
        MeasureCell();
        _renderedVersion = -1;
        ApplySize();
    }

    /// <summary>다른 배율의 모니터로 옮겨 DPI가 바뀌면 글자 칸 크기를 다시 재야 한다(안 그러면 칸과 글자가 어긋난다).</summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        MeasureCell();
        _renderedVersion = -1;
        ApplySize();
    }

    /// <summary>드래그 중 포커스를 잃어(Alt+Tab 등) 마우스 캡처가 풀리면 드래그 상태도 끝낸다(안 그러면 "끌고 있는 중"이 남는다).</summary>
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _selection.EndDrag();
    }

    private void MeasureCell() => _renderer.MeasureCell(TerminalFontSize, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private void RefreshIfChanged()
    {
        TerminalSession? s = Session;
        if (s is null)
        {
            return;
        }
        long v;
        lock (s.Screen.SyncRoot)
        {
            v = s.Screen.Version;
        }
        if (v != _renderedVersion)
        {
            InvalidateVisual();
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        ApplySize();
    }

    /// <summary>현재 컨트롤 크기에 맞는 열/행을 계산해 세션에 반영한다.</summary>
    private void ApplySize()
    {
        TerminalSession? s = Session;
        if (s is null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }
        int cols = Math.Max(MinCols, (int)(ActualWidth / _renderer.CellWidth));
        int rows = Math.Max(MinRows, (int)(ActualHeight / _renderer.CellHeight));
        if (cols != s.Screen.Cols || rows != s.Screen.Rows)
        {
            s.Resize(cols, rows);
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        _renderer.DrawBackground(dc, ActualWidth, ActualHeight);
        TerminalSession? s = Session;
        if (s is null)
        {
            return;
        }
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        lock (s.Screen.SyncRoot)
        {
            CaptureFrame(s.Screen); // 락 안에서는 복사만 한다.
        }
        _renderer.Draw(dc, _frame, TerminalFontSize, dpi); // 그리기는 락 밖에서: 그동안 셸 출력 파서가 기다리지 않는다.
    }

    /// <summary>화면 버퍼를 읽어 이번 프레임의 복사본(행, 선택 범위, 커서)을 만든다. 호출 시 <see cref="ScreenBuffer.SyncRoot"/>를 잡고 있어야 한다.</summary>
    private void CaptureFrame(ScreenBuffer b)
    {
        _selection.FollowScroll(b);
        _renderedVersion = b.Version;
        _scrollOffset = Math.Min(_scrollOffset, b.ScrollbackCount);
        _frame.Prepare(b.Rows);
        for (int y = 0; y < b.Rows; y++)
        {
            int line = y - _scrollOffset;
            _frame.SetRow(y, b.GetLine(line));
            if (_selection.TryGetRowRange(line, b.Cols, out int from, out int to))
            {
                _frame.SelFrom[y] = from;
                _frame.SelTo[y] = to;
            }
            else
            {
                _frame.SelFrom[y] = -1;
            }
        }
        // 스크롤백을 보는 중이거나 포커스가 없으면 커서를 그리지 않는다.
        _frame.ShowCursor = _scrollOffset == 0 && b.CursorVisible && IsKeyboardFocused;
        _frame.CursorX = Math.Min(b.CursorX, b.Cols - 1);
        _frame.CursorY = b.CursorY;
    }

    #region 입력

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (Session is null)
        {
            return;
        }
        if (e.ChangedButton == MouseButton.Right)
        {
            // 선택이 있으면 복사, 없으면 붙여넣기(Windows Terminal 방식).
            if (!CopySelection())
            {
                _scrollOffset = 0;
                PasteFromClipboard(Session!);
            }
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.Left)
        {
            (int Line, int Col) cell = CellAt(e.GetPosition(this));
            if (e.ClickCount == 2)
            {
                if (_selection.SelectWord(Session.Screen, cell))
                {
                    InvalidateVisual();
                }
                return;
            }
            _selection.Begin(cell, ScrolledNow());
            CaptureMouse();
            InvalidateVisual();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_selection.IsDragging)
        {
            return;
        }
        Point p = e.GetPosition(this);
        TerminalSession? s = Session;
        if (s is not null && (p.Y < 0 || p.Y > ActualHeight))
        {
            // 위/아래 바깥으로 끌면 스크롤백을 한 줄씩 따라간다.
            _scrollOffset = Math.Clamp(_scrollOffset + (p.Y < 0 ? 1 : -1), 0, ScrollbackCountOf(s));
        }
        _selection.Extend(CellAt(p));
        InvalidateVisual();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_selection.IsDragging && e.ChangedButton == MouseButton.Left)
        {
            _selection.EndDrag();
            ReleaseMouseCapture();
        }
    }

    /// <summary>화면 좌표 → (버퍼 줄 번호, 열). 범위를 벗어나면 가장자리로 맞춘다.</summary>
    private (int Line, int Col) CellAt(Point p)
    {
        TerminalSession? s = Session;
        int cols = s?.Screen.Cols ?? 1, rows = s?.Screen.Rows ?? 1;
        int col = Math.Clamp((int)(p.X / _renderer.CellWidth), 0, cols - 1);
        int row = Math.Clamp((int)(p.Y / _renderer.CellHeight), 0, rows - 1);
        return (row - _scrollOffset, col);
    }

    private long ScrolledNow()
    {
        TerminalSession? s = Session;
        if (s is null)
        {
            return 0;
        }
        lock (s.Screen.SyncRoot)
        {
            return s.Screen.ScrolledLines;
        }
    }

    private static int ScrollbackCountOf(TerminalSession s)
    {
        lock (s.Screen.SyncRoot)
        {
            return s.Screen.ScrollbackCount;
        }
    }

    /// <summary>선택한 범위를 클립보드에 복사하고 선택을 푼다. 선택이 없으면 false.</summary>
    private bool CopySelection()
    {
        TerminalSession? s = Session;
        if (s is null || !_selection.HasSelection)
        {
            return false;
        }
        string selected;
        lock (s.Screen.SyncRoot)
        {
            selected = _selection.ExtractText(s.Screen);
        }
        try
        {
            Clipboard.SetText(selected);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // 다른 프로그램이 클립보드를 쓰는 중: 선택을 유지해 다시 시도할 수 있게 한다.
            return true;
        }
        _selection.Clear();
        InvalidateVisual();
        return true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        TerminalSession? s = Session;
        if (s is null)
        {
            return;
        }
        int delta = e.Delta > 0 ? WheelLines : -WheelLines;
        _scrollOffset = Math.Clamp(_scrollOffset + delta, 0, ScrollbackCountOf(s));
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        InvalidateVisual();
    }

    /// <summary>
    /// 특수 키(Enter/방향키/Ctrl 조합 등)를 VT 시퀀스로 변환해 전송한다. 일반 문자는 <see cref="OnTextInput"/>이 처리한다.
    /// Tab/방향키가 WPF 포커스 이동에 쓰이지 않도록 Preview 단계에서 가로챈다.
    /// </summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        TerminalSession? s = Session;
        if (s is null)
        {
            return;
        }
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        ModifierKeys mods = Keyboard.Modifiers;
        if (TerminalKeyMap.IsCopyChord(key, mods) && (_selection.HasSelection || (mods & ModifierKeys.Shift) != 0))
        {
            // 선택이 있으면 Ctrl+C=복사, 선택이 없으면 Ctrl+C는 인터럽트로 전달. Ctrl+Shift+C는 항상 복사(없으면 무시).
            CopySelection();
            e.Handled = true;
            return;
        }
        string? seq = TerminalKeyMap.Translate(key, mods);
        if (seq is null)
        {
            return; // 문자 입력은 TextInput으로.
        }
        if (seq.Length == 0)
        {
            PasteFromClipboard(s); // 붙여넣기 요청.
        }
        else
        {
            _scrollOffset = 0;
            s.SendInput(seq);
        }
        e.Handled = true;
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);
        TerminalSession? s = Session;
        if (s is null || string.IsNullOrEmpty(e.Text))
        {
            return;
        }
        _scrollOffset = 0;
        s.SendInput(e.Text);
        e.Handled = true;
    }

    private void PasteFromClipboard(TerminalSession s)
    {
        string clipboardText;
        try
        {
            if (!Clipboard.ContainsText())
            {
                return; // 텍스트가 아닌 클립보드: 무시.
            }
            clipboardText = Clipboard.GetText();
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return; // 다른 프로그램이 클립보드를 쓰는 중: 이번 붙여넣기는 건너뛴다(복사와 같은 처리).
        }
        // 줄바꿈은 Enter(\r)로 통일하고 눈에 안 보이는 제어 문자는 제거한다(PasteGuard).
        string text = PasteGuard.Normalize(clipboardText);
        if (text.Length == 0)
        {
            return;
        }
        if (PasteGuard.RunsCommands(text) && !ConfirmPaste(text))
        {
            return;
        }
        s.SendInput(text);
    }

    /// <summary>
    /// 줄바꿈(Enter)이 하나라도 있으면 붙여넣는 즉시 명령이 실행된다(끝의 줄바꿈 하나뿐인 한 줄도 마찬가지) - 내용을 보여주고 확인한다.
    /// </summary>
    private bool ConfirmPaste(string text)
    {
        int lines = PasteGuard.LineCount(text);
        string preview = PasteGuard.Preview(text, 5, 80, out int hidden);
        string more = hidden > 0 ? Loc.T($"\n… 외 {hidden}줄", $"\n… and {hidden} more line(s)") : string.Empty;
        string message = Loc.T(
            $"{lines}줄을 터미널에 붙여넣습니다. 줄바꿈마다 명령이 바로 실행됩니다.\n\n{preview}{more}\n\n계속할까요?",
            $"{lines} line(s) will be pasted into the terminal. Each line break runs a command immediately.\n\n{preview}{more}\n\nContinue?");
        return PathDock.App.Views.ConfirmDialog.Ask(Window.GetWindow(this), Loc.T("붙여넣기 확인", "Confirm paste"), message);
    }

    #endregion

    #region UI Automation

    protected override AutomationPeer OnCreateAutomationPeer() => new TerminalAutomationPeer(this);

    /// <summary>화면 텍스트를 읽기 전용 ValuePattern으로 노출한다(자동 테스트용).</summary>
    private sealed class TerminalAutomationPeer : FrameworkElementAutomationPeer, IValueProvider
    {
        private readonly TerminalControl _owner;

        public TerminalAutomationPeer(TerminalControl owner) : base(owner)
        {
            _owner = owner;
        }

        protected override string GetClassNameCore() => nameof(TerminalControl);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Edit;
        protected override bool IsControlElementCore() => true;
        protected override bool IsContentElementCore() => true;

        public override object? GetPattern(PatternInterface patternInterface)
        {
            return patternInterface == PatternInterface.Value ? this : base.GetPattern(patternInterface);
        }

        public bool IsReadOnly => true;

        public string Value => _owner.Session?.Screen.GetVisibleText() ?? string.Empty;

        public void SetValue(string value)
        {
            throw new InvalidOperationException("터미널 화면은 읽기 전용입니다.");
        }
    }

    #endregion
}
