using Microsoft.VisualBasic.FileIO;
using PathDock.Core.Localization;
using PathDock.Core.Paths;

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
        if (IsReservedDeviceName(name))
        {
            return Loc.T("Windows 예약 이름(CON, PRN, AUX, NUL, COM1~9, LPT1~9)은 쓸 수 없습니다.", "Windows reserved names (CON, PRN, AUX, NUL, COM1-9, LPT1-9) cannot be used.");
        }
        return null;
    }

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// 장치 이름(CON, NUL, COM1 등)인지. 첫 마침표 앞 부분만 보므로 "con.txt"도 해당한다.
    /// 이런 이름의 폴더/파일은 만들어져도 탐색기 등에서 열거나 지우기 어렵다.
    /// </summary>
    public static bool IsReservedDeviceName(string name)
    {
        int dot = name.IndexOf('.');
        string stem = (dot < 0 ? name : name[..dot]).TrimEnd(' ');
        return ReservedDeviceNames.Contains(stem);
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

    /// <summary><paramref name="path"/>가 <paramref name="folder"/> 자신이거나 그 안인지(링크를 풀어낸 실제 위치로 비교).</summary>
    public static bool IsInside(string path, string folder) => IsSameOrInside(path, folder);

    private static bool IsSameOrInside(string path, string folder)
    {
        // 링크(정션/심볼릭 링크)를 모두 풀어낸 실제 위치로 비교한다: 자기 자신 안을 가리키는 링크로 우회하면 복사가 끝없이 이어진다.
        string p = SafeIo.RealPath(path).TrimEnd('\\', '/');
        string f = SafeIo.RealPath(folder).TrimEnd('\\', '/');
        return string.Equals(p, f, StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(f + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>파일/폴더를 대상 폴더로 복사하고 새 경로를 돌려준다. 같은 이름이 있으면 "- 복사" 이름으로 만든다. 폴더는 재분석 지점(링크)을 따라가지 않는다.</summary>
    public static string CopyInto(string source, string destDir, IProgress<long>? progress = null, CancellationToken ct = default) => CopyIntoCore(source, destDir, out _, progress, ct);

    /// <summary>복사 본체. <paramref name="skippedLinks"/>는 폴더 안의 링크(정션/심볼릭 링크)를 따라가지 않고 건너뛰었는지(원본 삭제 여부 판단용).</summary>
    private static string CopyIntoCore(string source, string destDir, out bool skippedLinks, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        skippedLinks = false;
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
            try
            {
                CopyDirectory(source, target, ref skippedLinks, progress, ct);
            }
            catch (OperationCanceledException)
            {
                TryDeleteDirectory(target); // 취소하면 이 항목의 반쯤 복사된 결과는 남기지 않는다.
                throw;
            }
        }
        else
        {
            CopyFile(source, target, progress, ct);
        }
        return target;
    }

    private static void CopyDirectory(string source, string target, ref bool skippedLinks, IProgress<long>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(target);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            CopyFile(file, Path.Combine(target, Path.GetFileName(file)), progress, ct);
        }
        foreach (string dir in Directory.EnumerateDirectories(source))
        {
            ct.ThrowIfCancellationRequested();
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
            {
                skippedLinks = true;
                continue; // 링크/정션은 따라가지 않는다(순환 방지).
            }
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)), ref skippedLinks, progress, ct);
        }
    }

    private const int CopyBufferBytes = 1024 * 1024;

    /// <summary>
    /// 파일 하나를 복사한다. 진행 보고나 취소가 필요 없으면 <see cref="File.Copy(string, string)"/>를 그대로 쓰고,
    /// 그렇지 않으면 1MB씩 읽어 쓰면서 바이트 수를 보고하고 취소를 확인한다(복사본은 특성·시간을 원본과 맞춘다).
    /// 중간에 실패하거나 취소되면 반쯤 쓰인 대상 파일은 지운다.
    /// </summary>
    private static void CopyFile(string source, string target, IProgress<long>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (progress is null && !ct.CanBeCanceled)
        {
            File.Copy(source, target);
            return;
        }
        bool completed = false;
        try
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, CopyBufferBytes, FileOptions.SequentialScan))
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferBytes, FileOptions.SequentialScan))
            {
                var buffer = new byte[CopyBufferBytes];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    output.Write(buffer, 0, read);
                    progress?.Report(read);
                }
            }
            File.SetAttributes(target, File.GetAttributes(source));
            File.SetCreationTimeUtc(target, File.GetCreationTimeUtc(source));
            File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(source));
            completed = true;
        }
        finally
        {
            if (!completed)
            {
                try { File.Delete(target); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 지우지 못해도 원래 오류를 우선한다. */ }
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 정리 실패는 무시한다(취소 결과를 알리는 것이 우선).
        }
    }

    /// <summary>복사·이동할 전체 바이트 수(진행률의 분모). 폴더는 하위 파일 크기를 더하고 링크(재분석 지점)는 복사와 같이 건너뛴다. 읽을 수 없는 항목은 0으로 센다.</summary>
    public static long MeasureBytes(string path, CancellationToken ct = default)
    {
        try
        {
            if (File.Exists(path))
            {
                return new FileInfo(path).Length;
            }
            if (!Directory.Exists(path))
            {
                return 0;
            }
            long total = 0;
            var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, RecurseSubdirectories = true };
            foreach (string file in Directory.EnumerateFiles(path, "*", options))
            {
                ct.ThrowIfCancellationRequested();
                try { total += new FileInfo(file).Length; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 읽을 수 없는 파일은 건너뛴다. */ }
            }
            return total;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>
    /// 파일/폴더를 대상 폴더로 옮기고 새 경로를 돌려준다. 이미 그 폴더에 있으면 아무것도 하지 않는다.
    /// 같은 이름이 있으면 "- 복사" 이름으로 옮긴다. 다른 드라이브면 복사한 뒤 원본을 지운다.
    /// </summary>
    public static string MoveInto(string source, string destDir, IProgress<long>? progress = null, CancellationToken ct = default)
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
        string copied = CopyIntoCore(source, destDir, out bool skippedLinks, progress, ct);
        if (skippedLinks)
        {
            // 복사에서 건너뛴 링크가 있다: 원본을 지우면 그 링크가 사라지므로(복사본에는 없다) 원본을 남기고 알린다.
            throw new IOException(Loc.T(
                $"폴더 안에 링크(정션/심볼릭 링크)가 있어 복사만 하고 원본은 지우지 않았습니다: {copied}",
                $"The folder contains links (junctions/symbolic links), so it was copied but the original was not deleted: {copied}"));
        }
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

    /// <summary>
    /// 휴지통으로 보낸다. Windows 대화상자를 허용한다(<see cref="UIOption.AllDialogs"/>): 휴지통 용량을 넘거나 휴지통이 꺼진 드라이브라서
    /// 영구 삭제가 되는 경우 Windows가 직접 확인을 묻는다. <c>OnlyErrorDialogs</c>(NOCONFIRMATION)는 그런 확인을 자동으로 "예"로 답해
    /// 앱의 "휴지통으로 보낼까요?" 안내와 달리 조용히 영구 삭제될 수 있다. 사용자가 취소하면 OperationCanceledException.
    /// </summary>
    public static void DeleteToRecycleBin(string path)
    {
        if (Directory.Exists(path))
        {
            FileSystem.DeleteDirectory(path, UIOption.AllDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
        }
        else if (File.Exists(path))
        {
            FileSystem.DeleteFile(path, UIOption.AllDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
        }
        else
        {
            throw new FileNotFoundException(Loc.T("대상을 찾을 수 없습니다.", "The item could not be found."), path);
        }
    }
}
