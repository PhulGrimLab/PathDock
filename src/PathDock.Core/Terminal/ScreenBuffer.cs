using System.Text;

namespace PathDock.Core.Terminal;

/// <summary>셀 속성 플래그.</summary>
[Flags]
public enum CellAttr : byte
{
    None = 0,
    Bold = 1,
    Underline = 2,
    Inverse = 4,
    /// <summary>전각 문자의 두 번째 칸(그리지 않음).</summary>
    WideTrail = 8,
    Italic = 16,
}

/// <summary>화면 한 칸. Fg/Bg가 0이면 기본색, 아니면 0xFFRRGGBB.</summary>
public struct Cell
{
    public int Ch;
    public uint Fg;
    public uint Bg;
    public CellAttr Attr;
}

/// <summary>
/// 터미널 화면 격자 + 스크롤백. Owner: TerminalSession, Lifetime: 세션과 동일.
/// 스레드: ConPTY 읽기 스레드(쓰기)와 UI 스레드(읽기)가 공유하므로 모든 접근은 <see cref="SyncRoot"/>로 lock 한다.
/// UI는 <see cref="Version"/> 값이 바뀐 경우에만 다시 그린다.
/// </summary>
public sealed class ScreenBuffer
{
    private const int MaxScrollback = 2000;
    private const int TabWidth = 8;

    private Cell[][] _screen;
    private Cell[][]? _savedMain;
    private readonly List<Cell[]> _scrollback = new();
    private int _top;
    private int _bottom;
    private int _savedX;
    private int _savedY;
    private int _mainSavedX;
    private int _mainSavedY;

    public object SyncRoot { get; } = new();
    public int Cols { get; private set; }
    public int Rows { get; private set; }
    public int CursorX { get; set; }
    public int CursorY { get; set; }
    public bool CursorVisible { get; set; } = true;
    public bool IsAlternate => _savedMain is not null;
    public long Version { get; private set; }
    public int ScrollbackCount => _scrollback.Count;
    /// <summary>스크롤백으로 밀려난(또는 지워진) 줄의 누적 수. 선택 영역이 줄 번호 이동을 따라가는 데 쓴다.</summary>
    public long ScrolledLines { get; private set; }

    // 현재 쓰기 속성(SGR 상태).
    public uint CurFg { get; set; }
    public uint CurBg { get; set; }
    public CellAttr CurAttr { get; set; }

    public ScreenBuffer(int cols, int rows)
    {
        Cols = Math.Max(1, cols);
        Rows = Math.Max(1, rows);
        _screen = NewScreen(Cols, Rows);
        _top = 0;
        _bottom = Rows - 1;
    }

    private static Cell[][] NewScreen(int cols, int rows)
    {
        var s = new Cell[rows][];
        for (int i = 0; i < rows; i++)
        {
            s[i] = new Cell[cols];
        }
        return s;
    }

    private void Touch() => Version++;

    /// <summary>문자 폭(0=결합 문자, 2=전각, 그 외 1). 한글/CJK/이모지 위주의 간이 판정.</summary>
    public static int CharWidth(int cp)
    {
        if (cp is >= 0x0300 and <= 0x036F or >= 0x200B and <= 0x200F or >= 0xFE00 and <= 0xFE0F)
        {
            return 0;
        }
        if (cp is >= 0x1100 and <= 0x115F
            or >= 0x2E80 and <= 0x303E
            or >= 0x3041 and <= 0xA4CF
            or >= 0xAC00 and <= 0xD7A3
            or >= 0xF900 and <= 0xFAFF
            or >= 0xFE30 and <= 0xFE6F
            or >= 0xFF00 and <= 0xFF60
            or >= 0xFFE0 and <= 0xFFE6
            or >= 0x1F300 and <= 0x1F64F
            or >= 0x1F900 and <= 0x1F9FF
            or >= 0x20000 and <= 0x3FFFD)
        {
            return 2;
        }
        return 1;
    }

    private Cell Blank() => new() { Ch = 0, Fg = 0, Bg = CurBg, Attr = CellAttr.None };

    /// <summary>문자 하나를 커서 위치에 쓰고 커서를 전진시킨다(자동 줄바꿈 포함).</summary>
    public void Put(int cp)
    {
        int w = CharWidth(cp);
        if (w == 0)
        {
            // 결합 문자는 격자 모델에서 표현하지 못하므로 버린다(앞 글자는 유지).
            return;
        }
        if (CursorX + w > Cols)
        {
            CarriageReturn();
            LineFeed();
        }
        else
        {
            // 줄바꿈 불필요.
        }
        ref Cell c = ref _screen[CursorY][CursorX];
        c = new Cell { Ch = cp, Fg = CurFg, Bg = CurBg, Attr = CurAttr };
        if (w == 2 && CursorX + 1 < Cols)
        {
            _screen[CursorY][CursorX + 1] = new Cell { Ch = 0, Fg = CurFg, Bg = CurBg, Attr = CurAttr | CellAttr.WideTrail };
        }
        CursorX += w;
        Touch();
    }

    public void CarriageReturn()
    {
        CursorX = 0;
        Touch();
    }

    public void Backspace()
    {
        if (CursorX > 0)
        {
            CursorX = Math.Min(CursorX, Cols) - 1;
        }
        else
        {
            // 줄 처음에서는 이동 없음.
        }
        Touch();
    }

    public void Tab()
    {
        CursorX = Math.Min(Cols - 1, (CursorX / TabWidth + 1) * TabWidth);
        Touch();
    }

    public void LineFeed()
    {
        if (CursorY == _bottom)
        {
            ScrollUp(1);
        }
        else if (CursorY < Rows - 1)
        {
            CursorY++;
        }
        else
        {
            // 화면 맨 아래(스크롤 영역 밖)에서는 더 내려가지 않는다.
        }
        Touch();
    }

    public void ReverseIndex()
    {
        if (CursorY == _top)
        {
            ScrollDown(1);
        }
        else if (CursorY > 0)
        {
            CursorY--;
        }
        else
        {
            // 맨 위: 이동 없음.
        }
        Touch();
    }

    /// <summary>스크롤 영역을 n줄 위로 민다. 전체 화면(주 화면)이면 밀려난 줄을 스크롤백에 보관한다.</summary>
    public void ScrollUp(int n)
    {
        for (int k = 0; k < n; k++)
        {
            Cell[] removed = _screen[_top];
            if (_top == 0 && _bottom == Rows - 1 && !IsAlternate)
            {
                _scrollback.Add(removed);
                ScrolledLines++;
                if (_scrollback.Count > MaxScrollback)
                {
                    _scrollback.RemoveAt(0);
                }
            }
            for (int i = _top; i < _bottom; i++)
            {
                _screen[i] = _screen[i + 1];
            }
            _screen[_bottom] = BlankRow();
        }
        Touch();
    }

    public void ScrollDown(int n)
    {
        for (int k = 0; k < n; k++)
        {
            for (int i = _bottom; i > _top; i--)
            {
                _screen[i] = _screen[i - 1];
            }
            _screen[_top] = BlankRow();
        }
        Touch();
    }

    private Cell[] BlankRow()
    {
        var row = new Cell[Cols];
        if (CurBg != 0)
        {
            Array.Fill(row, Blank());
        }
        else
        {
            // 기본 배경이면 default(Cell)이 곧 빈 칸이다.
        }
        return row;
    }

    public void SetScrollRegion(int top, int bottom)
    {
        if (top >= 0 && bottom < Rows && top < bottom)
        {
            _top = top;
            _bottom = bottom;
        }
        else
        {
            _top = 0;
            _bottom = Rows - 1;
        }
        CursorX = 0;
        CursorY = 0;
        Touch();
    }

    public void MoveTo(int x, int y)
    {
        CursorX = Math.Clamp(x, 0, Cols - 1);
        CursorY = Math.Clamp(y, 0, Rows - 1);
        Touch();
    }

    public void MoveBy(int dx, int dy)
    {
        MoveTo(Math.Min(CursorX, Cols - 1) + dx, CursorY + dy);
    }

    public void SaveCursor()
    {
        _savedX = CursorX;
        _savedY = CursorY;
    }

    public void RestoreCursor()
    {
        MoveTo(_savedX, _savedY);
    }

    /// <summary>화면 지우기. mode 0=커서~끝, 1=처음~커서, 2/3=전체.</summary>
    public void EraseDisplay(int mode)
    {
        int x = Math.Min(CursorX, Cols - 1);
        switch (mode)
        {
            case 0:
                ClearRange(CursorY, x, Cols);
                for (int y = CursorY + 1; y < Rows; y++)
                {
                    ClearRange(y, 0, Cols);
                }
                break;
            case 1:
                for (int y = 0; y < CursorY; y++)
                {
                    ClearRange(y, 0, Cols);
                }
                ClearRange(CursorY, 0, x + 1);
                break;
            default:
                for (int y = 0; y < Rows; y++)
                {
                    ClearRange(y, 0, Cols);
                }
                if (mode == 3)
                {
                    ScrolledLines += _scrollback.Count;
                    _scrollback.Clear();
                }
                break;
        }
        Touch();
    }

    /// <summary>줄 지우기. mode 0=커서~끝, 1=처음~커서, 2=전체 줄.</summary>
    public void EraseLine(int mode)
    {
        int x = Math.Min(CursorX, Cols - 1);
        switch (mode)
        {
            case 0: ClearRange(CursorY, x, Cols); break;
            case 1: ClearRange(CursorY, 0, x + 1); break;
            default: ClearRange(CursorY, 0, Cols); break;
        }
        Touch();
    }

    public void EraseChars(int n)
    {
        int x = Math.Min(CursorX, Cols - 1);
        ClearRange(CursorY, x, Math.Min(Cols, x + n));
        Touch();
    }

    private void ClearRange(int row, int from, int to)
    {
        Cell blank = Blank();
        for (int x = from; x < to && x < Cols; x++)
        {
            _screen[row][x] = blank;
        }
    }

    public void InsertChars(int n)
    {
        int x = Math.Min(CursorX, Cols - 1);
        Cell[] row = _screen[CursorY];
        n = Math.Min(n, Cols - x);
        Array.Copy(row, x, row, x + n, Cols - x - n);
        ClearRange(CursorY, x, x + n);
        Touch();
    }

    public void DeleteChars(int n)
    {
        int x = Math.Min(CursorX, Cols - 1);
        Cell[] row = _screen[CursorY];
        n = Math.Min(n, Cols - x);
        Array.Copy(row, x + n, row, x, Cols - x - n);
        ClearRange(CursorY, Cols - n, Cols);
        Touch();
    }

    public void InsertLines(int n)
    {
        if (CursorY < _top || CursorY > _bottom)
        {
            return;
        }
        n = Math.Min(n, _bottom - CursorY + 1);
        for (int i = _bottom; i >= CursorY + n; i--)
        {
            _screen[i] = _screen[i - n];
        }
        for (int i = CursorY; i < CursorY + n; i++)
        {
            _screen[i] = BlankRow();
        }
        Touch();
    }

    public void DeleteLines(int n)
    {
        if (CursorY < _top || CursorY > _bottom)
        {
            return;
        }
        n = Math.Min(n, _bottom - CursorY + 1);
        for (int i = CursorY; i <= _bottom - n; i++)
        {
            _screen[i] = _screen[i + n];
        }
        for (int i = _bottom - n + 1; i <= _bottom; i++)
        {
            _screen[i] = BlankRow();
        }
        Touch();
    }

    /// <summary>대체 화면(vim 등) 진입/복귀.</summary>
    public void SetAlternate(bool on)
    {
        if (on == IsAlternate)
        {
            return;
        }
        if (on)
        {
            _savedMain = _screen;
            _mainSavedX = CursorX;
            _mainSavedY = CursorY;
            _screen = NewScreen(Cols, Rows);
            _top = 0;
            _bottom = Rows - 1;
        }
        else
        {
            _screen = _savedMain!;
            _savedMain = null;
            _top = 0;
            _bottom = Rows - 1;
            CursorX = _mainSavedX;
            CursorY = _mainSavedY;
        }
        Touch();
    }

    /// <summary>크기 변경. 줄바꿈 재배치는 하지 않고 겹치는 영역만 보존한다(커서가 밀리면 윗줄을 스크롤백으로).</summary>
    public void Resize(int cols, int rows)
    {
        cols = Math.Max(1, cols);
        rows = Math.Max(1, rows);
        if (cols == Cols && rows == Rows)
        {
            return;
        }

        // 줄 수가 줄어 커서가 화면 밖으로 나가면 윗줄을 스크롤백으로 올린다(주 화면만).
        if (!IsAlternate)
        {
            while (CursorY >= rows)
            {
                _scrollback.Add(_screen[0]);
                ScrolledLines++;
                _screen = _screen[1..];
                CursorY--;
                Rows--;
            }
        }

        var ns = NewScreen(cols, rows);
        for (int y = 0; y < Math.Min(rows, _screen.Length); y++)
        {
            Array.Copy(_screen[y], ns[y], Math.Min(cols, _screen[y].Length));
        }
        _screen = ns;
        if (_savedMain is not null)
        {
            _savedMain = ResizeCopy(_savedMain, cols, rows);
        }
        Cols = cols;
        Rows = rows;
        _top = 0;
        _bottom = rows - 1;
        CursorX = Math.Min(CursorX, cols - 1);
        CursorY = Math.Min(CursorY, rows - 1);
        Touch();
    }

    private static Cell[][] ResizeCopy(Cell[][] src, int cols, int rows)
    {
        var ns = NewScreen(cols, rows);
        for (int y = 0; y < Math.Min(rows, src.Length); y++)
        {
            Array.Copy(src[y], ns[y], Math.Min(cols, src[y].Length));
        }
        return ns;
    }

    /// <summary>
    /// 줄 번호로 한 줄을 가져온다. 음수는 스크롤백(-1이 가장 최근), 0 이상은 화면 행. 호출자는 SyncRoot를 잡고 있어야 한다.
    /// </summary>
    public Cell[] GetLine(int index)
    {
        if (index >= 0)
        {
            return _screen[Math.Min(index, Rows - 1)];
        }
        int sb = _scrollback.Count + index;
        return sb >= 0 ? _scrollback[sb] : _screen[0];
    }

    /// <summary>현재 화면(스크롤백 제외)을 줄 단위 텍스트로 만든다. UIA/테스트용.</summary>
    public string GetVisibleText()
    {
        lock (SyncRoot)
        {
            var sb = new StringBuilder();
            for (int y = 0; y < Rows; y++)
            {
                sb.Append(RowToString(_screen[y]).TrimEnd());
                sb.Append('\n');
            }
            return sb.ToString().TrimEnd('\n');
        }
    }

    /// <summary>스크롤백과 현재 화면 전체를 줄 단위 텍스트로 만든다(줄 끝 공백과 끝의 빈 줄은 제거). 파일 저장용.</summary>
    public string GetAllText()
    {
        lock (SyncRoot)
        {
            var sb = new StringBuilder();
            foreach (Cell[] row in _scrollback)
            {
                sb.Append(RowToString(row).TrimEnd()).Append('\n');
            }
            for (int y = 0; y < Rows; y++)
            {
                sb.Append(RowToString(_screen[y]).TrimEnd()).Append('\n');
            }
            return sb.ToString().TrimEnd('\n');
        }
    }

    private static string RowToString(Cell[] row)
    {
        var sb = new StringBuilder(row.Length);
        foreach (Cell c in row)
        {
            if ((c.Attr & CellAttr.WideTrail) != 0)
            {
                continue;
            }
            sb.Append(c.Ch == 0 ? ' ' : char.ConvertFromUtf32(c.Ch));
        }
        return sb.ToString();
    }
}
