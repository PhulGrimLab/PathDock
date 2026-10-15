using CommunityToolkit.Mvvm.ComponentModel;
using PathDock.Core.Terminal;

namespace PathDock.App.ViewModels;

/// <summary>탭 하나의 셸 상태(상태 문구는 <see cref="TerminalViewModel"/>이 현재 언어로 만든다).</summary>
public enum TerminalTabStatus
{
    Starting,
    Idle,
    Running,
    Exited,
    WaitingToMove,
}

/// <summary>
/// 터미널 탭 하나: 독립된 셸 세션과 그 상태. Owner: TerminalViewModel, Lifetime: 탭을 닫을 때까지.
/// 모든 속성은 UI 스레드에서만 바꾼다(세션 이벤트는 TerminalViewModel이 UI 스레드로 옮겨 전달한다).
/// </summary>
public sealed partial class TerminalTab : ObservableObject
{
    public TerminalTab(int number, TerminalSession session, TerminalShell shell)
    {
        Number = number;
        Session = session;
        _shell = shell;
    }

    /// <summary>탭 번호(1부터, 닫아도 다시 쓰지 않는다). UI 자동화 식별자에 쓴다.</summary>
    public int Number { get; }

    public string AutomationId => $"TerminalTab_{Number}";

    public string CloseAutomationId => $"TerminalTabClose_{Number}";

    /// <summary>이 탭의 셸 세션. 셸 종류를 바꾸면 새 세션으로 교체된다.</summary>
    public TerminalSession Session { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private TerminalShell _shell;

    /// <summary>마지막으로 보고된 현재 폴더(아직 모르면 null). 제목과 툴팁에 쓴다.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private string? _cwd;

    public string Title => TerminalTabTitle.Build(Shell, Cwd);

    [ObservableProperty]
    private bool _isSelected;

    public TerminalTabStatus Status { get; set; } = TerminalTabStatus.Starting;

    /// <summary>종료 코드, 대기 중인 경로 같은 상태 보조값.</summary>
    public string StatusDetail { get; set; } = string.Empty;

    /// <summary>탐색기에서 온 이동이 이 탭의 실행 중 명령 때문에 보류 중인지.</summary>
    public bool IsPending { get; set; }
}
