using System.Runtime.InteropServices;

namespace PathDock.UiAutomation.Tests;

/// <summary>SendKeys 전에 앱 창을 확실히 포그라운드로 만드는 Win32 도우미.</summary>
internal static class Native
{
    private const byte VkMenu = 0x12;
    private const uint KeyUp = 0x0002;
    private const int ShowRestore = 9;

    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    /// <summary>해당 창 하나만(다른 창은 포함하지 않음) PNG로 저장한다. 진단/문서용.</summary>
    public static void CaptureWindow(IntPtr hwnd, string pngPath)
    {
        GetWindowRect(hwnd, out Rect r);
        using var bmp = new System.Drawing.Bitmap(r.Right - r.Left, r.Bottom - r.Top);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            IntPtr hdc = g.GetHdc();
            PrintWindow(hwnd, hdc, 2);
            g.ReleaseHdc(hdc);
        }
        bmp.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
    }

    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>창에 닫기 요청(WM_CLOSE)을 보낸다. <c>Process.CloseMainWindow</c>는 시작 직후 떠 있는 로딩 팝업을 메인 창으로 오인할 수 있어 쓰지 않는다.</summary>
    public static bool RequestClose(IntPtr hwnd) => PostMessage(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero);

    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);

    /// <summary>
    /// 이 프로세스를 모니터별 DPI 인식으로 만든다. 안 하면 배율이 100%가 아닌 PC에서 GetWindowRect/PrintWindow가 창을 축소된 크기로 읽어
    /// 캡처가 창의 왼쪽 위 일부만 찍힌다(막대가 있는 아래쪽이 빠져 "안 움직인다"고 잘못 판정). 다른 창을 만들기 전에 한 번 호출한다.
    /// </summary>
    public static void EnableDpiAwareness() => SetProcessDpiAwarenessContext(new IntPtr(-4)); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int maxCount);

    /// <summary>
    /// 해당 프로세스의 보이는 최상위 창 중 제목이 정확히 일치하는 첫 창의 핸들(없으면 0).
    /// 시작 중 로딩 팝업이 같은 프로세스의 먼저 뜬 창이라 <c>Process.MainWindowHandle</c>은 믿을 수 없어 제목으로 찾는다.
    /// </summary>
    public static IntPtr FindWindowByTitle(int processId, string title)
    {
        IntPtr found = IntPtr.Zero;
        var sb = new System.Text.StringBuilder(256);
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid != (uint)processId || !IsWindowVisible(hWnd))
            {
                return true;
            }
            sb.Clear();
            GetWindowText(hWnd, sb, sb.Capacity);
            if (sb.ToString() == title)
            {
                found = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>
    /// 창을 그려 가로 전체, 세로 [topFraction, bottomFraction) 구간의 픽셀 지문(체크섬)을 돌려준다. 같은 화면이면 같은 값 - 애니메이션이 움직이는지 판정하는 데 쓴다.
    /// 창 크기를 알 수 없으면 0.
    /// </summary>
    public static long CaptureChecksum(IntPtr hwnd, double topFraction, double bottomFraction)
    {
        if (!GetWindowRect(hwnd, out Rect r))
        {
            return 0;
        }
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        if (w <= 0 || h <= 0)
        {
            return 0;
        }
        // 실제 화면(CopyFromScreen)을 읽는다. PrintWindow는 창을 그 시점에 다시 그리게 해서, 스스로 타이머로 그리는 창은
        // 타이머가 멈춰도 매번 다른 그림이 나와 "움직인다"고 잘못 판정한다. 팝업은 최상단이라 다른 창에 가려지지 않는다.
        using var bmp = new System.Drawing.Bitmap(w, h);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(r.Left, r.Top, 0, 0, new System.Drawing.Size(w, h));
        }
        long sum = 17;
        for (int y = (int)(h * topFraction); y < (int)(h * bottomFraction); y++)
        {
            for (int x = 0; x < w; x++)
            {
                sum = unchecked(sum * 31 + bmp.GetPixel(x, y).ToArgb());
            }
        }
        return sum;
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    /// <summary>창을 앞으로 가져온다. Alt 키 입력을 흉내 내 포그라운드 잠금 제한을 우회한다. 성공 여부 반환.</summary>
    public static bool BringToFront(IntPtr hwnd)
    {
        ShowWindow(hwnd, ShowRestore);
        keybd_event(VkMenu, 0, 0, UIntPtr.Zero);
        keybd_event(VkMenu, 0, KeyUp, UIntPtr.Zero);
        SetForegroundWindow(hwnd);
        return GetForegroundWindow() == hwnd;
    }

    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    private const uint MouseLeftDown = 0x0002, MouseLeftUp = 0x0004;

    /// <summary>
    /// 앱 창 위에서 왼쪽 버튼으로 끌어 놓는다(OS 마우스 입력). 시작 전과 이동 중에 앱 창이 앞에 있는지 확인하고, 아니면 버튼을 떼고 false를 돌려준다
    /// (다른 창에 입력이 가는 것을 막는다). 끝나면 원래 커서 위치로 돌려놓는다. 좌표는 화면 픽셀(DPI 인식 프로세스 전제).
    /// </summary>
    public static bool Drag(IntPtr appWindow, int fromX, int fromY, int toX, int toY)
    {
        GetCursorPos(out Point original);
        bool pressed = false;
        try
        {
            if (GetForegroundWindow() != appWindow)
            {
                return false;
            }
            SetCursorPos(fromX, fromY);
            Thread.Sleep(150);
            mouse_event(MouseLeftDown, 0, 0, 0, UIntPtr.Zero);
            pressed = true;
            Thread.Sleep(150);
            const int steps = 14;
            for (int i = 1; i <= steps; i++)
            {
                if (GetForegroundWindow() != appWindow)
                {
                    return false;
                }
                SetCursorPos(fromX + (toX - fromX) * i / steps, fromY + (toY - fromY) * i / steps);
                Thread.Sleep(40);
            }
            Thread.Sleep(250); // 놓기 전에 마지막 DragOver가 처리되도록 잠깐 머문다
            return GetForegroundWindow() == appWindow;
        }
        finally
        {
            if (pressed)
            {
                mouse_event(MouseLeftUp, 0, 0, 0, UIntPtr.Zero);
            }
            Thread.Sleep(200);
            SetCursorPos(original.X, original.Y);
        }
    }
}
