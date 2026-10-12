using Microsoft.VisualBasic.FileIO;

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
            return "이름을 입력하세요.";
        }
        if (name is "." or "..")
        {
            return "사용할 수 없는 이름입니다.";
        }
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return "이름에 \\ / : * ? \" < > | 문자는 쓸 수 없습니다.";
        }
        if (name.EndsWith('.') || name.EndsWith(' '))
        {
            return "이름은 마침표나 공백으로 끝날 수 없습니다.";
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
        string parent = Path.GetDirectoryName(path) ?? throw new ArgumentException("루트는 이름을 바꿀 수 없습니다.", nameof(path));
        string target = Path.Combine(parent, newName);
        bool isDir = Directory.Exists(path);
        if (!isDir && !File.Exists(path))
        {
            throw new FileNotFoundException("대상을 찾을 수 없습니다.", path);
        }
        if (string.Equals(path, target, StringComparison.Ordinal))
        {
            return path; // 변화 없음
        }
        bool caseOnly = string.Equals(path, target, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && (File.Exists(target) || Directory.Exists(target)))
        {
            throw new IOException($"같은 이름이 이미 있습니다: {newName}");
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
            throw new IOException($"같은 이름이 이미 있습니다: {name}");
        }
        Directory.CreateDirectory(target);
        return target;
    }

    /// <summary>"새 폴더", "새 폴더 (2)"처럼 아직 없는 기본 이름을 제안한다.</summary>
    public static string SuggestNewFolderName(string parent)
    {
        const string Base = "새 폴더";
        string name = Base;
        for (int n = 2; Directory.Exists(Path.Combine(parent, name)) || File.Exists(Path.Combine(parent, name)); n++)
        {
            name = $"{Base} ({n})";
        }
        return name;
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
            throw new FileNotFoundException("대상을 찾을 수 없습니다.", path);
        }
    }
}
