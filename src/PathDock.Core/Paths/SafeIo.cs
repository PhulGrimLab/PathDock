using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PathDock.Core.Paths;

/// <summary>
/// 관리자 권한으로 도는 앱이 사용자 쓰기 가능한 폴더(설정/캐시/로그)를 다룰 때 쓰는 안전한 파일 입출력과, 느린 경로 확인용 도우미(상태 없음).
/// 같은 사용자의 일반 권한 프로세스가 폴더를 정션/심볼릭 링크로 바꿔 두면 관리자 권한 쓰기가 엉뚱한 위치로 따라가 나갈 수 있으므로,
/// 링크는 따라가지 않고 거부한다.
/// </summary>
public static class SafeIo
{
    /// <summary>경로(파일 또는 폴더)가 정션/심볼릭 링크 같은 재분석 지점인지. 없거나 읽을 수 없으면 false.</summary>
    public static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// 텍스트를 임시 파일에 쓴 뒤 대상으로 옮겨 교체한다. 폴더가 링크면 IOException.
    /// 임시 파일은 새로 만들기(CreateNew)로 열어, 미리 같은 이름으로 심어 둔 링크를 따라가지 않는다. 대상 자리에 링크가 있으면 링크 자체가 교체된다.
    /// </summary>
    public static void WriteAllTextAtomic(string path, string text, Encoding? encoding = null)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (dir is not null && IsReparsePoint(dir))
        {
            throw new IOException($"폴더가 링크라서 쓰지 않습니다: {dir}");
        }
        string tmp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var w = new StreamWriter(fs, encoding ?? new UTF8Encoding(false)))
            {
                w.Write(text);
            }
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 정리 실패는 무시 */ }
            throw;
        }
    }

    /// <summary>
    /// 폴더가 존재하는지 확인하되 <paramref name="timeout"/> 안에 답이 없으면(꺼진 네트워크 공유 등) false를 돌려준다.
    /// 응답 없는 서버를 기다리는 시간은 OS가 정하지만, UI 스레드에서 그대로 기다리면 앱 창이 멈추므로 호출 측이 이것을 쓴다.
    /// 시간 초과된 확인은 백그라운드에서 끝날 때까지 남는다(스레드 풀).
    /// </summary>
    public static bool DirectoryExistsWithTimeout(string path, TimeSpan timeout)
    {
        // 시간 초과한 서버(공유)는 잠시 동안 다시 확인하지 않는다: 시간 초과된 확인은 OS가 포기할 때까지 스레드 풀 스레드를 붙잡고
        // 호출마다 UI가 최대 timeout씩 멈추므로, 꺼진 서버로 동기화가 반복되면 스레드가 쌓이고 창이 계속 멈칫한다.
        string server = Path.GetPathRoot(path) ?? path;
        if (_unreachable.TryGetValue(server, out DateTime until) && DateTime.UtcNow < until)
        {
            return false;
        }
        Task<bool> probe = Task.Run(() =>
        {
            try { return Directory.Exists(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
        });
        if (!probe.Wait(timeout))
        {
            _unreachable[server] = DateTime.UtcNow + UnreachableMemory;
            return false;
        }
        return probe.Result;
    }

    private static readonly TimeSpan UnreachableMemory = TimeSpan.FromSeconds(30);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _unreachable = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>네트워크 위치인지: UNC 경로이거나 네트워크 드라이브로 연결된 드라이브 문자. 끊긴 서버는 응답을 오래 기다리게 하므로 시간 제한 대상이다.</summary>
    public static bool IsNetworkPath(string path)
    {
        if (IsUnc(path))
        {
            return true;
        }
        try
        {
            return path.Length >= 2 && path[1] == ':' && char.IsAsciiLetter(path[0]) && new DriveInfo(path[0].ToString()).DriveType == DriveType.Network;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            return false;
        }
    }

    /// <summary>경로가 UNC(네트워크) 경로인지.</summary>
    public static bool IsUnc(string path) => path.StartsWith(@"\\", StringComparison.Ordinal) && !path.StartsWith(@"\\?\", StringComparison.Ordinal)
        || path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 존재하는 경로의 실제 위치(정션/심볼릭 링크를 모두 풀어낸 경로). 풀 수 없으면 전체 경로화한 값을 돌려준다.
    /// 폴더를 "자기 자신 안으로" 복사/이동하는지 판단할 때 링크로 우회되지 않게 쓴다.
    /// </summary>
    public static string RealPath(string path)
    {
        string full = Path.GetFullPath(path);
        const uint FileFlagBackupSemantics = 0x02000000;
        const uint FileShareAll = 7;
        const uint OpenExisting = 3;
        using SafeFileHandle h = CreateFile(full, 0, FileShareAll, IntPtr.Zero, OpenExisting, FileFlagBackupSemantics, IntPtr.Zero);
        if (h.IsInvalid)
        {
            return full;
        }
        var sb = new StringBuilder(1024);
        uint n = GetFinalPathNameByHandle(h, sb, (uint)sb.Capacity, 0);
        if (n == 0 || n >= sb.Capacity)
        {
            return full;
        }
        string result = sb.ToString();
        if (result.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + result[8..];
        }
        return result.StartsWith(@"\\?\", StringComparison.Ordinal) ? result[4..] : result;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint size, uint flags);
}
