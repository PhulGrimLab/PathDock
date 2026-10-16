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
}
