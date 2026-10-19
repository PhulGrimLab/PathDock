namespace PathDock.Core.Explorer;

/// <summary>끌어 놓기(드롭)의 결과 동작.</summary>
public enum DropEffect
{
    /// <summary>놓을 수 없음(또는 아무 일도 일어나지 않음).</summary>
    None,
    Copy,
    Move,
}

/// <summary>
/// 끌어 놓기 규칙(상태 없음, UI 비의존). Windows 탐색기와 같은 관례: 같은 드라이브 안은 이동, 다른 드라이브는 복사,
/// Ctrl을 누르면 복사, Shift를 누르면 이동. 폴더를 자기 자신 안으로 놓거나, 이미 그 폴더에 있는 항목을 이동하는 것은 불가(None).
/// </summary>
public static class DropRules
{
    public static DropEffect Decide(IReadOnlyList<string> sources, string destFolder, bool ctrlHeld, bool shiftHeld)
    {
        if (sources.Count == 0 || string.IsNullOrWhiteSpace(destFolder) || !Directory.Exists(destFolder))
        {
            return DropEffect.None;
        }
        string dest = Path.GetFullPath(destFolder);
        foreach (string source in sources)
        {
            if (Directory.Exists(source) && FileOperations.IsInside(dest, source))
            {
                return DropEffect.None; // 폴더를 자기 자신 안으로(또는 그 하위로) 놓을 수 없다.
            }
        }

        DropEffect wanted = ctrlHeld ? DropEffect.Copy
            : shiftHeld ? DropEffect.Move
            : SameVolume(sources, dest) ? DropEffect.Move : DropEffect.Copy;

        if (wanted == DropEffect.Move && sources.All(s => IsDirectChild(s, dest)))
        {
            return DropEffect.None; // 이미 그 폴더에 있다: 이동은 할 일이 없다(복사는 "- 복사" 이름으로 가능).
        }
        return wanted;
    }

    private static bool SameVolume(IReadOnlyList<string> sources, string dest)
    {
        string? destRoot = Path.GetPathRoot(dest);
        return sources.All(s => string.Equals(Path.GetPathRoot(Path.GetFullPath(s)), destRoot, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDirectChild(string source, string destFolder)
    {
        string? parent = Path.GetDirectoryName(Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.Equals(parent?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), destFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }
}
