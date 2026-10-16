using System.Globalization;
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
/// 터미널 화면 커스텀 컨트롤. <see cref="TerminalSession"/>의 화면 버퍼를 그리고 키보드 입력을 VT 시퀀스로 바꿔 보낸다.
/// Owner: MainWindow(XAML). Lifetime: 창과 동일. 스레드: UI 스레드 전용 - 버퍼 읽기는 SyncRoot로 보호,
/// 33ms 타이머가 버퍼 Version 변화를 감지했을 때만 다시 그린다.
/// 크기가 바뀌면 열/행을 계산해 세션을 리사이즈한다. UI Automation에는 화면 텍스트를 ValuePattern(읽기 전용)으로 노출한다.
/// </summary>
public sealed class TerminalControl : FrameworkElement
{
    private const int RefreshIntervalMs = 33;
    private const int WheelLines = 3;
    private const int MinCols = 20;
    private const int MinRows = 3;
    private static readonly Brush SelectionBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x70, 0x30, 0x80, 0xFF)));

    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session), typeof(TerminalSession), typeof(TerminalControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((TerminalControl)d).OnSessionChanged()));

    public static readonly DependencyProperty ColorSchemeProperty = DependencyProperty.Register(
        nameof(ColorScheme), typeof(TerminalColorScheme), typeof(TerminalControl),
        new FrameworkPropertyMetadata(TerminalThemes.All[0], FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((TerminalControl)d).OnColorSchemeChanged()));

    public static readonly DependencyProperty TerminalFontSizeProperty = DependencyProperty.Register(
        nameof(TerminalFontSize), typeof(double), typeof(TerminalControl),
        new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((TerminalControl)d).OnFontSizeChanged()));

    private readonly Dictionary<uint, Brush> _brushes = new();
    private readonly DispatcherTimer _timer;
    private readonly Typeface _normal = new(new FontFamily("Cascadia Mono, Consolas, D2Coding, Malgun Gothic"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private readonly Typeface _bold = new(new FontFamily("Cascadia Mono, Consolas, D2Coding, Malgun Gothic"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private double _cellW = 8;
    private double _cellH = 16;
    private long _renderedVersion = -1;
    private int _scrollOffset;
    private Brush _background = Freeze(new SolidColorBrush(Color.FromRgb(0x0C, 0x0C, 0x0C)));
    private Brush _cursorBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)));
    private uint _defaultFg = 0xFFCCCCCC;
    private uint _defaultBg = 0xFF0C0C0C;
    // 기본 팔레트(Campbell) ANSI 색 → 현재 테마 색. 파서가 셀에 RGB로 저장하므로 그릴 때 바꿔 끼운다(출력이 이미 있어도 즉시 반영).
    private Dictionary<uint, uint> _ansiMap = new();

    // 마우스 선택 범위: 버퍼 줄 번호(음수=스크롤백)와 열. 새 출력이 오면(Version 변화) 해제한다.
    private bool _hasSelection;
    private bool _dragging;
    private long _selScrolled;
    private (int Line, int Col) _selAnchor;
    private (int Line, int Col) _selEnd;

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

    public TerminalControl()    {
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

    private void OnColorSchemeChanged()
    {
        TerminalColorScheme s = ColorScheme;
        _defaultFg = s.Foreground;
        _defaultBg = s.Background;
        _background = BrushFor(s.Background);
        _cursorBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xB0, (byte)(s.Foreground >> 16), (byte)(s.Foreground >> 8), (byte)s.Foreground)));
        _ansiMap = new Dictionary<uint, uint>();
        for (int i = 0; i < 16; i++)
        {
            _ansiMap[Palette.Get(i)] = s.Ansi[i];
        }
        _renderedVersion = -1;
        InvalidateVisual();
    }

    private void OnFontSizeChanged()
    {
        MeasureCell();
        _renderedVersion = -1;
        ApplySize();
    }

    /// <summary>"M" 한 글자로 셀 크기를 잰다(고정폭 폰트 가정).</summary>
    private void MeasureCell()
    {
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var ft = new FormattedText("M", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _normal, TerminalFontSize, Brushes.White, dpi);
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
        dc.DrawRectangle(_background, null, new Rect(0, 0, w, h));
        TerminalSession? s = Session;
        if (s is null)
        {
            return;
        }

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        ScreenBuffer b = s.Screen;
        lock (b.SyncRoot)
        {
            FollowSelectionWithScroll(b);
            _renderedVersion = b.Version;
            _scrollOffset = Math.Min(_scrollOffset, b.ScrollbackCount);
            (int Line, int Col) selStart, selStop;
            (selStart, selStop) = OrderedSelection();
            for (int y = 0; y < b.Rows; y++)
            {
                int line = y - _scrollOffset;
                DrawRow(dc, b.GetLine(line), y * _cellH, dpi);
                if (_hasSelection && line >= selStart.Line && line <= selStop.Line)
                {
                    int from = line == selStart.Line ? selStart.Col : 0;
                    int to = line == selStop.Line ? selStop.Col : b.Cols - 1;
                    if (to >= from)
                    {
                        dc.DrawRectangle(SelectionBrush, null, new Rect(from * _cellW, y * _cellH, (to - from + 1) * _cellW, _cellH));
                    }
                }
            }
            if (_scrollOffset == 0 && b.CursorVisible && IsKeyboardFocused)
            {
                int cx = Math.Min(b.CursorX, b.Cols - 1);
                dc.DrawRectangle(_cursorBrush, null, new Rect(cx * _cellW, b.CursorY * _cellH, _cellW, _cellH));
            }
            else
            {
                // 스크롤백 보는 중이거나 포커스 없음: 커서를 그리지 않는다.
            }
        }
    }

    /// <summary>
    /// 새 출력으로 줄이 위로 밀렸으면 선택도 같은 만큼 따라간다. 스크롤백 밖으로 밀려나면 선택을 푼다.
    /// 호출 시 <see cref="ScreenBuffer.SyncRoot"/>를 잡고 있어야 한다.
    /// </summary>
    private void FollowSelectionWithScroll(ScreenBuffer b)
    {
        if (!_hasSelection || b.ScrolledLines == _selScrolled)
        {
            return;
        }
        int shift = (int)Math.Min(b.ScrolledLines - _selScrolled, int.MaxValue / 2);
        _selAnchor.Line -= shift;
        _selEnd.Line -= shift;
        _selScrolled = b.ScrolledLines;
        if (Math.Max(_selAnchor.Line, _selEnd.Line) < -b.ScrollbackCount)
        {
            _hasSelection = false;
            _dragging = false;
        }
        else
        {
            _selAnchor.Line = Math.Max(_selAnchor.Line, -b.ScrollbackCount);
            _selEnd.Line = Math.Max(_selEnd.Line, -b.ScrollbackCount);
        }
    }

    /// <summary>선택의 시작/끝을 화면 순서(위→아래, 왼쪽→오른쪽)로 정렬해 돌려준다(드래그 방향과 무관).</summary>
    private ((int Line, int Col) Start, (int Line, int Col) Stop) OrderedSelection()
    {
        return _selEnd.Line < _selAnchor.Line || (_selEnd.Line == _selAnchor.Line && _selEnd.Col < _selAnchor.Col)
            ? (_selEnd, _selAnchor)
            : (_selAnchor, _selEnd);
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
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, TerminalFontSize, BrushFor(fg), dpi);
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
    private void ResolveColors(Cell c, out uint fg, out uint bg)
    {
        fg = c.Fg == 0 ? _defaultFg : Themed(c.Fg);
        bg = c.Bg == 0 ? 0 : Themed(c.Bg);
        if ((c.Attr & CellAttr.Inverse) != 0)
        {
            uint realBg = bg == 0 ? _defaultBg : bg;
            bg = fg;
            fg = realBg;
        }
        else
        {
            // 반전 아님.
        }
    }

    private uint Themed(uint argb) => _ansiMap.TryGetValue(argb, out uint mapped) ? mapped : argb;

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
                SelectWord(cell);
                return;
            }
            _selAnchor = _selEnd = cell;
            _selScrolled = ScrolledNow();
            _hasSelection = false;
            _dragging = true;
            CaptureMouse();
            InvalidateVisual();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging)
        {
            return;
        }
        Point p = e.GetPosition(this);
        TerminalSession? s = Session;
        if (s is not null && (p.Y < 0 || p.Y > ActualHeight))
        {
            // 위/아래 바깥으로 끌면 스크롤백을 한 줄씩 따라간다.
            int max;
            lock (s.Screen.SyncRoot)
            {
                max = s.Screen.ScrollbackCount;
            }
            _scrollOffset = Math.Clamp(_scrollOffset + (p.Y < 0 ? 1 : -1), 0, max);
        }
        _selEnd = CellAt(p);
        _hasSelection = _selEnd != _selAnchor;
        InvalidateVisual();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragging && e.ChangedButton == MouseButton.Left)
        {
            _dragging = false;
            ReleaseMouseCapture();
        }
    }

    /// <summary>화면 좌표 → (버퍼 줄 번호, 열). 범위를 벗어나면 가장자리로 맞춘다.</summary>
    private (int Line, int Col) CellAt(Point p)
    {
        TerminalSession? s = Session;
        int cols = s?.Screen.Cols ?? 1, rows = s?.Screen.Rows ?? 1;
        int col = Math.Clamp((int)(p.X / _cellW), 0, cols - 1);
        int row = Math.Clamp((int)(p.Y / _cellH), 0, rows - 1);
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

    /// <summary>더블클릭: 공백이 아닌 연속 글자를 단어로 선택한다.</summary>
    private void SelectWord((int Line, int Col) cell)
    {
        TerminalSession? s = Session;
        if (s is null)
        {
            return;
        }
        lock (s.Screen.SyncRoot)
        {
            Cell[] row = s.Screen.GetLine(cell.Line);
            static bool Blank(Cell c) => c.Ch == 0 || c.Ch == ' ';
            if (cell.Col >= row.Length || Blank(row[cell.Col]))
            {
                return;
            }
            int a = cell.Col, b = cell.Col;
            while (a > 0 && !Blank(row[a - 1]))
            {
                a--;
            }
            while (b < row.Length - 1 && !Blank(row[b + 1]))
            {
                b++;
            }
            _selAnchor = (cell.Line, a);
            _selEnd = (cell.Line, b);
            _selScrolled = s.Screen.ScrolledLines;
            _hasSelection = true;
        }
        InvalidateVisual();
    }

    /// <summary>선택한 범위를 클립보드에 복사하고 선택을 푼다. 선택이 없으면 false.</summary>
    private bool CopySelection()
    {
        TerminalSession? s = Session;
        if (s is null || !_hasSelection)
        {
            return false;
        }
        string selected;
        lock (s.Screen.SyncRoot)
        {
            selected = ExtractSelectedText(s.Screen);
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
        _hasSelection = false;
        InvalidateVisual();
        return true;
    }

    /// <summary>선택 범위의 글자를 줄 단위로 모은다(줄 끝 공백 제거, 줄바꿈은 CRLF). 호출 시 SyncRoot를 잡고 있어야 한다.</summary>
    private string ExtractSelectedText(ScreenBuffer screen)
    {
        (int Line, int Col) a, b;
        (a, b) = OrderedSelection();
        var sb = new System.Text.StringBuilder();
        for (int line = a.Line; line <= b.Line; line++)
        {
            Cell[] row = screen.GetLine(line);
            int from = line == a.Line ? a.Col : 0;
            int to = Math.Min(line == b.Line ? b.Col : row.Length - 1, row.Length - 1);
            if (line > a.Line)
            {
                sb.Append("\r\n");
            }
            sb.Append(RowText(row, from, to).TrimEnd());
        }
        return sb.ToString();
    }

    /// <summary>한 줄에서 from~to 칸의 글자(전각 문자의 뒤쪽 칸은 건너뛰고, 빈 칸은 공백).</summary>
    private static string RowText(Cell[] row, int from, int to)
    {
        var text = new System.Text.StringBuilder();
        for (int x = from; x <= to; x++)
        {
            Cell c = row[x];
            if ((c.Attr & CellAttr.WideTrail) != 0)
            {
                continue;
            }
            text.Append(c.Ch == 0 ? " " : char.ConvertFromUtf32(c.Ch));
        }
        return text.ToString();
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
        bool copyChord = key == Key.C && (mods & ModifierKeys.Control) != 0 && (mods & ModifierKeys.Alt) == 0;
        if (copyChord && (_hasSelection || (mods & ModifierKeys.Shift) != 0))
        {
            // 선택이 있으면 Ctrl+C=복사, 선택이 없으면 Ctrl+C는 인터럽트로 전달. Ctrl+Shift+C는 항상 복사(없으면 무시).
            CopySelection();
            e.Handled = true;
            return;
        }
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

    private void PasteFromClipboard(TerminalSession s)
    {
        if (Clipboard.ContainsText())
        {
            // 줄바꿈은 Enter(\r)로 통일하고 눈에 안 보이는 제어 문자는 제거한다(PasteGuard).
            string text = PasteGuard.Normalize(Clipboard.GetText());
            if (text.Length == 0)
            {
                return;
            }
            if (PasteGuard.RunsCommands(text))
            {
                // 줄바꿈(Enter)이 하나라도 있으면 붙여넣는 즉시 명령이 실행된다(끝의 줄바꿈 하나뿐인 한 줄도 마찬가지) - 내용을 보여주고 확인한다.
                int lines = PasteGuard.LineCount(text);
                string preview = PasteGuard.Preview(text, 5, 80, out int hidden);
                string more = hidden > 0 ? Loc.T($"\n… 외 {hidden}줄", $"\n… and {hidden} more line(s)") : string.Empty;
                string message = Loc.T(
                    $"{lines}줄을 터미널에 붙여넣습니다. 줄바꿈마다 명령이 바로 실행됩니다.\n\n{preview}{more}\n\n계속할까요?",
                    $"{lines} line(s) will be pasted into the terminal. Each line break runs a command immediately.\n\n{preview}{more}\n\nContinue?");
                if (!PathDock.App.Views.ConfirmDialog.Ask(Window.GetWindow(this), Loc.T("붙여넣기 확인", "Confirm paste"), message))
                {
                    return;
                }
            }
            s.SendInput(text);
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
