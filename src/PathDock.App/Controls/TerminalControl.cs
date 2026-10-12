using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PathDock.Core.Terminal;

namespace PathDock.App.Controls;

/// <summary>
/// 터미널 화면 커스텀 컨트롤. <see cref="TerminalSession"/>의 화면 버퍼를 그리고 키보드 입력을 VT 시퀀스로 바꿔 보낸다.
/// Owner: MainWindow(XAML). Lifetime: 창과 동일. 스레드: UI 스레드 전용 - 버퍼 읽기는 SyncRoot로 보호,
/// 33ms 타이머가 버퍼 Version 변화를 감지했을 때만 다시 그린다.
/// 크기가 바뀌면 열/행을 계산해 세션을 리사이즈한다. UI Automation에는 화면 텍스트를 ValuePattern(읽기 전용)으로 노출한다.
/// </summary>
public sealed class TerminalControl : FrameworkElement
{
    private const double FontSizeDip = 14;
    private const int RefreshIntervalMs = 33;
    private const int WheelLines = 3;
    private const int MinCols = 20;
    private const int MinRows = 3;
    private static readonly Brush DefaultBackground = Freeze(new SolidColorBrush(Color.FromRgb(0x0C, 0x0C, 0x0C)));
    private static readonly Brush DefaultForeground = Freeze(new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)));
    private static readonly Brush CursorBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)));

    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session), typeof(TerminalSession), typeof(TerminalControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((TerminalControl)d).OnSessionChanged()));

    private readonly Dictionary<uint, Brush> _brushes = new();
    private readonly DispatcherTimer _timer;
    private readonly Typeface _normal = new(new FontFamily("Cascadia Mono, Consolas, D2Coding, Malgun Gothic"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private readonly Typeface _bold = new(new FontFamily("Cascadia Mono, Consolas, D2Coding, Malgun Gothic"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private double _cellW = 8;
    private double _cellH = 16;
    private long _renderedVersion = -1;
    private int _scrollOffset;

    /// <summary>표시할 세션(ViewModel에서 바인딩).</summary>
    public TerminalSession? Session
    {
        get => (TerminalSession?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
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
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }

    private void OnSessionChanged()
    {
        _renderedVersion = -1;
        _scrollOffset = 0;
        ApplySize();
    }

    /// <summary>"M" 한 글자로 셀 크기를 잰다(고정폭 폰트 가정).</summary>
    private void MeasureCell()
    {
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var ft = new FormattedText("M", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _normal, FontSizeDip, Brushes.White, dpi);
        _cellW = Math.Max(1, ft.WidthIncludingTrailingWhitespace);
        _cellH = Math.Max(1, Math.Ceiling(ft.Height));
    }

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
        else
        {
            // 변화 없음: 다시 그리지 않는다.
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
        int cols = Math.Max(MinCols, (int)(ActualWidth / _cellW));
        int rows = Math.Max(MinRows, (int)(ActualHeight / _cellH));
        if (cols != s.Screen.Cols || rows != s.Screen.Rows)
        {
            s.Resize(cols, rows);
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(DefaultBackground, null, new Rect(0, 0, w, h));
        TerminalSession? s = Session;
        if (s is null)
        {
            return;
        }

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        ScreenBuffer b = s.Screen;
        lock (b.SyncRoot)
        {
            _renderedVersion = b.Version;
            _scrollOffset = Math.Min(_scrollOffset, b.ScrollbackCount);
            for (int y = 0; y < b.Rows; y++)
            {
                DrawRow(dc, b.GetLine(y - _scrollOffset), y * _cellH, dpi);
            }
            if (_scrollOffset == 0 && b.CursorVisible && IsKeyboardFocused)
            {
                int cx = Math.Min(b.CursorX, b.Cols - 1);
                dc.DrawRectangle(CursorBrush, null, new Rect(cx * _cellW, b.CursorY * _cellH, _cellW, _cellH));
            }
            else
            {
                // 스크롤백 보는 중이거나 포커스 없음: 커서를 그리지 않는다.
            }
        }
    }

    /// <summary>한 줄을 그린다: 배경 사각형 → 같은 스타일의 연속 반각 문자를 묶은 텍스트, 전각은 칸에 맞춰 개별 출력.</summary>
    private void DrawRow(DrawingContext dc, Cell[] row, double y, double dpi)
    {
        int cols = row.Length;
        for (int x = 0; x < cols; x++)
        {
            ResolveColors(row[x], out uint fg, out uint bg);
            if (bg != 0)
            {
                dc.DrawRectangle(BrushFor(bg), null, new Rect(x * _cellW, y, _cellW, _cellH));
            }
        }

        var run = new System.Text.StringBuilder();
        int runStart = 0;
        uint runFg = 0;
        CellAttr runAttr = CellAttr.None;
        for (int x = 0; x <= cols; x++)
        {
            bool end = x == cols;
            Cell c = end ? default : row[x];
            bool skip = !end && (c.Attr & CellAttr.WideTrail) != 0;
            if (skip)
            {
                continue;
            }
            bool wide = !end && c.Ch != 0 && ScreenBuffer.CharWidth(c.Ch) == 2;
            ResolveColors(c, out uint fg, out _);
            CellAttr style = c.Attr & (CellAttr.Bold | CellAttr.Underline | CellAttr.Italic);
            bool blank = end || c.Ch == 0 || c.Ch == ' ';

            bool continues = !end && !wide && !blank && run.Length > 0 && fg == runFg && style == runAttr && x == runStart + run.Length;
            if (!continues && run.Length > 0)
            {
                FlushRun(dc, run, runStart, y, runFg, runAttr, dpi);
                run.Clear();
            }
            if (end || blank)
            {
                continue;
            }
            if (wide)
            {
                DrawText(dc, char.ConvertFromUtf32(c.Ch), x, y, fg, style, dpi);
                continue;
            }
            if (run.Length == 0)
            {
                runStart = x;
                runFg = fg;
                runAttr = style;
            }
            run.Append(char.ConvertFromUtf32(c.Ch));
        }
    }

    private void FlushRun(DrawingContext dc, System.Text.StringBuilder run, int startCol, double y, uint fg, CellAttr style, double dpi)
    {
        DrawText(dc, run.ToString(), startCol, y, fg, style, dpi);
    }

    private void DrawText(DrawingContext dc, string text, int col, double y, uint fg, CellAttr style, double dpi)
    {
        Typeface face = (style & CellAttr.Bold) != 0 ? _bold : _normal;
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, FontSizeDip, BrushFor(fg), dpi);
        if ((style & CellAttr.Italic) != 0)
        {
            // 이탤릭 서체 별도 로드는 생략: 속성만 유지한다(고정폭 정렬 우선).
        }
        if ((style & CellAttr.Underline) != 0)
        {
            ft.SetTextDecorations(TextDecorations.Underline);
        }
        dc.DrawText(ft, new Point(col * _cellW, y));
    }

    /// <summary>기본색(0)과 반전 속성을 반영한 실제 전경/배경(0xFFRRGGBB, 배경 기본은 0).</summary>
    private static void ResolveColors(Cell c, out uint fg, out uint bg)
    {
        fg = c.Fg == 0 ? 0xFFCCCCCC : c.Fg;
        bg = c.Bg;
        if ((c.Attr & CellAttr.Inverse) != 0)
        {
            uint realBg = bg == 0 ? 0xFF0C0C0C : bg;
            bg = fg;
            fg = realBg;
        }
        else
        {
            // 반전 아님.
        }
    }

    private Brush BrushFor(uint argb)
    {
        if (!_brushes.TryGetValue(argb, out Brush? b))
        {
            b = Freeze(new SolidColorBrush(Color.FromRgb((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb)));
            _brushes[argb] = b;
        }
        return b;
    }

    #region 입력

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        TerminalSession? s = Session;
        if (s is null)
        {
            return;
        }
        int max;
        lock (s.Screen.SyncRoot)
        {
            max = s.Screen.ScrollbackCount;
        }
        int delta = e.Delta > 0 ? WheelLines : -WheelLines;
        _scrollOffset = Math.Clamp(_scrollOffset + delta, 0, max);
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
        string? seq = TranslateKey(key, mods);
        if (seq is null)
        {
            return; // 문자 입력은 TextInput으로.
        }
        if (seq.Length == 0)
        {
            // 붙여넣기 요청.
            PasteFromClipboard(s);
        }
        else
        {
            _scrollOffset = 0;
            s.SendInput(seq);
        }
        e.Handled = true;
    }

    /// <summary>키 → 전송 문자열. null=처리 안 함(텍스트 입력에 맡김), 빈 문자열=붙여넣기.</summary>
    private static string? TranslateKey(Key key, ModifierKeys mods)
    {
        bool ctrl = (mods & ModifierKeys.Control) != 0;
        bool shift = (mods & ModifierKeys.Shift) != 0;
        bool alt = (mods & ModifierKeys.Alt) != 0;

        if (ctrl && key == Key.V)
        {
            return string.Empty;
        }
        if (ctrl && !alt && key >= Key.A && key <= Key.Z)
        {
            return ((char)(key - Key.A + 1)).ToString(); // Ctrl+A=0x01 ... Ctrl+C=0x03(인터럽트)
        }
        return key switch
        {
            Key.Enter => "\r",
            Key.Back => "\x7f",
            Key.Tab => shift ? "\x1b[Z" : "\t",
            Key.Escape => "\x1b",
            Key.Up => "\x1b[A",
            Key.Down => "\x1b[B",
            Key.Right => ctrl ? "\x1b[1;5C" : "\x1b[C",
            Key.Left => ctrl ? "\x1b[1;5D" : "\x1b[D",
            Key.Home => "\x1b[H",
            Key.End => "\x1b[F",
            Key.Delete => "\x1b[3~",
            Key.Insert => "\x1b[2~",
            Key.PageUp => "\x1b[5~",
            Key.PageDown => "\x1b[6~",
            Key.F1 => "\x1bOP",
            Key.F2 => "\x1bOQ",
            Key.F3 => "\x1bOR",
            Key.F4 => "\x1bOS",
            _ => null,
        };
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

    private static void PasteFromClipboard(TerminalSession s)
    {
        if (Clipboard.ContainsText())
        {
            // 줄바꿈은 Enter(\r)로 통일한다.
            s.SendInput(Clipboard.GetText().Replace("\r\n", "\r").Replace('\n', '\r'));
        }
        else
        {
            // 텍스트가 아닌 클립보드: 무시.
        }
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
