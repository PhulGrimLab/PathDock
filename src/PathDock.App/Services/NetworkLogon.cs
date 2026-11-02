using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PathDock.App.Services;

/// <summary>
/// 로그인이 필요한 네트워크 공유(UNC)에 Windows 표준 자격 증명 창으로 연결한다(탐색기가 처음 접속할 때 띄우는 창과 같다).
/// 이미 저장된 자격 증명이나 현재 로그인 계정으로 접속되면 창은 뜨지 않는다. UI 스레드에서 호출해야 한다(창이 닫힐 때까지 돌아오지 않는다).
/// </summary>
public static class NetworkLogon
{
    private const uint ConnectInteractive = 0x00000008; // 인증이 필요하면 창을 띄운다
    private const uint ResourceTypeDisk = 1;
    private const int ErrorCancelled = 1223;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NetResource
    {
        public uint Scope;
        public uint Type;
        public uint DisplayType;
        public uint Usage;
        public string? LocalName;
        public string? RemoteName;
        public string? Comment;
        public string? Provider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection3(IntPtr owner, ref NetResource resource, string? password, string? userName, uint flags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetGetConnection(string localName, System.Text.StringBuilder remoteName, ref int length);

    private const int ErrorConnectionUnavail = 1201; // 기억된 네트워크 드라이브가 지금 끊겨 있음(원격 이름은 채워져 돌아온다)

    /// <summary>"Z:\..."의 Z가 네트워크 드라이브로 연결(또는 기억)돼 있으면 그 공유 경로(\\서버\공유). 아니면 null.</summary>
    private static string? MappedDriveRemote(string path)
    {
        if (path.Length < 2 || path[1] != ':' || !char.IsAsciiLetter(path[0]))
        {
            return null;
        }
        var sb = new System.Text.StringBuilder(512);
        int length = sb.Capacity;
        int result = WNetGetConnection(path.Substring(0, 2), sb, ref length);
        return (result is 0 or ErrorConnectionUnavail) && sb.Length > 0 ? sb.ToString() : null;
    }

    /// <summary>
    /// 공유(UNC 경로, 또는 끊긴 네트워크 드라이브 문자)에 연결을 시도한다(필요하면 로그인 창). 연결되면 true.
    /// 사용자가 취소하거나 서버를 찾지 못하면 false이고 <paramref name="cancelled"/>로 취소 여부를 알린다.
    /// 연결 대상이 아닌 경로면 false이고 <paramref name="error"/>는 -1(시도하지 않음).
    /// </summary>
    public static bool TryConnect(string path, out bool cancelled, out int error)
    {
        cancelled = false;
        error = -1;
        string? local = null;
        string? share = Core.Paths.SafeIo.UncShareRoot(path);
        if (share is null)
        {
            share = MappedDriveRemote(path);
            local = share is null ? null : path.Substring(0, 2);
        }
        if (share is null)
        {
            return false;
        }
        var resource = new NetResource { Type = ResourceTypeDisk, LocalName = local, RemoteName = share };
        error = WNetAddConnection3(OwnerHandle(), ref resource, null, null, ConnectInteractive);
        cancelled = error == ErrorCancelled;
        return error == 0;
    }

    private static IntPtr OwnerHandle()
    {
        Window? owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;
        return owner is null ? IntPtr.Zero : new WindowInteropHelper(owner).Handle;
    }
}
