using Microsoft.VisualBasic.FileIO;
using PathDock.Core.Localization;

namespace PathDock.Core.Explorer;

/// <summary>
/// 파일/폴더 조작(이름 변경, 새 폴더, 휴지통 삭제). 상태 없음, UI 스레드가 아닌 곳에서도 호출 가능.
/// 실패는 IOException/UnauthorizedAccessException/ArgumentException으로 던지며, 메시지는 사용자에게 보여줄 수 있는 문장이다.
/// </summary>
public static class FileOperations
{
    /// <summary>이름이 유효하면 null, 아니면 사유 문장을 돌려준다.</summary>
    public static string? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Loc.T("이름을 입력하세요.", "Enter a name.");
        }
        if (name is "." or "..")
        {
            return Loc.T("사용할 수 없는 이름입니다.", "This name cannot be used.");
        }
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return Loc.T("이름에 \\ / : * ? \" < > | 문자는 쓸 수 없습니다.", "A name cannot contain any of these characters: \\ / : * ? \" < > |");
        }
        if (name.EndsWith('.') || name.EndsWith(' '))
        {
            return Loc.T("이름은 마침표나 공백으로 끝날 수 없습니다.", "A name cannot end with a period or a space.");
        }
        return null;
    }

    /// <summary>이름을 바꾸고 새 전체 경로를 돌려준다. 같은 이름이 이미 있으면(대소문자만 다른 자기 자신 제외) 실패.</summary>
    public static string Rename(string path, string newName)
    {
        string? error = ValidateName(newName);
        if (error is not null)
        {
            throw new ArgumentException(error, nameof(newName));
        }
        string parent = Path.GetDirectoryName(path) ?? throw new ArgumentException(Loc.T("루트는 이름을 바꿀 수 없습니다.", "A root folder cannot be renamed."), nameof(path));
        string target = Path.Combine(parent, newName);
        bool isDir = Directory.Exists(path);
        if (!isDir && !File.Exists(path))
        {
            throw new FileNotFoundException(Loc.T("대상을 찾을 수 없습니다.", "The item could not be found."), path);
        }
        if (string.Equals(path, target, StringComparison.Ordinal))
        {
            return path; // 변화 없음
        }
        bool caseOnly = string.Equals(path, target, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && (File.Exists(target) || Directory.Exists(target)))
        {
            throw new IOException(Loc.T($"같은 이름이 이미 있습니다: {newName}", $"An item with the same name already exists: {newName}"));
        }

        if (caseOnly)
        {
            // 대소문자만 바꾸는 경우: 같은 위치로의 직접 이동이 거부될 수 있어 임시 이름을 거친다.
            string temp = Path.Combine(parent, "~pd_" + Guid.NewGuid().ToString("N"));
            Move(path, temp, isDir);
            Move(temp, target, isDir);
        }
        else
        {
            Move(path, target, isDir);
        }
        return target;
    }

    private static void Move(string from, string to, bool isDir)
    {
        if (isDir)
        {
            Directory.Move(from, to);
        }
        else
        {
            File.Move(from, to);
        }
    }

    /// <summary>하위 폴더를 만들고 전체 경로를 돌려준다. 이미 있으면 실패.</summary>
    public static string CreateFolder(string parent, string name)
    {
        string? error = ValidateName(name);
        if (error is not null)
        {
            throw new ArgumentException(error, nameof(name));
        }
        string target = Path.Combine(parent, name);
        if (File.Exists(target) || Directory.Exists(target))
        {
            throw new IOException(Loc.T($"같은 이름이 이미 있습니다: {name}", $"An item with the same name already exists: {name}"));
        }
        Directory.CreateDirectory(target);
        return target;
    }

    /// <summary>"새 폴더", "새 폴더 (2)"처럼 아직 없는 기본 이름을 제안한다.</summary>
    public static string SuggestNewFolderName(string parent)
    {
        string Base = Loc.T("새 폴더", "New folder");
        string name = Base;
        for (int n = 2; Directory.Exists(Path.Combine(parent, name)) || File.Exists(Path.Combine(parent, name)); n++)
        {
            name = $"{Base} ({n})";
        }
        return name;
    }

    /// <summary>"이름", "이름 - 복사", "이름 - 복사 (2)"처럼 대상 폴더에 아직 없는 이름을 고른다(파일은 확장자 앞에 붙인다).</summary>
    public static string UniqueTargetPath(string destDir, string name, bool isDirectory)
    {
        string target = Path.Combine(destDir, name);
        if (!File.Exists(target) && !Directory.Exists(target))
        {
            return target;
        }
        string stem = isDirectory ? name : Path.GetFileNameWithoutExtension(name);
        string ext = isDirectory ? string.Empty : Path.GetExtension(name);
        for (int n = 1; ; n++)
        {
            string copy = Loc.T("복사", "Copy");
            string candidate = Path.Combine(destDir, n == 1 ? $"{stem} - {copy}{ext}" : $"{stem} - {copy} ({n}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static bool IsSameOrInside(string path, string folder)
    {
        string p = Path.GetFullPath(path).TrimEnd('\\', '/');
        string f = Path.GetFullPath(folder).TrimEnd('\\', '/');
        return string.Equals(p, f, StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(f + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>파일/폴더를 대상 폴더로 복사하고 새 경로를 돌려준다. 같은 이름이 있으면 "- 복사" 이름으로 만든다. 폴더는 재분석 지점(링크)을 따라가지 않는다.</summary>
    public static string CopyInto(string source, string destDir)
    {
        bool isDir = Directory.Exists(source);
        if (!isDir && !File.Exists(source))
        {
            throw new FileNotFoundException(Loc.T("대상을 찾을 수 없습니다.", "The item could not be found."), source);
        }
        if (isDir && IsSameOrInside(destDir, source))
        {
            throw new IOException(Loc.T("폴더를 자기 자신 안으로 복사할 수 없습니다.", "A folder cannot be copied into itself."));
        }
        string target = UniqueTargetPath(destDir, Path.GetFileName(source.TrimEnd('\\', '/')), isDir);
        if (isDir)
        {
            CopyDirectory(source, target);
        }
        else
        {
            File.Copy(source, target);
        }
        return target;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }
        foreach (string dir in Directory.EnumerateDirectories(source))
        {
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
            {
                continue; // 링크/정션은 따라가지 않는다(순환 방지).
            }
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
        }
    }

    /// <summary>
    /// 파일/폴더를 대상 폴더로 옮기고 새 경로를 돌려준다. 이미 그 폴더에 있으면 아무것도 하지 않는다.
    /// 같은 이름이 있으면 "- 복사" 이름으로 옮긴다. 다른 드라이브면 복사한 뒤 원본을 지운다.
    /// </summary>
    public static string MoveInto(string source, string destDir)
    {
        bool isDir = Directory.Exists(source);
        if (!isDir && !File.Exists(source))
        {
            throw new FileNotFoundException(Loc.T("대상을 찾을 수 없습니다.", "The item could not be found."), source);
        }
        string trimmed = source.TrimEnd('\\', '/');
        if (string.Equals(Path.GetDirectoryName(trimmed), Path.GetFullPath(destDir).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
        {
            return source;
        }
        if (isDir && IsSameOrInside(destDir, source))
        {
            throw new IOException(Loc.T("폴더를 자기 자신 안으로 옮길 수 없습니다.", "A folder cannot be moved into itself."));
        }
        bool sameVolume = string.Equals(Path.GetPathRoot(Path.GetFullPath(source)), Path.GetPathRoot(Path.GetFullPath(destDir)), StringComparison.OrdinalIgnoreCase);
        if (sameVolume)
        {
            string target = UniqueTargetPath(destDir, Path.GetFileName(trimmed), isDir);
            Move(source, target, isDir);
            return target;
        }
        string copied = CopyInto(source, destDir);
        if (isDir)
        {
            Directory.Delete(source, recursive: true);
        }
        else
        {
            File.Delete(source);
        }
        return copied;
    }

    /// <summary>
    /// 이 경로를 지우면 휴지통을 거치는지(고정 디스크만 해당). 네트워크/이동식 드라이브는 휴지통이 없어 바로 영구 삭제된다.
    /// 확인 문구가 실제 동작과 같도록 호출 측이 판단에 쓴다.
    /// </summary>
    public static bool UsesRecycleBin(string path)
    {
        string? root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return false;
        }
        try
        {
            return new DriveInfo(root).DriveType == DriveType.Fixed;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>휴지통으로 보낸다(영구 삭제 아님 - 복구 가능). 확인 창/진행 창은 띄우지 않는다.</summary>
    public static void DeleteToRecycleBin(string path)
    {
        if (Directory.Exists(path))
        {
            FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
        }
        else if (File.Exists(path))
        {
            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
        }
        else
        {
            throw new FileNotFoundException(Loc.T("대상을 찾을 수 없습니다.", "The item could not be found."), path);
        }
    }
}
