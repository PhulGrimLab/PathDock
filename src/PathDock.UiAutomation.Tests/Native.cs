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
}
