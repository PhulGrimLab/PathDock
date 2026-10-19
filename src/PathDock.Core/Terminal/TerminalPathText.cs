namespace PathDock.Core.Terminal;

/// <summary>
/// 파일 경로를 터미널 입력줄에 넣을 글자로 바꾼다(Enter는 포함하지 않으므로 명령이 바로 실행되지 않는다). 상태 없음.
/// 공백·특수문자가 있으면 셸에 맞게 따옴표로 감싼다: PowerShell은 작은따옴표(안의 작은따옴표는 두 번), cmd는 큰따옴표.
/// 경로에 제어 문자(줄바꿈 등)가 들어 있으면 입력으로 명령이 실행될 수 있어 그 경로는 건너뛴다.
/// </summary>
public static class TerminalPathText
{
    private const string PowerShellSpecials = " \t&()[]{}^=;!'+,`~@#$%";
    private const string CmdSpecials = " \t&()[]{}^=;!'+,`~@#%";

    /// <summary>경로 하나를 입력줄용으로 만든다. 제어 문자가 있으면 null.</summary>
    public static string? Quote(string path, TerminalShell shell)
    {
        if (string.IsNullOrEmpty(path) || path.Any(char.IsControl))
        {
            return null;
        }
        if (shell == TerminalShell.CommandPrompt)
        {
            return path.IndexOfAny(CmdSpecials.ToCharArray()) >= 0 || path.Contains('"')
                ? $"\"{path.Replace("\"", string.Empty)}\"" // 파일 이름에는 큰따옴표를 쓸 수 없다. 경로에 있더라도 cmd에서는 안전하게 제거한다.
                : path;
        }
        return path.IndexOfAny(PowerShellSpecials.ToCharArray()) >= 0
            ? $"'{path.Replace("'", "''")}'"
            : path;
    }

    /// <summary>여러 경로를 공백으로 이어 입력줄용 글자로 만든다(끝에 공백 하나를 붙여 이어서 입력하기 쉽게 한다). 쓸 수 있는 경로가 없으면 빈 문자열.</summary>
    public static string Join(IEnumerable<string> paths, TerminalShell shell)
    {
        List<string> quoted = paths.Select(p => Quote(p, shell)).Where(q => q is not null).Select(q => q!).ToList();
        return quoted.Count == 0 ? string.Empty : string.Join(' ', quoted) + " ";
    }
}
