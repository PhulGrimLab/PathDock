namespace PathDock.Core.Terminal;

/// <summary>터미널 탭 제목 규칙: "셸 이름 · 현재 폴더 이름". 상태가 없는 정적 헬퍼.</summary>
public static class TerminalTabTitle
{
    /// <summary>
    /// 셸 이름과 현재 폴더의 마지막 이름으로 제목을 만든다. 경로를 아직 모르면 셸 이름만, 드라이브 루트(C:\)는 "C:"로 표시한다.
    /// </summary>
    public static string Build(TerminalShell shell, string? cwd)
    {
        string shellName = shell == TerminalShell.CommandPrompt ? "cmd" : "PowerShell";
        string? leaf = LeafName(cwd);
        return leaf is null ? shellName : $"{shellName} · {leaf}";
    }

    private static string? LeafName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }
        string trimmed = path.TrimEnd('\\', '/');
        if (trimmed.Length == 0)
        {
            return path.Trim();
        }
        string name = Path.GetFileName(trimmed);
        if (name.Length > 0)
        {
            return name;
        }
        // 이름이 없는 루트: "C:\"는 "C:", UNC 공유 루트(\\서버\공유)는 길어지므로 마지막 조각(공유 이름)만 쓴다.
        return trimmed.StartsWith(@"\\", StringComparison.Ordinal) ? trimmed[(trimmed.LastIndexOf('\\') + 1)..] : trimmed;
    }
}
