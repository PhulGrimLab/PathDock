using System.Windows.Input;

namespace PathDock.App.Controls;

/// <summary>터미널 키 입력을 셸로 보낼 VT 시퀀스로 바꾸는 규칙(상태 없음, <see cref="TerminalControl"/>이 쓴다).</summary>
internal static class TerminalKeyMap
{
    /// <summary>Ctrl+C(또는 Ctrl+Shift+C) 같은 복사 조합인지(Alt가 눌리지 않은 Ctrl+C).</summary>
    public static bool IsCopyChord(Key key, ModifierKeys mods) =>
        key == Key.C && (mods & ModifierKeys.Control) != 0 && (mods & ModifierKeys.Alt) == 0;

    /// <summary>키 → 전송 문자열. null=처리 안 함(텍스트 입력에 맡김), 빈 문자열=붙여넣기.</summary>
    public static string? Translate(Key key, ModifierKeys mods)
    {
        bool ctrl = (mods & ModifierKeys.Control) != 0;
        bool shift = (mods & ModifierKeys.Shift) != 0;
        bool alt = (mods & ModifierKeys.Alt) != 0;

        // AltGr(= Ctrl+Alt)로 V를 치는 자판은 붙여넣기가 아니라 글자 입력이다.
        if ((ctrl && !alt && key == Key.V) || (shift && !ctrl && key == Key.Insert))
        {
            return string.Empty;
        }
        if (ctrl && !alt && key >= Key.A && key <= Key.Z)
        {
            return ((char)(key - Key.A + 1)).ToString(); // Ctrl+A=0x01 ... Ctrl+C=0x03(인터럽트)
        }
        // xterm 방식의 보조키 번호: Shift=2, Alt=3, Ctrl=5 ... (없으면 1)
        int modifier = 1 + (shift ? 1 : 0) + (alt ? 2 : 0) + (ctrl ? 4 : 0);
        string Cursor(char final) => modifier > 1 ? $"\x1b[1;{modifier}{final}" : $"\x1b[{final}";
        string Tilde(int code) => modifier > 1 ? $"\x1b[{code};{modifier}~" : $"\x1b[{code}~";
        return key switch
        {
            Key.Enter => "\r",
            Key.Back => ctrl ? "\x08" : "\x7f", // Ctrl+Backspace: PSReadLine이 앞 단어 지우기로 쓰는 ^H
            Key.Tab => shift ? "\x1b[Z" : "\t",
            Key.Escape => "\x1b",
            Key.Up => Cursor('A'),
            Key.Down => Cursor('B'),
            Key.Right => Cursor('C'),
            Key.Left => Cursor('D'),
            Key.Home => modifier > 1 ? Cursor('H') : "\x1b[H",
            Key.End => modifier > 1 ? Cursor('F') : "\x1b[F",
            Key.Delete => Tilde(3),
            Key.Insert => "\x1b[2~",
            Key.PageUp => "\x1b[5~",
            Key.PageDown => "\x1b[6~",
            Key.F1 => "\x1bOP",
            Key.F2 => "\x1bOQ",
            Key.F3 => "\x1bOR",
            Key.F4 => "\x1bOS",
            Key.F5 => Tilde(15),
            Key.F6 => Tilde(17),
            Key.F7 => Tilde(18),
            Key.F8 => Tilde(19),
            Key.F9 => Tilde(20),
            Key.F10 => Tilde(21),
            Key.F11 => Tilde(23),
            Key.F12 => Tilde(24),
            _ => null,
        };
    }
}
