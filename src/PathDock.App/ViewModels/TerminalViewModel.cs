using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using PathDock.Core.Logging;
using PathDock.Core.Sync;
using PathDock.Core.Terminal;

namespace PathDock.App.ViewModels;

/// <summary>
/// 터미널 패널의 ViewModel. Owner: MainViewModel, Lifetime: 앱 전체(Dispose에서 PowerShell 종료).
/// <see cref="TerminalSession"/>의 이벤트는 ConPTY 읽기 스레드에서 오므로 여기서 UI 스레드로 옮겨 외부에 전달한다.
/// 동기화 대상(<see cref="IPathSyncTarget"/>)으로서 탐색기의 이동 요청을 세션에 전달한다.
/// </summary>
public sealed partial class TerminalViewModel : ObservableObject, IPathSyncTarget, IDisposable
{
    private const int DefaultCols = 120;
    private const int DefaultRows = 30;

    private readonly Dispatcher _ui;
    private readonly ILog _log;
    private readonly Services.IDialogService _dialogs;

    /// <summary>현재 셸 세션. 셸 종류를 바꾸면 새 세션으로 교체된다(TerminalControl이 바인딩).</summary>
    [ObservableProperty]
    private TerminalSession _session;

    /// <summary>하단 상태 문구(실행 중/대기 중/종료 등).</summary>
    [ObservableProperty]
    private string _statusText = "터미널 시작 중...";

    /// <summary>탐색기에서 온 이동이 터미널 실행 중이라 보류 중인지(상태 문구를 강조 표시).</summary>
    [ObservableProperty]
    private bool _isPending;

    /// <summary>터미널 글자 크기(DIP). TerminalControl에 바인딩된다.</summary>
    [ObservableProperty]
    private double _fontSize = PathDock.Core.Settings.AppSettings.DefaultTerminalFontSize;

    /// <summary>터미널 색 구성. TerminalControl에 바인딩된다.</summary>
    [ObservableProperty]
    private TerminalColorScheme _colorScheme = TerminalThemes.All[0];

    /// <summary>패널을 접었는지(머리글만 보임). 화면 배치는 MainWindow가 이 값을 보고 바꾼다.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CollapseToolTip))]
    private bool _isCollapsed;

    public string CollapseToolTip => IsCollapsed ? "터미널 펼치기" : "터미널 접기";

    /// <summary>사용자에게 알릴 결과 메시지(저장 성공/실패). MainViewModel이 하단 안내줄에 표시한다.</summary>
    public event Action<string, bool>? Notice;

    /// <summary>터미널 전체 내용(스크롤백 포함)을 사용자가 고른 파일에 저장한다.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void SaveOutput()
    {
        string? path = _dialogs.PickSaveFile("터미널 내용 저장", $"terminal-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        if (path is null)
        {
            return;
        }
        try
        {
            System.IO.File.WriteAllText(path, Session.Screen.GetAllText() + "\r\n", new System.Text.UTF8Encoding(true));
            _log.Info($"터미널 내용 저장: {path}");
            Notice?.Invoke($"터미널 내용을 저장했습니다: {path}", true);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            _log.Warn($"터미널 내용 저장 실패: {path} - {ex.Message}");
            Notice?.Invoke($"저장하지 못했습니다: {ex.Message}", false);
        }
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ToggleCollapsed() => IsCollapsed = !IsCollapsed;

    /// <summary>경로 변경 보고(UI 스레드). 두 번째 인자: 앱이 주입한 cd로 인한 변경이면 true.</summary>
    public event Action<string, bool>? CwdChanged;

    public TerminalViewModel(ILog log, Services.IDialogService dialogs)
    {
        _dialogs = dialogs;
        _ui = Application.Current.Dispatcher;
        _log = log;
        _session = CreateSession();
    }

    /// <summary>세션을 만들고 이벤트를 연결한다. 교체되어 버려진 세션의 이벤트는 무시한다.</summary>
    private TerminalSession CreateSession()
    {
        var s = new TerminalSession(DefaultCols, DefaultRows, _log);
        s.CwdChanged += (p, fromRequest) => _ui.BeginInvoke(() =>
        {
            if (ReferenceEquals(s, Session))
            {
                CwdChanged?.Invoke(p, fromRequest);
            }
        });
        s.IdleChanged += idle => _ui.BeginInvoke(() =>
        {
            if (!ReferenceEquals(s, Session))
            {
                return;
            }
            StatusText = idle ? "터미널: 대기 중(입력 가능)" : "터미널: 명령 실행 중";
            if (idle)
            {
                IsPending = false;
            }
        });
        s.Exited += code => _ui.BeginInvoke(() =>
        {
            if (ReferenceEquals(s, Session))
            {
                StatusText = $"터미널 종료됨 (코드 {code})";
            }
        });
        return s;
    }

    /// <summary>현재 터미널을 닫고 다른 셸을 같은 폴더에서 새로 시작한다.</summary>
    public void RestartShell(string? workingDirectory, TerminalShell shell)
    {
        TerminalSession old = Session;
        Session = CreateSession();
        StatusText = "터미널 시작 중...";
        IsPending = false;
        Start(workingDirectory, shell);
        old.Dispose();
    }

    /// <summary>패널 머리글에 표시할 셸 이름(Start에서 정해진다).</summary>
    [ObservableProperty]
    private string _header = "터미널 - PowerShell";

    public void Start(string? workingDirectory, TerminalShell shell = TerminalShell.PowerShell)
    {
        Header = shell == TerminalShell.CommandPrompt ? "터미널 - 명령 프롬프트" : "터미널 - PowerShell";
        Session.Start(workingDirectory, shell);
    }

    public string? CurrentPath => Session.Cwd;

    /// <summary>탐색기에서 온 이동 요청. Busy이면 보류하고 상태 문구에 표시한다.</summary>
    public bool RequestChange(string path)
    {
        bool delivered = Session.RequestChangeDirectory(path);
        if (!delivered)
        {
            StatusText = $"터미널 실행 중 - 이동 대기: {path}";
            IsPending = true;
        }
        else
        {
            // 즉시 전달됨.
        }
        return delivered;
    }

    public void Dispose()
    {
        _log.Info("터미널 종료 요청");
        Session.Dispose();
    }
}
