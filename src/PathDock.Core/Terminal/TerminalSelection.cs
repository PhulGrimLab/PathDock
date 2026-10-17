using System.Text;

namespace PathDock.Core.Terminal;

/// <summary>
/// 터미널 마우스 선택 상태와 동작(WPF 비의존). Owner: TerminalControl, Lifetime: 컨트롤과 동일. UI 스레드 전용.
/// 위치는 (버퍼 줄 번호, 열)이다 - 줄 번호가 음수면 스크롤백(-1이 가장 최근)이라, 화면이 스크롤되어도 같은 글자를 가리킨다.
/// <see cref="ScreenBuffer"/>를 읽는 메서드 중 "호출 시 SyncRoot를 잡고 있어야 한다"고 적힌 것은 호출 측이 락을 잡는다.
/// </summary>
public sealed class TerminalSelection
{
    private long _scrolled;
    private (int Line, int Col) _anchor;
    private (int Line, int Col) _end;

    /// <summary>선택된 범위가 있는지(빈 범위는 선택이 아니다).</summary>
    public bool HasSelection { get; private set; }

    /// <summary>마우스 버튼을 누른 채 끌고 있는 중인지.</summary>
    public bool IsDragging { get; private set; }

    /// <summary>끌기를 시작한다: 이전 선택은 풀리고, 끌어서 범위가 생기기 전까지는 선택이 아니다.</summary>
    public void Begin((int Line, int Col) cell, long scrolledLines)
    {
        _anchor = _end = cell;
        _scrolled = scrolledLines;
        HasSelection = false;
        IsDragging = true;
    }

    /// <summary>끌기 도중 끝 위치를 옮긴다. 시작과 같은 칸이면 선택이 아니다.</summary>
    public void Extend((int Line, int Col) cell)
    {
        _end = cell;
        HasSelection = _end != _anchor;
    }

    /// <summary>마우스 버튼을 뗐다(선택은 유지).</summary>
    public void EndDrag() => IsDragging = false;

    /// <summary>선택을 푼다.</summary>
    public void Clear() => HasSelection = false;

    /// <summary>
    /// 한 줄 안에서 공백이 아닌 연속 글자를 단어로 선택한다. 공백이나 줄 밖이면 아무것도 바꾸지 않고 false.
    /// 내부에서 버퍼 락을 잡는다.
    /// </summary>
    public bool SelectWord(ScreenBuffer b, (int Line, int Col) cell)
    {
        lock (b.SyncRoot)
        {
            Cell[] row = b.GetLine(cell.Line);
            if (cell.Col < 0 || cell.Col >= row.Length || IsBlank(row[cell.Col]))
            {
                return false;
            }
            int from = cell.Col, to = cell.Col;
            while (from > 0 && !IsBlank(row[from - 1]))
            {
                from--;
            }
            while (to < row.Length - 1 && !IsBlank(row[to + 1]))
            {
                to++;
            }
            _anchor = (cell.Line, from);
            _end = (cell.Line, to);
            _scrolled = b.ScrolledLines;
            HasSelection = true;
            return true;
        }
    }

    private static bool IsBlank(Cell c) => c.Ch == 0 || c.Ch == ' ';

    /// <summary>
    /// 새 출력으로 줄이 위로 밀렸으면 선택도 같은 만큼 따라간다. 스크롤백 밖으로 밀려나면 선택을 푼다.
    /// 호출 시 <see cref="ScreenBuffer.SyncRoot"/>를 잡고 있어야 한다.
    /// </summary>
    public void FollowScroll(ScreenBuffer b)
    {
        if (!HasSelection || b.ScrolledLines == _scrolled)
        {
            return;
        }
        int shift = (int)Math.Min(b.ScrolledLines - _scrolled, int.MaxValue / 2);
        _anchor.Line -= shift;
        _end.Line -= shift;
        _scrolled = b.ScrolledLines;
        if (Math.Max(_anchor.Line, _end.Line) < -b.ScrollbackCount)
        {
            HasSelection = false;
            IsDragging = false;
        }
        else
        {
            _anchor.Line = Math.Max(_anchor.Line, -b.ScrollbackCount);
            _end.Line = Math.Max(_end.Line, -b.ScrollbackCount);
        }
    }

    /// <summary>시작/끝을 화면 순서(위→아래, 왼쪽→오른쪽)로 정렬해 돌려준다(끌기 방향과 무관).</summary>
    public ((int Line, int Col) Start, (int Line, int Col) Stop) Ordered()
    {
        return _end.Line < _anchor.Line || (_end.Line == _anchor.Line && _end.Col < _anchor.Col)
            ? (_end, _anchor)
            : (_anchor, _end);
    }

    /// <summary>
    /// 이 줄에서 선택된 열 범위(<paramref name="from"/>~<paramref name="to"/>, 양 끝 포함)를 돌려준다. 이 줄이 선택에 없으면 false.
    /// 첫 줄은 시작 열부터, 마지막 줄은 끝 열까지, 그 사이 줄은 전체(<paramref name="cols"/> 기준)다.
    /// </summary>
    public bool TryGetRowRange(int line, int cols, out int from, out int to)
    {
        from = to = 0;
        if (!HasSelection)
        {
            return false;
        }
        ((int Line, int Col) start, (int Line, int Col) stop) = Ordered();
        if (line < start.Line || line > stop.Line)
        {
            return false;
        }
        from = line == start.Line ? start.Col : 0;
        to = line == stop.Line ? stop.Col : cols - 1;
        return to >= from;
    }

    /// <summary>선택 범위의 글자를 줄 단위로 모은다(줄 끝 공백 제거, 줄바꿈은 CRLF). 호출 시 SyncRoot를 잡고 있어야 한다.</summary>
    public string ExtractText(ScreenBuffer screen)
    {
        ((int Line, int Col) a, (int Line, int Col) b) = Ordered();
        var sb = new StringBuilder();
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
        var text = new StringBuilder();
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
}
