using System.Text;
using PathDock.Core.Logging;
using PathDock.Core.Paths;

namespace PathDock.Core.Terminal;

/// <summary>
/// 터미널 세션 한 개: ConPTY + VT 파서 + 화면 버퍼 + 셸 상태(Idle/Busy, 현재 경로)를 묶는다.
/// Owner: TerminalViewModel. Lifetime: Start ~ Dispose(창과 동일).
/// 스레드: 이벤트(CwdChanged/IdleChanged/Exited/ScreenChanged)는 ConPTY 읽기 스레드나 호출 스레드에서 발생할 수 있으므로
/// 구독자가 UI 스레드로 옮겨야 한다. 상태 필드(_idle/_cwd/_pending)는 <see cref="_stateLock"/>으로 보호한다.
/// </summary>
public sealed class TerminalSession : IDisposable
{
    /// <summary>
    /// PowerShell 프롬프트 훅(doc/02 3절). 프롬프트가 그려질 때마다 창 제목을 "PD:번호:경로"로 바꿔 VtParser에 알린다.
    /// 번호는 매번 증가하므로 경로가 같아도 conhost가 제목 변경을 다시 전달한다(= 프롬프트 대기 신호). ASCII만 사용(5.1 파일 인코딩 안전).
    /// </summary>
    public const string ShellIntegrationScript = """
        $global:__pdN = 0
        $global:__pdOrigPrompt = $function:prompt
        function global:prompt {
            $p = ''
            try { if ((Get-Location).Provider.Name -eq 'FileSystem') { $p = (Get-Location).ProviderPath } } catch {}
            $global:__pdN++
            $Host.UI.RawUI.WindowTitle = "PD:$($global:__pdN):$p"
            & $global:__pdOrigPrompt
        }
        """;

    private readonly ILog _log;
    private readonly object _stateLock = new();
    private readonly ConPtySession _pty;
    private readonly VtParser _parser;
    private bool _idle;
    private string? _cwd;
    private string? _pending;
    private string? _expectedCwd;
    private bool _disposed;
    private string? _scriptPath;

    public ScreenBuffer Screen { get; }

    /// <summary>경로 변경 보고. 두 번째 인자는 앱이 주입한 cd로 인한 변경이면 true(재전파 금지 판정용).</summary>
    public event Action<string, bool>? CwdChanged;
    /// <summary>Idle(프롬프트 대기) 여부가 바뀜.</summary>
    public event Action<bool>? IdleChanged;
    public event Action<int>? Exited;
    /// <summary>화면 내용이 바뀌었을 수 있음(UI는 Screen.Version으로 확인).</summary>
    public event Action? ScreenChanged;

    public bool IsIdle
    {
        get { lock (_stateLock) { return _idle; } }
    }

    public string? Cwd
    {
        get { lock (_stateLock) { return _cwd; } }
    }

    public TerminalSession(int cols, int rows, ILog log)
    {
        _log = log;
        Screen = new ScreenBuffer(cols, rows);
        _parser = new VtParser(Screen) { Reply = SendRaw };
        _parser.CwdReported += OnCwdReported;
        _parser.PromptShown += OnPromptShown;
        _pty = new ConPtySession(OnOutput, OnExit);
    }

    /// <summary>PowerShell을 시작 폴더 <paramref name="workingDirectory"/>에서 실행한다.</summary>
    public void Start(string? workingDirectory)
    {
        // -EncodedCommand는 PowerShell 5.1이 CLIXML을 출력해 화면이 오염되므로 임시 스크립트 파일을 -File로 실행한다.
        string dir = Path.Combine(Path.GetTempPath(), "PathDock");
        Directory.CreateDirectory(dir);
        _scriptPath = Path.Combine(dir, $"shell-integration-{Environment.ProcessId}-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(_scriptPath, ShellIntegrationScript);
        string cmd = $"powershell.exe -NoLogo -NoExit -ExecutionPolicy Bypass -File \"{_scriptPath}\"";
        _log.Info($"터미널 시작: cwd={workingDirectory}");
        _pty.Start(cmd, workingDirectory, Screen.Cols, Screen.Rows);
    }

    private void OnOutput(byte[] data, int count)
    {
        _parser.Feed(data, count);
        ScreenChanged?.Invoke();
    }

    private void OnExit(int code)
    {
        _log.Info($"터미널 종료: exit={code}");
        Exited?.Invoke(code);
    }

    private void OnCwdReported(string path)
    {
        string? normalized = PathNormalizer.Normalize(path);
        if (normalized is null)
        {
            return;
        }
        bool fromRequest;
        lock (_stateLock)
        {
            if (PathNormalizer.AreSame(_cwd, normalized))
            {
                return;
            }
            _cwd = normalized;
            fromRequest = PathNormalizer.AreSame(_expectedCwd, normalized);
            if (fromRequest)
            {
                _expectedCwd = null;
            }
            else
            {
                // 사용자가 직접 바꾼 경로.
            }
        }
        CwdChanged?.Invoke(normalized, fromRequest);
    }

    private void OnPromptShown()
    {
        string? toSend = null;
        lock (_stateLock)
        {
            _idle = true;
            if (_pending is not null)
            {
                if (PathNormalizer.AreSame(_pending, _cwd))
                {
                    _pending = null; // 그 사이 이미 같은 위치가 되었다.
                }
                else
                {
                    toSend = _pending;
                    _pending = null;
                }
            }
            else
            {
                // 보류된 이동 없음.
            }
        }
        IdleChanged?.Invoke(true);
        if (toSend is not null)
        {
            InjectChangeDirectory(toSend);
        }
        else
        {
            // 보낼 것 없음.
        }
    }

    /// <summary>
    /// 앱이 만든 입력 전송(키 입력 등). Enter(\r)가 포함되면 Busy로 전환한다 - 다음 프롬프트까지 주입을 보류한다.
    /// </summary>
    public void SendInput(string text)
    {
        if (text.Contains('\r'))
        {
            SetBusy();
        }
        else
        {
            // 줄 편집 중이므로 상태 변화 없음.
        }
        SendRaw(text);
    }

    private void SetBusy()
    {
        bool changed;
        lock (_stateLock)
        {
            changed = _idle;
            _idle = false;
        }
        if (changed)
        {
            IdleChanged?.Invoke(false);
        }
        else
        {
            // 이미 Busy.
        }
    }

    private void SendRaw(string text)
    {
        _pty.Write(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    /// 탐색기에서 온 경로 변경 요청. Idle이면 즉시 cd를 주입(true), Busy이면 마지막 요청 하나만 보류(false)했다가
    /// 다음 프롬프트에서 반영한다. 경로 존재 확인은 호출 측(PathSyncService) 책임이다.
    /// </summary>
    public bool RequestChangeDirectory(string path)
    {
        lock (_stateLock)
        {
            if (!_idle)
            {
                _pending = path;
                _log.Info($"터미널 Busy - 이동 보류: {path}");
                return false;
            }
        }
        InjectChangeDirectory(path);
        return true;
    }

    /// <summary>보류 중인 요청이 있는지(UI의 "대기 중" 표시용).</summary>
    public string? PendingPath
    {
        get { lock (_stateLock) { return _pending; } }
    }

    private void InjectChangeDirectory(string path)
    {
        lock (_stateLock)
        {
            _expectedCwd = path;
        }
        string quoted = path.Replace("'", "''");
        _log.Info($"터미널 cd 주입: {path}");
        SendInput($"Set-Location -LiteralPath '{quoted}'\r");
    }

    public void Resize(int cols, int rows)
    {
        lock (Screen.SyncRoot)
        {
            Screen.Resize(cols, rows);
        }
        _pty.Resize(cols, rows);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _pty.Dispose();
        if (_scriptPath is not null)
        {
            try
            {
                File.Delete(_scriptPath);
            }
            catch (IOException)
            {
                // 임시 파일 정리 실패는 무시한다(다음 실행의 고유 이름과 충돌하지 않음).
            }
        }
        else
        {
            // 시작 전에 해제됨.
        }
    }
}
