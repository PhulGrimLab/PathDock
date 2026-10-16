using System.Text;

namespace PathDock.Core.Terminal;

/// <summary>
/// VT/ANSI 시퀀스 해석기. 바이트(UTF-8)를 받아 <see cref="ScreenBuffer"/>를 갱신한다.
/// Owner: TerminalSession, Lifetime: 세션과 동일. 스레드: <see cref="Feed"/>는 ConPTY 읽기 스레드 한 곳에서만 호출하며,
/// 내부에서 버퍼의 SyncRoot를 잡는다. 이벤트도 읽기 스레드에서 발생하므로 구독자가 스레드를 옮겨야 한다.
/// 지원 범위는 doc/02 6절 참고. 모르는 시퀀스는 조용히 무시한다.
/// </summary>
public sealed class VtParser
{
    private enum State { Ground, Escape, EscapeIntermediate, Csi, Osc, StringIgnore }

    private const int MaxOscLength = 4096;
    private const int MaxParams = 32;
    private const int DefaultParam = 0;

    private readonly ScreenBuffer _b;
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly List<int> _params = new();
    private readonly StringBuilder _osc = new();
    private State _state = State.Ground;
    private char _highSurrogate;
    private int _curParam = -1;
    private char _csiPrefix;

    /// <summary>터미널에 되돌려 보낼 응답(커서 위치 보고 등). ConPTY 입력으로 써야 한다.</summary>
    public Action<string>? Reply { get; set; }
    /// <summary>OSC 9;9로 보고된 현재 경로.</summary>
    public event Action<string>? CwdReported;
    /// <summary>OSC 133;A - 셸이 프롬프트를 그렸다(입력 대기).</summary>
    public event Action? PromptShown;
    public event Action<string>? TitleChanged;

    /// <summary>
    /// 셸 통합 마커에 요구하는 토큰(null이면 토큰 없는 "PD:번호:경로" 형식). 토큰이 다른 제목은 일반 제목으로 취급해
    /// 프로그램 출력으로 프롬프트/경로 신호를 위조하지 못하게 한다.
    /// </summary>
    public string? MarkerToken { get; set; }

    public VtParser(ScreenBuffer buffer)
    {
        _b = buffer;
    }

    /// <summary>ConPTY 출력 바이트를 처리한다. 멀티바이트/시퀀스가 청크 경계에서 잘려도 상태를 유지한다.</summary>
    public void Feed(byte[] data, int count)
    {
        var chars = new char[Encoding.UTF8.GetMaxCharCount(count)];
        int n = _decoder.GetChars(data, 0, count, chars, 0, flush: false);
        lock (_b.SyncRoot)
        {
            for (int i = 0; i < n; i++)
            {
                Process(chars[i]);
            }
        }
    }

    private void Process(char c)
    {
        switch (_state)
        {
            case State.Ground:
                Ground(c);
                break;
            case State.Escape:
                EscapeState(c);
                break;
            case State.EscapeIntermediate:
                EscapeIntermediateState(c);
                break;
            case State.Csi:
                CsiState(c);
                break;
            case State.Osc:
                OscState(c);
                break;
            case State.StringIgnore:
                StringIgnoreState(c);
                break;
        }
    }

    /// <summary>문자 집합 지정(ESC ( B 등): 마지막 한 글자를 소비하고 끝.</summary>
    private void EscapeIntermediateState(char c)
    {
        if (c >= 0x30)
        {
            _state = State.Ground;
        }
    }

    /// <summary>DCS/APC 등 문자열 시퀀스: BEL이나 ST(ESC \)를 만날 때까지 내용을 버린다.</summary>
    private void StringIgnoreState(char c)
    {
        if (c == '\a')
        {
            _state = State.Ground;
        }
        else if (c == '\x1b')
        {
            _state = State.Escape;
        }
        else
        {
            // 내용은 버린다.
        }
    }

    private void Ground(char c)
    {
        if (c == '\x1b')
        {
            _state = State.Escape;
        }
        else if (c < 0x20)
        {
            Execute(c);
        }
        else if (c == 0x7F)
        {
            // DEL 무시.
        }
        else if (char.IsHighSurrogate(c))
        {
            _highSurrogate = c;
        }
        else if (char.IsLowSurrogate(c))
        {
            if (_highSurrogate != 0)
            {
                _b.Put(char.ConvertToUtf32(_highSurrogate, c));
                _highSurrogate = '\0';
            }
            else
            {
                // 짝 없는 low surrogate는 버린다.
            }
        }
        else
        {
            _b.Put(c);
        }
    }

    private void Execute(char c)
    {
        switch (c)
        {
            case '\r': _b.CarriageReturn(); break;
            case '\n':
            case '\v':
            case '\f': _b.LineFeed(); break;
            case '\b': _b.Backspace(); break;
            case '\t': _b.Tab(); break;
            default: break; // BEL 등은 무시.
        }
    }

    /// <summary>ESC 바로 다음 글자를 처리한다: 다른 상태로 들어가거나, 한 글자짜리 명령을 실행하거나, 제어 문자면 실행한다.</summary>
    private void EscapeState(char c)
    {
        _state = State.Ground;
        if (TryEnterState(c) || TryEscapeCommand(c))
        {
            return;
        }
        if (c < 0x20)
        {
            Execute(c);
        }
        else
        {
            // '=' '>' '\\'(ST) 등: 의미 없음.
        }
    }

    /// <summary>ESC 뒤 글자가 CSI/OSC/문자열/중간 문자 시퀀스의 시작이면 그 상태로 들어간다.</summary>
    private bool TryEnterState(char c)
    {
        switch (c)
        {
            case '[':
                _params.Clear();
                _curParam = -1;
                _csiPrefix = '\0';
                _state = State.Csi;
                return true;
            case ']':
                _osc.Clear();
                _state = State.Osc;
                return true;
            case 'P':
            case 'X':
            case '^':
            case '_':
                _state = State.StringIgnore;
                return true;
            case '(':
            case ')':
            case '*':
            case '+':
            case '#':
            case '%':
            case ' ':
                _state = State.EscapeIntermediate;
                return true;
            case '\x1b':
                _state = State.Escape;
                return true;
            default:
                return false;
        }
    }

    /// <summary>한 글자짜리 ESC 명령(인덱스, 커서 저장/복원, 전체 초기화).</summary>
    private bool TryEscapeCommand(char c)
    {
        switch (c)
        {
            case 'M': _b.ReverseIndex(); return true;
            case 'D': _b.LineFeed(); return true;
            case 'E': _b.CarriageReturn(); _b.LineFeed(); return true;
            case '7': _b.SaveCursor(); return true;
            case '8': _b.RestoreCursor(); return true;
            case 'c': FullReset(); return true;
            default: return false;
        }
    }

    /// <summary>ESC c: 화면을 지우고 커서와 글자 속성을 처음 상태로.</summary>
    private void FullReset()
    {
        _b.EraseDisplay(2);
        _b.MoveTo(0, 0);
        ResetSgr();
    }

    /// <summary>CSI 안의 글자 하나: 숫자는 파라미터, ; :는 구분, ? > &lt; =는 접두, 0x40~0x7E는 끝 글자.</summary>
    private void CsiState(char c)
    {
        switch (c)
        {
            case >= '0' and <= '9':
                _curParam = Math.Min((_curParam < 0 ? 0 : _curParam) * 10 + (c - '0'), 65535);
                break;
            case ';' or ':':
                AddParam(_curParam < 0 ? DefaultParam : _curParam);
                _curParam = -1;
                break;
            case '?' or '>' or '<' or '=':
                _csiPrefix = c;
                break;
            case >= (char)0x20 and <= (char)0x2F:
                // 중간 문자(예: ESC [ 0 SP q): 값은 쓰지 않는다.
                break;
            case >= (char)0x40 and <= (char)0x7E:
                FinishCsi(c);
                break;
            case '\x1b':
                _state = State.Escape;
                break;
            case < (char)0x20:
                Execute(c);
                break;
            default:
                _state = State.Ground;
                break;
        }
    }

    /// <summary>CSI의 끝 글자를 받았다: 마지막 파라미터를 확정하고 명령을 실행한다.</summary>
    private void FinishCsi(char final)
    {
        if (_curParam >= 0 || _params.Count > 0)
        {
            AddParam(_curParam < 0 ? DefaultParam : _curParam);
        }
        DispatchCsi(final);
        _state = State.Ground;
    }

    /// <summary>파라미터를 추가한다. 개수에 상한을 둔다: ';'만 수백만 개 이어진 시퀀스로 메모리를 쌓지 못하게(초과분은 버린다).</summary>
    private void AddParam(int value)
    {
        if (_params.Count < MaxParams)
        {
            _params.Add(value);
        }
    }

    private int P(int index, int def)
    {
        return index < _params.Count && _params[index] != 0 ? _params[index] : def;
    }

    /// <summary>CSI 명령을 실행한다. 접두(? 등)가 있는 시퀀스는 모드 설정(h/l)과 장치 속성 질의(c)만 처리하고 나머지는 무시한다.</summary>
    private void DispatchCsi(char final)
    {
        if (_csiPrefix is '?' && final is 'h' or 'l')
        {
            bool on = final == 'h';
            foreach (int mode in _params)
            {
                ApplyPrivateMode(mode, on);
            }
            return;
        }
        if (_csiPrefix != '\0' && final != 'c')
        {
            return; // 그 밖의 사설 시퀀스는 무시.
        }
        _ = DispatchCursorCsi(final) || DispatchEditCsi(final) || DispatchScrollCsi(final) || DispatchStatusCsi(final);
    }

    /// <summary>커서 이동과 저장/복원(A B C D E F G d H f s u). 처리했으면 true.</summary>
    private bool DispatchCursorCsi(char final)
    {
        switch (final)
        {
            case 'A': _b.MoveBy(0, -P(0, 1)); return true;
            case 'B': _b.MoveBy(0, P(0, 1)); return true;
            case 'C': _b.MoveBy(P(0, 1), 0); return true;
            case 'D': _b.MoveBy(-P(0, 1), 0); return true;
            case 'E': _b.MoveTo(0, _b.CursorY + P(0, 1)); return true;
            case 'F': _b.MoveTo(0, _b.CursorY - P(0, 1)); return true;
            case 'G': _b.MoveTo(P(0, 1) - 1, _b.CursorY); return true;
            case 'd': _b.MoveTo(_b.CursorX, P(0, 1) - 1); return true;
            case 'H':
            case 'f': _b.MoveTo(P(1, 1) - 1, P(0, 1) - 1); return true;
            case 's': _b.SaveCursor(); return true;
            case 'u': _b.RestoreCursor(); return true;
            default: return false;
        }
    }

    /// <summary>지우기와 삽입/삭제(J K L M P @ X). 처리했으면 true.</summary>
    private bool DispatchEditCsi(char final)
    {
        switch (final)
        {
            case 'J': _b.EraseDisplay(_params.Count > 0 ? _params[0] : 0); return true;
            case 'K': _b.EraseLine(_params.Count > 0 ? _params[0] : 0); return true;
            case 'L': _b.InsertLines(P(0, 1)); return true;
            case 'M': _b.DeleteLines(P(0, 1)); return true;
            case 'P': _b.DeleteChars(P(0, 1)); return true;
            case '@': _b.InsertChars(P(0, 1)); return true;
            case 'X': _b.EraseChars(P(0, 1)); return true;
            default: return false;
        }
    }

    /// <summary>스크롤과 스크롤 영역(S T r). 처리했으면 true.</summary>
    private bool DispatchScrollCsi(char final)
    {
        switch (final)
        {
            case 'S': _b.ScrollUp(P(0, 1)); return true;
            case 'T': _b.ScrollDown(P(0, 1)); return true;
            case 'r': _b.SetScrollRegion(P(0, 1) - 1, P(1, _b.Rows) - 1); return true;
            default: return false;
        }
    }

    /// <summary>글자 속성(m)과 장치 질의(n, c). 처리했으면 true.</summary>
    private bool DispatchStatusCsi(char final)
    {
        switch (final)
        {
            case 'm': ApplySgr(); return true;
            case 'n': ApplyDeviceStatus(); return true;
            case 'c':
                // 장치 속성 질의: VT100 + AVO로 응답. (ConPTY가 시작 시 물어본다.)
                Reply?.Invoke(_csiPrefix == '>' ? "\x1b[>0;0;0c" : "\x1b[?1;0c");
                return true;
            default: return false;
        }
    }

    private void ApplyDeviceStatus()
    {
        int kind = _params.Count > 0 ? _params[0] : 0;
        if (kind == 6)
        {
            // 커서 위치 보고(1-based). ConPTY는 시작 시 이 응답을 기다릴 수 있다.
            Reply?.Invoke($"\x1b[{_b.CursorY + 1};{Math.Min(_b.CursorX, _b.Cols - 1) + 1}R");
        }
        else if (kind == 5)
        {
            Reply?.Invoke("\x1b[0n");
        }
        else
        {
            // 그 밖의 상태 질의는 응답하지 않는다.
        }
    }

    private void ApplyPrivateMode(int mode, bool on)
    {
        switch (mode)
        {
            case 25: _b.CursorVisible = on; break;
            case 47:
            case 1047:
            case 1049: _b.SetAlternate(on); break;
            default: break; // 자동 줄바꿈, 마우스, bracketed paste 등은 미지원.
        }
    }

    private void ApplySgr()
    {
        if (_params.Count == 0)
        {
            ResetSgr();
            return;
        }
        for (int i = 0; i < _params.Count; i++)
        {
            int p = _params[i];
            if (p is 38 or 48)
            {
                i = ApplyExtendedColor(i, p == 38); // 뒤따르는 파라미터를 함께 소비한다.
            }
            else if (!TryApplySgrAttribute(p))
            {
                TryApplySgrBasicColor(p);
            }
        }
    }

    /// <summary>글자 속성 SGR(0 초기화, 1 굵게, 3 기울임, 4 밑줄, 7 반전과 각각의 해제 22 23 24 27). 처리했으면 true.</summary>
    private bool TryApplySgrAttribute(int p)
    {
        switch (p)
        {
            case 0: ResetSgr(); return true;
            case 1: _b.CurAttr |= CellAttr.Bold; return true;
            case 3: _b.CurAttr |= CellAttr.Italic; return true;
            case 4: _b.CurAttr |= CellAttr.Underline; return true;
            case 7: _b.CurAttr |= CellAttr.Inverse; return true;
            case 22: _b.CurAttr &= ~CellAttr.Bold; return true;
            case 23: _b.CurAttr &= ~CellAttr.Italic; return true;
            case 24: _b.CurAttr &= ~CellAttr.Underline; return true;
            case 27: _b.CurAttr &= ~CellAttr.Inverse; return true;
            default: return false;
        }
    }

    /// <summary>16색 전경/배경 SGR(30~37, 40~47, 밝은 색 90~97, 100~107, 기본색 39, 49). 처리했으면 true.</summary>
    private bool TryApplySgrBasicColor(int p)
    {
        switch (p)
        {
            case >= 30 and <= 37: _b.CurFg = Palette.Get(p - 30); return true;
            case 39: _b.CurFg = 0; return true;
            case >= 40 and <= 47: _b.CurBg = Palette.Get(p - 40); return true;
            case 49: _b.CurBg = 0; return true;
            case >= 90 and <= 97: _b.CurFg = Palette.Get(p - 90 + 8); return true;
            case >= 100 and <= 107: _b.CurBg = Palette.Get(p - 100 + 8); return true;
            default: return false;
        }
    }

    /// <summary>38/48 확장 색(5;n 또는 2;r;g;b)을 적용하고 소비한 마지막 인덱스를 돌려준다.</summary>
    private int ApplyExtendedColor(int i, bool foreground)
    {
        uint color = 0;
        if (i + 2 < _params.Count && _params[i + 1] == 5)
        {
            color = Palette.Get(_params[i + 2]);
            i += 2;
        }
        else if (i + 4 < _params.Count && _params[i + 1] == 2)
        {
            color = 0xFF000000u | (uint)((_params[i + 2] & 0xFF) << 16 | (_params[i + 3] & 0xFF) << 8 | (_params[i + 4] & 0xFF));
            i += 4;
        }
        else
        {
            return _params.Count; // 형식 오류: 나머지 파라미터를 버린다.
        }
        if (foreground)
        {
            _b.CurFg = color;
        }
        else
        {
            _b.CurBg = color;
        }
        return i;
    }

    private void ResetSgr()
    {
        _b.CurFg = 0;
        _b.CurBg = 0;
        _b.CurAttr = CellAttr.None;
    }

    private void OscState(char c)
    {
        if (c == '\a')
        {
            DispatchOsc();
            _state = State.Ground;
        }
        else if (c == '\x1b')
        {
            // ESC \ (ST) 종결: 여기서 확정하고 Escape 상태에서 '\'를 소비한다.
            DispatchOsc();
            _state = State.Escape;
        }
        else if (_osc.Length < MaxOscLength)
        {
            _osc.Append(c);
        }
        else
        {
            // 비정상적으로 긴 OSC는 더 이상 쌓지 않는다.
        }
    }

    /// <summary>
    /// 셸 통합 마커 "PD:&lt;번호&gt;:&lt;경로&gt;"(창 제목으로 전달)를 해석한다. 경로가 비어 있으면(FileSystem 외 위치) 프롬프트 신호만 보낸다.
    /// PowerShell 5.1은 콘솔 VT 모드가 꺼져 있어 OSC 9;9를 직접 쓸 수 없고, 창 제목은 conhost가 항상 전달하므로 이 경로를 쓴다(doc/02 3절).
    /// </summary>
    private bool TryDispatchShellMarker(string payload)
    {
        string prefix = MarkerToken is null ? "PD:" : $"PD:{MarkerToken}:";
        if (!payload.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }
        int second = payload.IndexOf(':', prefix.Length);
        if (second < 0)
        {
            return false;
        }
        string path = payload[(second + 1)..];
        int stamp = path.IndexOf('?'); // cmd 프롬프트가 붙이는 시각(경로에는 ?를 쓸 수 없다).
        if (stamp >= 0)
        {
            path = path[..stamp];
        }
        if (path.Length > 0)
        {
            CwdReported?.Invoke(path);
        }
        else
        {
            // 경로 없음: cwd 보고는 생략하고 프롬프트 신호만 보낸다.
        }
        PromptShown?.Invoke();
        return true;
    }

    private void DispatchOsc()
    {
        string s = _osc.ToString();
        int semi = s.IndexOf(';');
        string cmd = semi < 0 ? s : s[..semi];
        string payload = semi < 0 ? string.Empty : s[(semi + 1)..];
        switch (cmd)
        {
            case "0":
            case "2":
                HandleTitle(payload);
                break;
            case "9":
                HandleOscCwd(payload);
                break;
            case "133":
                HandleOscPrompt(payload);
                break;
            default:
                break; // OSC 52(클립보드 쓰기) 등 지원하지 않는 명령은 무시한다.
        }
    }

    /// <summary>창 제목(OSC 0/2). 셸 통합 마커는 제목으로 보여주지 않고 경로/프롬프트 신호로 쓴다.</summary>
    private void HandleTitle(string payload)
    {
        if (!TryDispatchShellMarker(payload))
        {
            TitleChanged?.Invoke(payload);
        }
    }

    /// <summary>OSC 9;9;경로. 토큰을 요구하는 세션에서는 무시한다: 토큰으로 인증할 수 없어, 화면에 출력된 내용(파일, 원격 출력)만으로
    /// 경로(UNC 포함)를 위조할 수 있다. 앱의 셸 통합은 이 시퀀스를 쓰지 않는다(창 제목 마커만 사용).</summary>
    private void HandleOscCwd(string payload)
    {
        if (MarkerToken is null && payload.StartsWith("9;", StringComparison.Ordinal))
        {
            CwdReported?.Invoke(payload[2..].Trim('"'));
        }
    }

    /// <summary>OSC 133;A(프롬프트 시작). OSC 9;9와 같은 이유로 토큰 세션에서는 무시한다.</summary>
    private void HandleOscPrompt(string payload)
    {
        if (MarkerToken is null && payload.StartsWith('A'))
        {
            PromptShown?.Invoke();
        }
    }
}

/// <summary>ANSI 16색 + xterm 256색 팔레트(Campbell 계열). 반환값은 0xFFRRGGBB.</summary>
public static class Palette
{
    private static readonly uint[] Base16 =
    {
        0xFF0C0C0C, 0xFFC50F1F, 0xFF13A10E, 0xFFC19C00, 0xFF0037DA, 0xFF881798, 0xFF3A96DD, 0xFFCCCCCC,
        0xFF767676, 0xFFE74856, 0xFF16C60C, 0xFFF9F1A5, 0xFF3B78FF, 0xFFB4009E, 0xFF61D6D6, 0xFFF2F2F2,
    };

    public static uint Get(int index)
    {
        if (index < 0 || index > 255)
        {
            return 0;
        }
        if (index < 16)
        {
            return Base16[index];
        }
        if (index < 232)
        {
            int i = index - 16;
            return Rgb(Level(i / 36), Level(i / 6 % 6), Level(i % 6));
        }
        int g = 8 + (index - 232) * 10;
        return Rgb(g, g, g);
    }

    private static int Level(int v) => v == 0 ? 0 : 55 + v * 40;
    private static uint Rgb(int r, int g, int b) => 0xFF000000u | (uint)(r << 16 | g << 8 | b);
}
