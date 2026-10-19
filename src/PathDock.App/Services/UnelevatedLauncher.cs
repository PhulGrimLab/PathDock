using System.Runtime.InteropServices;
using System.Security.Principal;

namespace PathDock.App.Services;

/// <summary>
/// 관리자 권한으로 실행 중인 앱이 파일을 일반 사용자 권한으로 연다. 탐색기(관리자 아님)에게 ShellExecute를 맡기는 방식이라
/// 연결 프로그램이 평소 탐색기에서 더블클릭한 것과 똑같이 열린다(관리자 권한으로 띄우면 안 열리는 프로그램 대응).
/// UI 스레드에서 호출한다.
/// </summary>
public static class UnelevatedLauncher
{
    private const string ShellWindowsClsid = "9BA05972-F6A8-11CF-A442-00A0C90A8F39";
    private const int DesktopSwClass = 8;      // SWC_DESKTOP
    private const int NeedDispatch = 1;        // SWFO_NEEDDISPATCH

    /// <summary>이 프로세스가 관리자 권한(상승)인지. 아니면 탐색기에 맡길 필요가 없다.</summary>
    public static bool IsElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>탐색기를 통해 파일을 연다. 탐색기와 통신하지 못하면 false(호출 측이 기존 방식으로 연다).</summary>
    public static bool TryOpen(string path, string workingDirectory, string verb = "open")
    {
        try
        {
            Type? type = Type.GetTypeFromCLSID(new Guid(ShellWindowsClsid));
            if (type is null)
            {
                return false;
            }
            dynamic shellWindows = Activator.CreateInstance(type)!;
            object location = 0;                 // CSIDL_DESKTOP
            object root = Type.Missing;
            int hwnd = 0;
            dynamic desktop = shellWindows.FindWindowSW(ref location, ref root, DesktopSwClass, out hwnd, NeedDispatch);
            dynamic explorerShell = desktop.Document.Application;
            explorerShell.ShellExecute(path, string.Empty, workingDirectory, verb, 1);
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or MissingMemberException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            return false;
        }
    }
}