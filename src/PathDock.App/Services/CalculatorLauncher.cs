using System.Runtime.InteropServices;
using System.Text;

namespace PathDock.App.Services;

/// <summary>
/// Windows 계산기를 연다. 이미 떠 있는 계산기 창이 있으면 새로 만들지 않고 그 창을 맨 앞으로 가져온다.
/// 정적 헬퍼, UI 스레드에서 호출한다.
/// </summary>
public static class CalculatorLauncher
{
    private const int SwRestore = 9;
    private static readonly string[] Titles = { "계산기", "Calculator" };
    private static readonly string[] Classes = { "ApplicationFrameWindow", "CalcFrame" };

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder text, int max);

    /// <summary>떠 있는 계산기 창을 앞으로 가져왔으면 true, 없으면 false(호출 측이 새로 실행한다).</summary>
    public static bool TryActivateExisting()
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd) || !IsCalculator(hWnd))
            {
                return true;
            }
            found = hWnd;
            return false;
        }, IntPtr.Zero);
        if (found == IntPtr.Zero)
        {
            return false;
        }
        Activate(found);
        return true;
    }

    private static bool IsCalculator(IntPtr hWnd)
    {
        var cls = new StringBuilder(64);
        GetClassName(hWnd, cls, cls.Capacity);
        if (Array.IndexOf(Classes, cls.ToString()) < 0)
        {
            return false;
        }
        var title = new StringBuilder(128);
        GetWindowText(hWnd, title, title.Capacity);
        return Array.IndexOf(Titles, title.ToString()) >= 0;
    }

    /// <summary>최소화되어 있으면 복원하고 맨 앞으로 올린다. 다른 프로세스 창이라 입력 스레드를 잠시 연결해야 전경 전환이 허용된다.</summary>
    private static void Activate(IntPtr hWnd)
    {
        if (IsIconic(hWnd))
        {
            ShowWindow(hWnd, SwRestore);
        }
        uint current = GetCurrentThreadId();
        uint foreground = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        bool attached = foreground != current && AttachThreadInput(current, foreground, true);
        try
        {
            BringWindowToTop(hWnd);
            SetForegroundWindow(hWnd);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(current, foreground, false);
            }
        }
    }
}
