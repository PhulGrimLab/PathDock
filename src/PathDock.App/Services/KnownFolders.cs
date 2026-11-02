using System.Runtime.InteropServices;

namespace PathDock.App.Services;

/// <summary>사용자가 위치를 옮겼을 수 있는 Windows 기본 폴더(바탕화면, 다운로드)의 실제 경로. 정적 헬퍼, 스레드 제한 없음.</summary>
public static class KnownFolders
{
    private static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, [MarshalAs(UnmanagedType.LPWStr)] out string path);

    public static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    /// <summary>다운로드 폴더. 조회에 실패하면 사용자 폴더 아래 Downloads.</summary>
    public static string Downloads =>
        SHGetKnownFolderPath(DownloadsId, 0, IntPtr.Zero, out string path) == 0 && !string.IsNullOrEmpty(path)
            ? path
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
}
