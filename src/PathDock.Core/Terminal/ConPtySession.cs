using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PathDock.Core.Terminal;

/// <summary>
/// Windows Pseudo Console(ConPTY) 위에서 프로세스 하나를 실행하고 바이트 입출력을 중계한다.
/// Owner: TerminalSession. Lifetime: Start ~ Dispose.
/// 스레드: 전용 읽기 스레드 1개가 출력을 콜백으로 전달, 전용 감시 스레드 1개가 프로세스 종료를 기다린다.
/// Write/Resize는 임의 스레드에서 호출 가능(Write는 lock으로 직렬화). Dispose는 한 번만 의미가 있다.
/// </summary>
public sealed class ConPtySession : IDisposable
{
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const int ProcThreadAttributePseudoConsole = 0x00020016;
    private const int ReadBufferSize = 8192;
    private const int StartfUseStdHandles = 0x00000100;
    private const uint InfiniteWait = 0xFFFFFFFF;

    private readonly object _writeLock = new();
    private readonly Action<byte[], int> _onOutput;
    private readonly Action<int> _onExit;
    private IntPtr _hPC;
    private IntPtr _attrList;
    private IntPtr _hProcess;
    private IntPtr _hThread;
    private SafeFileHandle? _inWrite;
    private SafeFileHandle? _outRead;
    private FileStream? _writeStream;
    private volatile bool _disposed;

    /// <param name="onOutput">출력 청크 콜백(읽기 스레드에서 호출).</param>
    /// <param name="onExit">프로세스 종료 콜백(감시 스레드에서 호출, 종료 코드).</param>
    public ConPtySession(Action<byte[], int> onOutput, Action<int> onExit)
    {
        _onOutput = onOutput;
        _onExit = onExit;
    }

    /// <summary>현재 프로세스의 환경 변수에 <paramref name="extra"/>를 덮어쓴 환경 블록(유니코드, 이름순, 이중 NUL 종료)을 만든다.</summary>
    internal static string BuildEnvironmentBlock(IReadOnlyDictionary<string, string> extra)
    {
        var vars = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
        {
            vars[(string)e.Key] = (string?)e.Value ?? string.Empty;
        }
        foreach (KeyValuePair<string, string> kv in extra)
        {
            vars[kv.Key] = kv.Value;
        }
        var sb = new System.Text.StringBuilder();
        foreach (KeyValuePair<string, string> kv in vars)
        {
            sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\0');
        }
        return sb.Append('\0').ToString();
    }

    /// <summary>
    /// 의사 콘솔을 만들고 <paramref name="commandLine"/>을 실행한다. 실패 시 Win32Exception.
    /// <paramref name="extraEnvironment"/>가 있으면 현재 환경에 덮어써서 자식 프로세스에 준다(이 앱의 환경은 바꾸지 않는다).
    /// </summary>
    public void Start(string commandLine, string? workingDirectory, int cols, int rows, uint flags = 0, IReadOnlyDictionary<string, string>? extraEnvironment = null)
    {
        if (!CreatePipe(out SafeFileHandle inRead, out SafeFileHandle inWrite, IntPtr.Zero, 0)
            || !CreatePipe(out SafeFileHandle outRead, out SafeFileHandle outWrite, IntPtr.Zero, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe 실패");
        }

        int hr = CreatePseudoConsole(new Coord((short)cols, (short)rows), inRead, outWrite, flags, out _hPC);
        if (hr != 0)
        {
            throw new Win32Exception(hr, "CreatePseudoConsole 실패");
        }

        // 의사 콘솔 쪽 끝점은 자식이 상속받으므로 우리 쪽 사본은 닫는다.
        inRead.Dispose();
        outWrite.Dispose();
        _inWrite = inWrite;
        _outRead = outRead;
        _writeStream = new FileStream(inWrite, FileAccess.Write, 1, false);

        var si = new StartupInfoEx();
        si.StartupInfo.cb = Marshal.SizeOf<StartupInfoEx>();
        si.StartupInfo.dwFlags = StartfUseStdHandles;
        IntPtr size = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
        _attrList = Marshal.AllocHGlobal(size);
        if (!InitializeProcThreadAttributeList(_attrList, 1, 0, ref size)
            || !UpdateProcThreadAttribute(_attrList, 0, (IntPtr)ProcThreadAttributePseudoConsole, _hPC, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "프로세스 속성 설정 실패");
        }
        si.lpAttributeList = _attrList;

        IntPtr env = extraEnvironment is null ? IntPtr.Zero : Marshal.StringToHGlobalUni(BuildEnvironmentBlock(extraEnvironment));
        ProcessInformation pi;
        try
        {
            if (!CreateProcessW(null, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                    ExtendedStartupInfoPresent | CreateUnicodeEnvironment, env, workingDirectory, ref si, out pi))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcess 실패: " + commandLine);
            }
        }
        finally
        {
            if (env != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(env);
            }
        }
        _hProcess = pi.hProcess;
        _hThread = pi.hThread;

        new Thread(ReadLoop) { IsBackground = true, Name = "PathDock.ConPtyRead" }.Start();
        new Thread(WaitLoop) { IsBackground = true, Name = "PathDock.ConPtyWait" }.Start();
    }

    private void ReadLoop()
    {
        var buf = new byte[ReadBufferSize];
        try
        {
            using var stream = new FileStream(_outRead!, FileAccess.Read, 1, false);
            int n;
            while (!_disposed && (n = stream.Read(buf, 0, buf.Length)) > 0)
            {
                _onOutput(buf, n);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // 파이프가 닫힘 = 세션 종료. 정상 흐름이다.
        }
    }

    private void WaitLoop()
    {
        WaitForSingleObject(_hProcess, InfiniteWait);
        GetExitCodeProcess(_hProcess, out uint code);
        if (!_disposed)
        {
            _onExit((int)code);
        }
        else
        {
            // Dispose로 종료시킨 경우에는 종료 콜백을 부르지 않는다.
        }
    }

    /// <summary>입력 바이트를 셸에 보낸다(스레드 안전). 세션이 끝났으면 무시한다.</summary>
    public void Write(byte[] data)
    {
        lock (_writeLock)
        {
            if (_disposed || _writeStream is null)
            {
                return;
            }
            try
            {
                _writeStream.Write(data, 0, data.Length);
                _writeStream.Flush();
            }
            catch (IOException)
            {
                // 셸이 이미 종료됨.
            }
        }
    }

    public void Resize(int cols, int rows)
    {
        if (!_disposed && _hPC != IntPtr.Zero)
        {
            ResizePseudoConsole(_hPC, new Coord((short)Math.Max(1, cols), (short)Math.Max(1, rows)));
        }
        else
        {
            // 세션이 없거나 종료됨.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        if (_hProcess != IntPtr.Zero)
        {
            TerminateProcess(_hProcess, 0);
        }
        // ClosePseudoConsole은 출력 파이프가 비워질 때까지 블록할 수 있어 별도 스레드에서 수행한다(종료 지연 방지).
        IntPtr hPC = _hPC;
        IntPtr attr = _attrList;
        IntPtr hProcess = _hProcess;
        IntPtr hThread = _hThread;
        SafeFileHandle? outRead = _outRead;
        _writeStream?.Dispose();
        _inWrite?.Dispose();
        Task.Run(() =>
        {
            if (hPC != IntPtr.Zero)
            {
                ClosePseudoConsole(hPC);
            }
            outRead?.Dispose();
            if (attr != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(attr);
                Marshal.FreeHGlobal(attr);
            }
            if (hThread != IntPtr.Zero)
            {
                CloseHandle(hThread);
            }
            if (hProcess != IntPtr.Zero)
            {
                CloseHandle(hProcess);
            }
        });
    }

    #region Win32

    [StructLayout(LayoutKind.Sequential)]
    private struct Coord
    {
        public short X;
        public short Y;
        public Coord(short x, short y) { X = x; Y = y; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, IntPtr attrs, int size);

    [DllImport("kernel32.dll")]
    private static extern int CreatePseudoConsole(Coord size, SafeFileHandle hInput, SafeFileHandle hOutput, uint flags, out IntPtr phPC);

    [DllImport("kernel32.dll")]
    private static extern int ResizePseudoConsole(IntPtr hPC, Coord size);

    [DllImport("kernel32.dll")]
    private static extern void ClosePseudoConsole(IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr prev, IntPtr retSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr list);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(string? app, string cmd, IntPtr procAttrs, IntPtr threadAttrs, bool inherit,
        uint flags, IntPtr env, string? cwd, ref StartupInfoEx si, out ProcessInformation pi);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr handle, uint ms);

    [DllImport("kernel32.dll")]
    private static extern bool GetExitCodeProcess(IntPtr handle, out uint code);

    [DllImport("kernel32.dll")]
    private static extern bool TerminateProcess(IntPtr handle, uint code);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    #endregion
}
