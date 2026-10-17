namespace PathDock.Core.Terminal;

/// <summary>
/// 터미널에 붙여넣기 전에 클립보드 텍스트를 다듬고 확인이 필요한지 판단한다(상태 없음).
/// 줄바꿈(Enter)은 입력 즉시 명령을 실행하므로, 줄바꿈이 하나라도 있으면 사용자가 내용을 보고 확인해야 한다
/// (웹에서 복사한 명령 끝에 숨은 줄바꿈이 붙어 있으면 한 줄이어도 바로 실행된다).
/// </summary>
public static class PasteGuard
{
    /// <summary>
    /// 줄바꿈을 Enter(\r)로 통일하고, 탭과 Enter 외의 제어 문자(ESC, Ctrl 조합 등)를 제거한다.
    /// 제어 문자를 그대로 보내면 셸의 키 바인딩이나 이스케이프 시퀀스로 해석되어 눈에 보이지 않는 동작을 일으킬 수 있다.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }
        string unified = text.Replace("\r\n", "\r").Replace('\n', '\r');
        return unified.Any(c => char.IsControl(c) && c != '\r' && c != '\t')
            ? string.Concat(unified.Where(c => !char.IsControl(c) || c == '\r' || c == '\t'))
            : unified;
    }

    /// <summary>붙여넣으면 명령이 실행되는지(Enter가 하나라도 있는지). 끝의 줄바꿈도 포함한다.</summary>
    public static bool RunsCommands(string normalized) => normalized.Contains('\r');

    /// <summary>붙여넣을 줄 수(끝의 줄바꿈 뒤 빈 줄은 세지 않는다). 비어 있지 않으면 최소 1.</summary>
    public static int LineCount(string normalized)
    {
        return normalized.Length == 0 ? 0 : normalized.TrimEnd('\r').Count(c => c == '\r') + 1;
    }

    /// <summary>확인 창에 보여줄 미리보기: 앞의 <paramref name="maxLines"/>줄(각 줄은 <paramref name="maxChars"/>자까지)과 가려진 줄 수.</summary>
    public static string Preview(string normalized, int maxLines, int maxChars, out int hiddenLines)
    {
        string[] lines = normalized.TrimEnd('\r').Split('\r');
        hiddenLines = Math.Max(0, lines.Length - maxLines);
        return string.Join('\n', lines.Take(maxLines).Select(l => l.Length > maxChars ? l[..maxChars] + "…" : l));
    }
}
