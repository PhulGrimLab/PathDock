using System.Globalization;
using System.Windows;
using System.Windows.Media;
using PathDock.Core.Terminal;

namespace PathDock.App.Controls;

/// <summary>
/// 한 프레임을 그리는 데 필요한 화면 복사본. 화면 버퍼를 읽는 동안만 락을 잡고 이 복사본을 만든 뒤, 락 밖에서 그린다 -
/// 그리는 동안 셸 출력 파서(읽기 스레드)가 기다리지 않게 하기 위해서다. 프레임 사이에 행 배열을 재사용해 할당을 줄인다.
/// </summary>
internal sealed class TerminalFrame
{
    public Cell[]?[] Rows { get; private set; } = Array.Empty<Cell[]?>();

    /// <summary>각 행의 선택된 열 범위(양 끝 포함). <see cref="SelFrom"/>이 -1이면 선택 없음.</summary>
    public int[] SelFrom { get; private set; } = Array.Empty<int>();

    public int[] SelTo { get; private set; } = Array.Empty<int>();
    public bool ShowCursor { get; set; }
    public int CursorX { get; set; }
    public int CursorY { get; set; }

    /// <summary>행 수에 맞춘다(같으면 그대로).</summary>
    public void Prepare(int rows)
    {
        if (Rows.Length != rows)
        {
            Rows = new Cell[]?[rows];
            SelFrom = new int[rows];
            SelTo = new int[rows];
        }
    }

    /// <summary>y번째 행에 원본 행의 내용을 복사한다(길이가 다르면 다시 만든다 - 스크롤백 행은 예전 폭일 수 있다).</summary>
    public void SetRow(int y, Cell[] source)
    {
        Cell[]? row = Rows[y];
        if (row is null || row.Length != source.Length)
        {
            row = new Cell[source.Length];
            Rows[y] = row;
        }
        Array.Copy(source, row, source.Length);
    }
}

/// <summary>
/// 터미널 화면을 그린다: 글꼴/셀 크기, 색 구성(테마 색으로 바꿔 끼우기), 브러시 캐시, 행 그리기(배경 → 같은 스타일의 연속 글자 묶음).
/// Owner: TerminalControl, Lifetime: 컨트롤과 동일. UI 스레드 전용.
/// </summary>
internal sealed class TerminalRenderer
{
    /// <summary>색마다 브러시를 캐시하는데, 트루컬러 출력은 색 종류가 사실상 무한이라 상한을 넘으면 비운다(메모리가 계속 늘지 않도록).</summary>
    private const int MaxCachedBrushes = 4096;

    private static readonly Brush SelectionBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x70, 0x30, 0x80, 0xFF)));
    private static readonly FontFamily Family = new("Cascadia Mono, Consolas, D2Coding, Malgun Gothic");

    private readonly Dictionary<uint, Brush> _brushes = new();
    private readonly Typeface _normal = new(Family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private readonly Typeface _bold = new(Family, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private Brush _background = Freeze(new SolidColorBrush(Color.FromRgb(0x0C, 0x0C, 0x0C)));
    private Brush _cursorBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)));
    private uint _defaultFg = 0xFFCCCCCC;
    private uint _defaultBg = 0xFF0C0C0C;
    // 기본 팔레트(Campbell) ANSI 색 → 현재 테마 색. 파서가 셀에 RGB로 저장하므로 그릴 때 바꿔 끼운다(출력이 이미 있어도 즉시 반영).
    private Dictionary<uint, uint> _ansiMap = new();

    public double CellWidth { get; private set; } = 8;
    public double CellHeight { get; private set; } = 16;

    /// <summary>캐시된 브러시 수(시험/진단용).</summary>
    public int CachedBrushCount => _brushes.Count;

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }

    /// <summary>색 구성을 적용한다(기본 글자/배경, 커서, ANSI 색 바꿔 끼우기).</summary>
    public void ApplyColorScheme(TerminalColorScheme s)
    {
        _defaultFg = s.Foreground;
        _defaultBg = s.Background;
        _background = BrushFor(s.Background);
        _cursorBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xB0, (byte)(s.Foreground >> 16), (byte)(s.Foreground >> 8), (byte)s.Foreground)));
        _ansiMap = new Dictionary<uint, uint>();
        for (int i = 0; i < 16; i++)
        {
            _ansiMap[Palette.Get(i)] = s.Ansi[i];
        }
    }

    /// <summary>"M" 한 글자로 셀 크기를 잰다(고정폭 글꼴 가정).</summary>
    public void MeasureCell(double fontSize, double dpi)
    {
        var ft = new FormattedText("M", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _normal, fontSize, Brushes.White, dpi);
        CellWidth = Math.Max(1, ft.WidthIncludingTrailingWhitespace);
        CellHeight = Math.Max(1, Math.Ceiling(ft.Height));
    }

    public void DrawBackground(DrawingContext dc, double width, double height) =>
        dc.DrawRectangle(_background, null, new Rect(0, 0, width, height));

    /// <summary>복사해 둔 화면을 그린다: 각 행과 그 위의 선택 표시, 마지막에 커서.</summary>
    public void Draw(DrawingContext dc, TerminalFrame frame, double fontSize, double dpi)
    {
        for (int y = 0; y < frame.Rows.Length; y++)
        {
            DrawRow(dc, frame.Rows[y]!, y * CellHeight, fontSize, dpi);
            if (frame.SelFrom[y] >= 0)
            {
                dc.DrawRectangle(SelectionBrush, null, new Rect(frame.SelFrom[y] * CellWidth, y * CellHeight, (frame.SelTo[y] - frame.SelFrom[y] + 1) * CellWidth, CellHeight));
            }
        }
        if (frame.ShowCursor)
        {
            dc.DrawRectangle(_cursorBrush, null, new Rect(frame.CursorX * CellWidth, frame.CursorY * CellHeight, CellWidth, CellHeight));
        }
    }

    /// <summary>한 줄을 그린다: 배경 사각형 → 같은 스타일의 연속 반각 문자를 묶은 텍스트, 전각은 칸에 맞춰 개별 출력.</summary>
    private void DrawRow(DrawingContext dc, Cell[] row, double y, double fontSize, double dpi)
    {
        int cols = row.Length;
        for (int x = 0; x < cols; x++)
        {
            ResolveColors(row[x], out _, out uint bg);
            if (bg != 0)
            {
                dc.DrawRectangle(BrushFor(bg), null, new Rect(x * CellWidth, y, CellWidth, CellHeight));
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
            if (!end && (c.Attr & CellAttr.WideTrail) != 0)
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
                DrawText(dc, run.ToString(), runStart, y, runFg, runAttr, fontSize, dpi);
                run.Clear();
            }
            if (end || blank)
            {
                continue;
            }
            if (wide)
            {
                DrawText(dc, char.ConvertFromUtf32(c.Ch), x, y, fg, style, fontSize, dpi);
                continue;
            }
            if (run.Length == 0)
            {
                runStart = x;
                runFg = fg;
                runAttr = style;
            }
            // 기본 평면(BMP)의 글자는 문자열을 만들지 않고 바로 붙인다(화면이 바뀔 때마다 수천 개의 임시 문자열이 생겼다).
            if (c.Ch < 0x10000)
            {
                run.Append((char)c.Ch);
            }
            else
            {
                run.Append(char.ConvertFromUtf32(c.Ch));
            }
        }
    }

    private void DrawText(DrawingContext dc, string text, int col, double y, uint fg, CellAttr style, double fontSize, double dpi)
    {
        Typeface face = (style & CellAttr.Bold) != 0 ? _bold : _normal;
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, fontSize, BrushFor(fg), dpi);
        // 이탤릭 서체는 따로 불러오지 않는다: 속성만 유지하고 고정폭 정렬을 우선한다.
        if ((style & CellAttr.Underline) != 0)
        {
            ft.SetTextDecorations(TextDecorations.Underline);
        }
        dc.DrawText(ft, new Point(col * CellWidth, y));
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
    }

    private uint Themed(uint argb) => _ansiMap.TryGetValue(argb, out uint mapped) ? mapped : argb;

    private Brush BrushFor(uint argb)
    {
        if (!_brushes.TryGetValue(argb, out Brush? b))
        {
            if (_brushes.Count >= MaxCachedBrushes)
            {
                _brushes.Clear();
            }
            b = Freeze(new SolidColorBrush(Color.FromRgb((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb)));
            _brushes[argb] = b;
        }
        return b;
    }
}
