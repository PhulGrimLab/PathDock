using System.IO;
using System.Runtime.InteropServices;

namespace PathDock.App.Services;

/// <summary>정리할 폴더 묶음 하나. <see cref="OlderThan"/>이 있으면 그보다 오래 바뀌지 않은 파일/빈 폴더만 지운다(실행 중인 프로그램이 쓰는 임시 파일 보호).</summary>
public sealed record CleanupFolderSpec(
    string Id,
    string Korean,
    string English,
    string DescriptionKo,
    string DescriptionEn,
    string[] Folders,
    TimeSpan? OlderThan = null,
    Func<string, bool>? FileFilter = null,
    string[]? SkipTopLevel = null);

/// <summary>폴더 검사/정리 결과. <see cref="Skipped"/>는 사용 중이거나 권한이 없어 지우지 못한 파일 수.</summary>
public sealed class CleanupTotals
{
    public long Bytes { get; set; }
    public int Files { get; set; }
    public int Skipped { get; set; }
}

/// <summary>
/// Windows 임시 파일/캐시 폴더 검사와 정리, 메모리(작업 집합) 줄이기. 정적 헬퍼, 호출 스레드 제한 없음(오래 걸리니 UI 스레드 밖에서 호출).
/// 링크(재분석 지점)는 따라가지 않고, 지울 수 없는 파일은 건너뛰고 세어 둔다.
/// </summary>
public static class CleanupService
{
    private static readonly TimeSpan TempAge = TimeSpan.FromDays(1);

    private static string Local(params string[] parts) => Path.Combine(new[] { Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) }.Concat(parts).ToArray());

    private static string CommonData(params string[] parts) => Path.Combine(new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) }.Concat(parts).ToArray());

    private static string Windows(params string[] parts) => Path.Combine(new[] { Environment.GetFolderPath(Environment.SpecialFolder.Windows) }.Concat(parts).ToArray());

    /// <summary>정리할 폴더 목록. 앞 두 개(임시 폴더)만 최근 하루 안에 바뀐 파일을 남긴다. 사용자 임시 폴더의 ".net"은 이 앱(단일 파일)의 압축 해제 폴더라 건드리지 않는다.</summary>
    public static IReadOnlyList<CleanupFolderSpec> FolderSpecs { get; } = new[]
    {
        new CleanupFolderSpec("UserTemp", "사용자 임시 파일", "User temp files",
            "프로그램이 만들어 두고 지우지 않은 임시 파일(최근 24시간 안에 바뀐 파일은 남김)",
            "Leftover temp files from programs (files changed in the last 24 hours are kept)",
            new[] { Path.GetTempPath() }, TempAge, SkipTopLevel: new[] { ".net" }),
        new CleanupFolderSpec("WindowsTemp", "Windows 임시 파일", "Windows temp files",
            "Windows 폴더의 임시 파일(최근 24시간 안에 바뀐 파일은 남김)",
            "Temp files in the Windows folder (files changed in the last 24 hours are kept)",
            new[] { Windows("Temp") }, TempAge),
        new CleanupFolderSpec("UpdateDownloads", "Windows 업데이트 내려받은 파일", "Windows Update downloads",
            "업데이트 설치 파일 캐시(최근 24시간 안에 바뀐 파일은 남김, 필요하면 Windows가 다시 내려받음)",
            "Cached update installers (files changed in the last 24 hours are kept; Windows downloads them again if needed)",
            new[] { Windows("SoftwareDistribution", "Download") }, TempAge),
        new CleanupFolderSpec("Thumbnails", "썸네일 캐시", "Thumbnail cache",
            "탐색기 미리 보기 그림 캐시(다시 열면 자동으로 만들어짐)",
            "Explorer thumbnail cache (rebuilt automatically when folders are opened)",
            new[] { Local("Microsoft", "Windows", "Explorer") },
            FileFilter: name => name.StartsWith("thumbcache_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("iconcache_", StringComparison.OrdinalIgnoreCase)),
        new CleanupFolderSpec("InternetCache", "인터넷 임시 파일", "Internet temporary files",
            "Windows 인터넷 구성 요소가 저장한 임시 파일",
            "Temporary files stored by Windows internet components",
            new[] { Local("Microsoft", "Windows", "INetCache") }),
        new CleanupFolderSpec("ErrorReports", "오류 보고서와 충돌 덤프", "Error reports and crash dumps",
            "프로그램이 멈췄을 때 남은 보고서와 덤프 파일",
            "Reports and dump files left after program crashes",
            new[] { Local("Microsoft", "Windows", "WER"), CommonData("Microsoft", "Windows", "WER", "ReportArchive"), CommonData("Microsoft", "Windows", "WER", "ReportQueue"), Local("CrashDumps") }),
        new CleanupFolderSpec("ShaderCache", "그래픽 셰이더 캐시", "Graphics shader cache",
            "DirectX가 저장한 셰이더 캐시(게임/앱이 다시 만들며 처음 한 번 조금 느릴 수 있음)",
            "Shader cache stored by DirectX (apps rebuild it, the first run may be a bit slower)",
            new[] { Local("D3DSCache") }),
    };

    /// <summary>폴더 묶음의 지울 수 있는 크기와 파일 수.</summary>
    public static CleanupTotals Scan(CleanupFolderSpec spec, CancellationToken ct) => Run(spec, delete: false, ct);

    /// <summary>폴더 묶음을 정리한다. <see cref="CleanupTotals.Bytes"/>는 실제로 지운 크기.</summary>
    public static CleanupTotals Clean(CleanupFolderSpec spec, CancellationToken ct) => Run(spec, delete: true, ct);

    private static CleanupTotals Run(CleanupFolderSpec spec, bool delete, CancellationToken ct)
    {
        var totals = new CleanupTotals();
        DateTime cutoff = spec.OlderThan is { } age ? DateTime.UtcNow - age : DateTime.MaxValue;
        foreach (string folder in spec.Folders)
        {
            // 링크로 바뀐 폴더, 그리고 TEMP를 드라이브 루트/문서 폴더 등으로 바꿔 둔 환경은 건드리지 않는다(이 앱은 관리자 권한).
            if (Directory.Exists(folder) && IsSafeRoot(folder))
            {
                Visit(new DirectoryInfo(folder), isRoot: true, spec, cutoff, delete, totals, ct);
            }
        }
        return totals;
    }

    /// <summary>정리 대상 루트로 써도 되는지: 링크가 아니고, 드라이브 루트/Windows/사용자 폴더/문서/바탕화면/다운로드/Program Files이거나 그 상위가 아니어야 한다.</summary>
    internal static bool IsSafeRoot(string folder)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (Path.GetPathRoot(full) is { } root && string.Equals(Path.TrimEndingDirectorySeparator(root), full, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        try
        {
            if (new DirectoryInfo(full).Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        string[] protectedFolders =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            KnownFolders.Downloads,
        };
        foreach (string p in protectedFolders.Where(p => !string.IsNullOrEmpty(p)).Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p))))
        {
            // 보호 폴더와 같거나(예: TEMP=문서 폴더), 보호 폴더를 품은 상위(예: C:\Users)이면 거부한다.
            if (string.Equals(p, full, StringComparison.OrdinalIgnoreCase) || p.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }

    private static void Visit(DirectoryInfo dir, bool isRoot, CleanupFolderSpec spec, DateTime cutoff, bool delete, CleanupTotals totals, CancellationToken ct)
    {
        List<FileSystemInfo> entries;
        try
        {
            entries = dir.EnumerateFileSystemInfos().ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }
        foreach (FileSystemInfo entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            if ((isRoot && spec.SkipTopLevel?.Contains(entry.Name, StringComparer.OrdinalIgnoreCase) == true)
                || entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }
            if (entry is DirectoryInfo sub)
            {
                sub.Refresh(); // 열거한 뒤 링크로 바뀌었을 수 있다: 들어가기 직전에 다시 확인한다.
                if (sub.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }
                Visit(sub, isRoot: false, spec, cutoff, delete, totals, ct);
                // 파일 이름 조건이 있는 항목(썸네일 등)은 그 폴더의 무관한 빈 하위 폴더를 지우지 않는다.
                if (delete && spec.FileFilter is null && sub.LastWriteTimeUtc < cutoff)
                {
                    TryDeleteEmptyDirectory(sub);
                }
            }
            else if (entry is FileInfo file && file.LastWriteTimeUtc < cutoff && (spec.FileFilter?.Invoke(file.Name) ?? true))
            {
                if (!delete)
                {
                    totals.Bytes += file.Length;
                    totals.Files++;
                }
                else if (TryDeleteFile(file, out long length))
                {
                    totals.Bytes += length;
                    totals.Files++;
                }
                else
                {
                    totals.Skipped++;
                }
            }
        }
    }

    private static bool TryDeleteFile(FileInfo file, out long length)
    {
        length = 0;
        bool clearedReadOnly = false;
        try
        {
            length = file.Length;
            if (file.IsReadOnly)
            {
                file.IsReadOnly = false;
                clearedReadOnly = true;
            }
            file.Delete();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (clearedReadOnly)
            {
                try
                {
                    file.IsReadOnly = true; // 못 지웠으면 속성을 원래대로 돌려 둔다.
                }
                catch (Exception ex2) when (ex2 is IOException or UnauthorizedAccessException)
                {
                }
            }
            return false;
        }
    }

    private static void TryDeleteEmptyDirectory(DirectoryInfo dir)
    {
        try
        {
            dir.Delete(recursive: false); // 비어 있지 않으면 IOException
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // ----- 메모리 -----

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr process);

    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessSetQuota = 0x0100;

    /// <summary>물리 메모리 사용량과 전체 크기. 조회에 실패하면 null.</summary>
    public static (long Used, long Total)? QueryMemory()
    {
        var m = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref m) ? ((long)(m.TotalPhys - m.AvailPhys), (long)m.TotalPhys) : null;
    }

    /// <summary>
    /// 다른 프로세스들의 작업 집합(실제 메모리에 올라온 부분)을 줄여 사용 가능한 메모리를 늘린다. 이 앱 자신과 접근할 수 없는 프로세스는 건너뛴다.
    /// 줄어든 쪽은 필요할 때 Windows가 다시 불러오므로 효과는 일시적이다. 성공한 프로세스 수를 돌려준다.
    /// </summary>
    public static int TrimMemory(CancellationToken ct)
    {
        int self = Environment.ProcessId;
        int trimmed = 0;
        foreach (System.Diagnostics.Process p in System.Diagnostics.Process.GetProcesses())
        {
            using (p)
            {
                ct.ThrowIfCancellationRequested();
                if (p.Id == self || p.Id == 0)
                {
                    continue;
                }
                IntPtr handle = OpenProcess(ProcessQueryInformation | ProcessSetQuota, false, p.Id);
                if (handle == IntPtr.Zero)
                {
                    continue;
                }
                try
                {
                    if (EmptyWorkingSet(handle))
                    {
                        trimmed++;
                    }
                }
                finally
                {
                    CloseHandle(handle);
                }
            }
        }
        return trimmed;
    }
}
