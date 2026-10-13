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

    public TerminalSession Session { get; }

    /// <summary>하단 상태 문구(실행 중/대기 중/종료 등).</summary>
    [ObservableProperty]
    private string _statusText = "터미널 시작 중...";

    /// <summary>탐색기에서 온 이동이 터미널 실행 중이라 보류 중인지(상태 문구를 강조 표시).</summary>
    [ObservableProperty]
    private bool _isPending;

    /// <summary>터미널 글자 크기(DIP). TerminalControl에 바인딩된다.</summary>
    [ObservableProperty]
    private double _fontSize = PathDock.Core.Settings.AppSettings.DefaultTerminalFontSize;

    /// <summary>패널을 접었는지(머리글만 보임). 화면 배치는 MainWindow가 이 값을 보고 바꾼다.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CollapseToolTip))]
    private bool _isCollapsed;

    public string CollapseToolTip => IsCollapsed ? "터미널 펼치기" : "터미널 접기";

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ToggleCollapsed() => IsCollapsed = !IsCollapsed;

    /// <summary>경로 변경 보고(UI 스레드). 두 번째 인자: 앱이 주입한 cd로 인한 변경이면 true.</summary>
    public event Action<string, bool>? CwdChanged;

    public TerminalViewModel(ILog log)
    {
        _ui = Application.Current.Dispatcher;
        _log = log;
        Session = new TerminalSession(DefaultCols, DefaultRows, log);
        Session.CwdChanged += (p, fromRequest) => _ui.BeginInvoke(() => CwdChanged?.Invoke(p, fromRequest));
        Session.IdleChanged += idle => _ui.BeginInvoke(() =>
        {
            StatusText = idle ? "터미널: 대기 중(입력 가능)" : "터미널: 명령 실행 중";
            if (idle)
            {
                IsPending = false;
            }
        });
        Session.Exited += code => _ui.BeginInvoke(() => StatusText = $"터미널 종료됨 (코드 {code})");
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
