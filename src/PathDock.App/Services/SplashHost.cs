using System.Runtime.InteropServices;
using PathDock.Core.Localization;

namespace PathDock.App.Services;

/// <summary>
/// 시작 로딩 팝업. WPF가 아니라 순수 Win32 창(전용 스레드 + GDI + 타이머)으로 그린다.
/// 이유: 메인 창을 만드는 동안 WPF UI 스레드가 오래 막히는데, 팝업을 WPF 창으로 만들면(같은 스레드는 물론 별도 UI 스레드에서도)
/// 진행 막대 애니메이션과 문구 갱신이 같이 멈췄다(실측: 약 1초 간격으로만 갱신). WPF 렌더링과 무관한 이 창은 계속 움직인다.
/// Owner: App, Lifetime: 시작 후 약 1초까지(창이 닫히면 스레드도 끝난다). 공개 메서드는 아무 스레드에서나 호출할 수 있다.
/// </summary>
public sealed class SplashHost
{
    /// <summary>창 제목. UI 테스트가 메인 창("PathDock")과 구분하는 데 쓴다.</summary>
    public const string WindowTitle = "PathDock Loading";

    // 레이아웃(96 DPI 기준 픽셀, 실제 DPI에 맞춰 곱한다).
    private const int Width = 340, Height = 170;
    private const int IconSize = 44, IconX = 76, IconY = 22;
    private const int TrackX = 22, TrackY = 148, TrackW = 296, TrackH = 4, GlowW = 100;
    private const double SweepSeconds = 1.2;

    // 색(COLORREF 0x00BBGGRR).
    private static readonly uint BgColor = Rgb(0x1B, 0x24, 0x33);
    private static readonly uint BorderColor = Rgb(0x3B, 0x5A, 0x80);
    private static readonly uint TrackColor = Rgb(0x2A, 0x38, 0x50);
    private static readonly uint GlowColor = Rgb(0x4C, 0x9B, 0xFF);
    private static readonly uint TitleColor = Rgb(0xFF, 0xFF, 0xFF);
    private static readonly uint LoadingColor = Rgb(0xE3, 0xEA, 0xF5);
    private static readonly uint StatusColor = Rgb(0x9F, 0xB0, 0xC8);

    // 자식 컨트롤 ID. UI Automation의 AutomationId로 노출된다(테스트가 문구를 읽는 데 쓴다).
    public const int TitleId = 1001, LoadingId = 1002, StatusId = 1003;

    private const uint WmApp = 0x8000;
    private const uint WmSetStatus = WmApp + 1, WmCloseAfter = WmApp + 2, WmCloseNow = WmApp + 3;
    private const nuint BarTimerId = 1, CloseTimerId = 2;

    private static readonly TimeSpan MaxWaitForShown = TimeSpan.FromSeconds(5);

    /// <summary>팝업을 띄우지 못했을 때의 이유(성공하면 null). 진단 로그용.</summary>
    public volatile string? FailureReason;

    private readonly ManualResetEventSlim _shown = new(false);
    private readonly WndProc _wndProc; // 네이티브가 호출하므로 수명 동안 붙잡아 둔다.
    private volatile string _statusText = string.Empty;
    private volatile IntPtr _hwnd;

    private double _scale = 1.0;
    private IntPtr _icon, _bgBrush, _borderBrush, _trackBrush, _glowBrush;
    private IntPtr _fontTitle, _fontLoading, _fontStatus;
    private IntPtr _titleWnd, _loadingWnd, _statusWnd;
    private long _barStartTicks;

    public SplashHost() => _wndProc = WindowProc;

    /// <summary>팝업을 띄우고 화면에 그려질 때까지 기다린다(최대 5초 - 실패해도 앱 시작은 계속한다).</summary>
    public void Start()
    {
        var thread = new Thread(Run) { Name = "Splash", IsBackground = true };
        thread.Start();
        _shown.Wait(MaxWaitForShown);
    }

    /// <summary>"지금 하는 일" 한 줄을 바꾼다(기다리지 않는다).</summary>
    public void SetStatus(string text)
    {
        _statusText = text;
        IntPtr h = _hwnd;
        if (h != IntPtr.Zero)
        {
            PostMessage(h, WmSetStatus, UIntPtr.Zero, IntPtr.Zero);
        }
    }

    /// <summary>지정한 시간 뒤에 닫는다.</summary>
    public void CloseAfter(TimeSpan delay)
    {
        IntPtr h = _hwnd;
        if (h != IntPtr.Zero)
        {
            PostMessage(h, WmCloseAfter, (UIntPtr)(uint)Math.Max(1, (int)delay.TotalMilliseconds), IntPtr.Zero);
        }
    }

    /// <summary>바로 닫는다.</summary>
    public void CloseNow()
    {
        IntPtr h = _hwnd;
        if (h != IntPtr.Zero)
        {
            PostMessage(h, WmCloseNow, UIntPtr.Zero, IntPtr.Zero);
        }
    }

    // ----- 창 스레드 -----

    private void Run()
    {
        try
        {
            _scale = Math.Max(1.0, GetDpiForSystem() / 96.0);
            CreateResources();
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = GetModuleHandle(null),
                hCursor = LoadCursor(IntPtr.Zero, (IntPtr)IdcArrow),
                hbrBackground = _bgBrush,
                lpszClassName = "PathDockSplashWindow",
            };
            if (RegisterClassEx(ref wc) == 0)
            {
                FailureReason = $"RegisterClassEx 실패(오류 {Marshal.GetLastWin32Error()})";
                return;
            }
            int w = Scale(Width), h = Scale(Height);
            int x = (GetSystemMetrics(SmCxScreen) - w) / 2, y = (GetSystemMetrics(SmCyScreen) - h) / 2;
            // 최상단, 작업 표시줄에 안 나타남, 포커스를 가져가지 않음.
            IntPtr hwnd = CreateWindowEx(WsExTopmost | WsExToolWindow | WsExNoActivate, wc.lpszClassName, WindowTitle,
                WsPopup | WsClipChildren, x, y, w, h, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
            {
                FailureReason = $"CreateWindowEx 실패(오류 {Marshal.GetLastWin32Error()})";
                return;
            }
            _hwnd = hwnd;
            _barStartTicks = Environment.TickCount64;
            SetTimer(hwnd, BarTimerId, 16, IntPtr.Zero);
            ShowWindow(hwnd, SwShowNoActivate);
            UpdateWindow(hwnd); // 첫 그리기를 지금 끝낸다.
            _shown.Set();
            while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        catch (Exception ex)
        {
            // 팝업은 보조 기능이다: 실패해도 앱 시작은 계속하고 이유만 남긴다.
            FailureReason = ex.ToString();
        }
        finally
        {
            _hwnd = IntPtr.Zero;
            _shown.Set(); // 못 띄운 경우에도 앱 시작이 기다리지 않게.
            DestroyResources();
        }
    }

    private int Scale(int dip) => (int)Math.Round(dip * _scale);

    private void CreateResources()
    {
        _bgBrush = CreateSolidBrush(BgColor);
        _borderBrush = CreateSolidBrush(BorderColor);
        _trackBrush = CreateSolidBrush(TrackColor);
        _glowBrush = CreateSolidBrush(GlowColor);
        _fontTitle = CreateFont(-Scale(26), 600);
        _fontLoading = CreateFont(-Scale(13), 400);
        _fontStatus = CreateFont(-Scale(12), 400);
        // 앱 아이콘(실행 파일에 포함된 PathDock 아이콘)을 큰 크기로 가져온다.
        string? exe = Environment.ProcessPath;
        if (exe is not null && ExtractIconEx(exe, 0, out IntPtr large, IntPtr.Zero, 1) > 0)
        {
            _icon = large;
        }
    }

    private void DestroyResources()
    {
        foreach (IntPtr h in new[] { _bgBrush, _borderBrush, _trackBrush, _glowBrush, _fontTitle, _fontLoading, _fontStatus })
        {
            if (h != IntPtr.Zero)
            {
                DeleteObject(h);
            }
        }
        if (_icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
        }
        _bgBrush = _borderBrush = _trackBrush = _glowBrush = _fontTitle = _fontLoading = _fontStatus = _icon = IntPtr.Zero;
    }

    private IntPtr AddLabel(IntPtr parent, string text, int id, uint align, IntPtr font, int x, int y, int w, int h)
    {
        IntPtr label = CreateWindowEx(0, "STATIC", text, WsChild | WsVisible | SsNoPrefix | align,
            Scale(x), Scale(y), Scale(w), Scale(h), parent, (IntPtr)id, GetModuleHandle(null), IntPtr.Zero);
        SendMessage(label, WmSetFont, font, (IntPtr)1);
        return label;
    }

    private IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WmCreate:
                _titleWnd = AddLabel(hwnd, "PathDock", TitleId, SsLeft, _fontTitle, IconX + IconSize + 14, 28, 190, 36);
                _loadingWnd = AddLabel(hwnd, Loc.T("로딩 중...", "Loading..."), LoadingId, SsCenter, _fontLoading, 1, 84, Width - 2, 20); // 좌우 1px은 테두리 자리
                _statusWnd = AddLabel(hwnd, _statusText, StatusId, SsCenter | SsEndEllipsis, _fontStatus, 12, 108, Width - 24, 18);
                return IntPtr.Zero;

            case WmCtlColorStatic:
            {
                IntPtr child = lParam;
                SetTextColor(wParam, child == _titleWnd ? TitleColor : child == _loadingWnd ? LoadingColor : StatusColor);
                SetBkColor(wParam, BgColor);
                return _bgBrush;
            }

            case WmEraseBkgnd:
                return (IntPtr)1; // 배경은 WM_PAINT에서 한 번에 그린다(깜빡임 방지).

            case WmPaint:
                Paint(hwnd);
                return IntPtr.Zero;

            case WmTimer:
                if ((nuint)wParam == BarTimerId)
                {
                    var bar = new RECT { left = Scale(TrackX), top = Scale(TrackY), right = Scale(TrackX + TrackW), bottom = Scale(TrackY + TrackH) };
                    InvalidateRect(hwnd, ref bar, false);
                }
                else if ((nuint)wParam == CloseTimerId)
                {
                    KillTimer(hwnd, CloseTimerId);
                    DestroyWindow(hwnd);
                }
                return IntPtr.Zero;

            case WmSetStatus:
                SetWindowText(_statusWnd, _statusText);
                return IntPtr.Zero;

            case WmCloseAfter:
                SetTimer(hwnd, CloseTimerId, (uint)(nuint)wParam, IntPtr.Zero);
                return IntPtr.Zero;

            case WmCloseNow:
                DestroyWindow(hwnd);
                return IntPtr.Zero;

            case WmDestroy:
                KillTimer(hwnd, BarTimerId);
                _hwnd = IntPtr.Zero;
                PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    /// <summary>배경/테두리/아이콘/진행 막대를 메모리 DC에 그린 뒤 한 번에 화면으로 복사한다.</summary>
    private void Paint(IntPtr hwnd)
    {
        IntPtr hdc = BeginPaint(hwnd, out PAINTSTRUCT ps);
        GetClientRect(hwnd, out RECT rc);
        int w = rc.right - rc.left, h = rc.bottom - rc.top;
        IntPtr mem = CreateCompatibleDC(hdc);
        IntPtr bmp = CreateCompatibleBitmap(hdc, w, h);
        IntPtr old = SelectObject(mem, bmp);

        FillRect(mem, ref rc, _bgBrush);
        FrameRect(mem, ref rc, _borderBrush);
        if (_icon != IntPtr.Zero)
        {
            DrawIconEx(mem, Scale(IconX), Scale(IconY), _icon, Scale(IconSize), Scale(IconSize), 0, IntPtr.Zero, DiNormal);
        }
        var track = new RECT { left = Scale(TrackX), top = Scale(TrackY), right = Scale(TrackX + TrackW), bottom = Scale(TrackY + TrackH) };
        FillRect(mem, ref track, _trackBrush);
        // 밝은 조각이 막대 왼쪽 밖에서 오른쪽 밖까지 계속 반복해 흐른다.
        double phase = (Environment.TickCount64 - _barStartTicks) / 1000.0 / SweepSeconds % 1.0;
        int glowLeft = track.left + (int)Math.Round((-GlowW + phase * (TrackW + GlowW)) * _scale);
        var glow = new RECT { left = Math.Max(track.left, glowLeft), top = track.top, right = Math.Min(track.right, glowLeft + Scale(GlowW)), bottom = track.bottom };
        if (glow.right > glow.left)
        {
            FillRect(mem, ref glow, _glowBrush);
        }

        BitBlt(hdc, 0, 0, w, h, mem, 0, 0, SrcCopy);
        SelectObject(mem, old);
        DeleteObject(bmp);
        DeleteDC(mem);
        EndPaint(hwnd, ref ps);
    }

    private IntPtr CreateFont(int height, int weight) =>
        CreateFontW(height, 0, 0, 0, weight, 0, 0, 0, 1 /*DEFAULT_CHARSET*/, 0, 0, 5 /*CLEARTYPE_QUALITY*/, 0, "Segoe UI");

    private static uint Rgb(byte r, byte g, byte b) => (uint)(r | (g << 8) | (b << 16));

    // ----- Win32 -----

    private const uint WmCreate = 0x0001, WmDestroy = 0x0002, WmPaint = 0x000F, WmEraseBkgnd = 0x0014, WmSetFont = 0x0030, WmTimer = 0x0113, WmCtlColorStatic = 0x0138;
    private const uint WsPopup = 0x80000000, WsChild = 0x40000000, WsVisible = 0x10000000, WsClipChildren = 0x02000000;
    private const uint WsExTopmost = 0x00000008, WsExToolWindow = 0x00000080, WsExNoActivate = 0x08000000;
    private const uint SsLeft = 0x0, SsCenter = 0x1, SsNoPrefix = 0x80, SsEndEllipsis = 0x4000;
    private const int SwShowNoActivate = 4, SmCxScreen = 0, SmCyScreen = 1, IdcArrow = 32512;
    private const uint SrcCopy = 0x00CC0020, DiNormal = 3;

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public int fErase;
        public RECT rcPaint;
        public int fRestore, fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    // 유니코드(W) 버전이어야 한다: ANSI 기본 프로시저(DefWindowProcA)로 보내면 창 제목이 첫 글자만 남는다.
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] private static extern bool UpdateWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern UIntPtr SetTimer(IntPtr hWnd, UIntPtr id, uint ms, IntPtr proc);
    [DllImport("user32.dll")] private static extern bool KillTimer(IntPtr hWnd, UIntPtr id);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr hWnd, ref RECT rect, bool erase);
    [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT ps);
    [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern int FillRect(IntPtr hdc, ref RECT rect, IntPtr brush);
    [DllImport("user32.dll")] private static extern int FrameRect(IntPtr hdc, ref RECT rect, IntPtr brush);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr icon, int cx, int cy, uint step, IntPtr flicker, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] private static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetWindowText(IntPtr hWnd, string text);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint ExtractIconEx(string file, int index, out IntPtr large, IntPtr small, uint count);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr hdc, uint color);
    [DllImport("gdi32.dll")] private static extern uint SetBkColor(IntPtr hdc, uint color);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);
}
