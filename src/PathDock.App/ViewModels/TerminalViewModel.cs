using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PathDock.Core.Localization;
using PathDock.Core.Logging;
using PathDock.Core.Sync;
using PathDock.Core.Terminal;

namespace PathDock.App.ViewModels;

/// <summary>
/// 터미널 패널의 ViewModel. Owner: MainViewModel, Lifetime: 앱 전체(Dispose에서 모든 셸 종료).
/// 탭마다 독립된 <see cref="TerminalSession"/>을 가지며, 화면(<see cref="Session"/>)과 경로 동기화는 선택된 탭만 따른다.
/// <see cref="TerminalSession"/>의 이벤트는 ConPTY 읽기 스레드에서 오므로 여기서 UI 스레드로 옮겨 외부에 전달한다.
/// 동기화 대상(<see cref="IPathSyncTarget"/>)으로서 탐색기의 이동 요청을 선택된 탭의 세션에 전달한다.
/// </summary>
public sealed partial class TerminalViewModel : ObservableObject, IPathSyncTarget, IDisposable
{
    private const int DefaultCols = 120;
    private const int DefaultRows = 30;

    /// <summary>한 번에 열 수 있는 탭 수(탭 줄이 좁은 창에서도 한 줄에 들어가도록 제한).</summary>
    public const int MaxTabs = 8;

    private readonly Dispatcher _ui;
    private readonly ILog _log;
    private readonly Services.IDialogService _dialogs;

    /// <summary>화면에 보이는(선택된) 탭의 셸 세션. 탭을 바꾸거나 셸 종류를 바꾸면 교체된다(TerminalControl이 바인딩).</summary>
    [ObservableProperty]
    private TerminalSession _session;

    /// <summary>열려 있는 탭들(왼쪽부터). 항상 1개 이상.</summary>
    public System.Collections.ObjectModel.ObservableCollection<TerminalTab> Tabs { get; } = new();

    /// <summary>선택된 탭. 바뀌면 화면에 그 탭의 세션을 보여주고 탐색기를 그 탭의 폴더로 따라가게 한다.</summary>
    [ObservableProperty]
    private TerminalTab _selectedTab;

    /// <summary>탭이 둘 이상이면 닫기 버튼을 보여준다(마지막 탭은 닫을 수 없다).</summary>
    public bool HasMultipleTabs => Tabs.Count > 1;

    /// <summary>새 탭을 시작할 폴더를 돌려주는 함수(MainViewModel이 탐색기 현재 폴더를 연결). 없으면 선택된 탭의 폴더.</summary>
    public Func<string?>? StartDirectoryProvider { get; set; }

    /// <summary>새 탭이 쓸 셸(설정의 셸).</summary>
    public TerminalShell DefaultShell { get; private set; } = TerminalShell.PowerShell;

    private int _nextTabNumber = 1;

    /// <summary>하단 상태 문구(실행 중/대기 중/종료 등). 상태와 세부값(종료 코드, 대기 중인 경로)만 저장하고 문구는 현재 언어로 만든다.</summary>
    public string StatusText => SelectedTab.Status switch
    {
        TerminalTabStatus.Idle => Loc.T("터미널: 대기 중(입력 가능)", "Terminal: idle (ready for input)"),
        TerminalTabStatus.Running => Loc.T("터미널: 명령 실행 중", "Terminal: running a command"),
        TerminalTabStatus.Exited => Loc.T($"터미널 종료됨 (코드 {SelectedTab.StatusDetail})", $"Terminal exited (code {SelectedTab.StatusDetail})"),
        TerminalTabStatus.WaitingToMove => Loc.T($"터미널 실행 중 - 이동 대기: {SelectedTab.StatusDetail}", $"Terminal busy - waiting to move: {SelectedTab.StatusDetail}"),
        _ => Loc.T("터미널 시작 중...", "Starting terminal..."),
    };

    /// <summary>탭의 상태를 바꾼다. 선택된 탭이면 상태 문구도 갱신한다.</summary>
    private void SetStatus(TerminalTab tab, TerminalTabStatus status, string detail = "")
    {
        tab.Status = status;
        tab.StatusDetail = detail;
        if (ReferenceEquals(tab, SelectedTab))
        {
            OnPropertyChanged(nameof(StatusText));
        }
    }

    private void SetPending(TerminalTab tab, bool pending)
    {
        tab.IsPending = pending;
        if (ReferenceEquals(tab, SelectedTab))
        {
            IsPending = pending;
        }
    }

    /// <summary>언어가 바뀌었을 때 코드에서 만든 문구를 다시 읽게 한다.</summary>
    public void RefreshLanguage() => OnPropertyChanged(string.Empty);

    /// <summary>선택된 탭에서 탐색기가 보낸 이동이 터미널 실행 중이라 보류 중인지(상태 문구를 강조 표시).</summary>
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

    public string CollapseToolTip => IsCollapsed ? Loc.T("터미널 펼치기", "Expand terminal") : Loc.T("터미널 접기", "Collapse terminal");

    /// <summary>사용자에게 알릴 결과 메시지(저장 성공/실패). MainViewModel이 하단 안내줄에 표시한다.</summary>
    public event Action<string, bool>? Notice;

    /// <summary>선택된 탭의 터미널 전체 내용(스크롤백 포함)을 사용자가 고른 파일에 저장한다.</summary>
    [RelayCommand]
    private void SaveOutput()
    {
        string? path = _dialogs.PickSaveFile(Loc.T("터미널 내용 저장", "Save terminal contents"), $"terminal-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        if (path is null)
        {
            return;
        }
        try
        {
            System.IO.File.WriteAllText(path, Session.Screen.GetAllText() + "\r\n", new System.Text.UTF8Encoding(true));
            _log.Info($"터미널 내용 저장: {path}");
            Notice?.Invoke(Loc.T($"터미널 내용을 저장했습니다: {path}", $"Terminal contents saved: {path}"), true);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            _log.Warn($"터미널 내용 저장 실패: {path} - {ex.Message}");
            Notice?.Invoke(Loc.T($"저장하지 못했습니다: {ex.Message}", $"Could not save: {ex.Message}"), false);
        }
    }

    /// <summary>선택된 탭의 터미널 화면과 지난 출력을 지운다.</summary>
    [RelayCommand]
    private void ClearScreen() => Session.ClearScreen();

    [RelayCommand]
    private void ToggleCollapsed() => IsCollapsed = !IsCollapsed;

    /// <summary>경로 변경 보고(UI 스레드). 두 번째 인자: 앱이 주입한 cd로 인한 변경이면 true. 선택된 탭의 보고만 전달한다.</summary>
    public event Action<string, bool>? CwdChanged;

    public TerminalViewModel(ILog log, Services.IDialogService dialogs)
    {
        _dialogs = dialogs;
        _ui = Application.Current.Dispatcher;
        _log = log;
        TerminalTab first = CreateTab(DefaultShell);
        first.IsSelected = true;
        Tabs.Add(first);
        _selectedTab = first;
        _session = first.Session;
        Tabs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMultipleTabs));
    }

    private TerminalTab CreateTab(TerminalShell shell)
    {
        var tab = new TerminalTab(_nextTabNumber++, session: null!, shell);
        tab.Session = CreateSession(tab);
        return tab;
    }

    /// <summary>탭의 세션을 만들고 이벤트를 연결한다. 교체되어 버려진 세션의 이벤트는 무시한다.</summary>
    private TerminalSession CreateSession(TerminalTab tab)
    {
        var s = new TerminalSession(DefaultCols, DefaultRows, _log);
        s.CwdChanged += (p, fromRequest) => _ui.BeginInvoke(() =>
        {
            if (!ReferenceEquals(s, tab.Session))
            {
                return;
            }
            tab.Cwd = p;
            if (ReferenceEquals(tab, SelectedTab))
            {
                CwdChanged?.Invoke(p, fromRequest);
            }
        });
        s.IdleChanged += idle => _ui.BeginInvoke(() =>
        {
            if (!ReferenceEquals(s, tab.Session))
            {
                return;
            }
            SetStatus(tab, idle ? TerminalTabStatus.Idle : TerminalTabStatus.Running);
            if (idle)
            {
                SetPending(tab, false);
            }
        });
        s.Exited += code => _ui.BeginInvoke(() =>
        {
            if (ReferenceEquals(s, tab.Session))
            {
                SetStatus(tab, TerminalTabStatus.Exited, code.ToString());
            }
        });
        return s;
    }

    /// <summary>선택이 바뀌면 화면에 그 탭의 세션을 보여주고, 그 탭의 폴더를 사용자가 이동한 것처럼 알려 탐색기가 따라가게 한다(모드가 허용할 때).</summary>
    partial void OnSelectedTabChanged(TerminalTab? oldValue, TerminalTab newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }
        newValue.IsSelected = true;
        Session = newValue.Session;
        IsPending = newValue.IsPending;
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(Header));
        if (newValue.Session.Cwd is { } cwd)
        {
            CwdChanged?.Invoke(cwd, false);
        }
    }

    /// <summary>탭을 선택한다(탭 줄 클릭).</summary>
    [RelayCommand]
    private void SelectTab(TerminalTab? tab)
    {
        if (tab is not null && Tabs.Contains(tab))
        {
            SelectedTab = tab;
        }
    }

    /// <summary>다음/이전 탭으로(끝에서 처음으로 돌아간다).</summary>
    [RelayCommand]
    private void NextTab() => StepTab(+1);

    [RelayCommand]
    private void PreviousTab() => StepTab(-1);

    private void StepTab(int delta)
    {
        if (Tabs.Count > 1)
        {
            int i = Tabs.IndexOf(SelectedTab);
            SelectedTab = Tabs[(i + delta + Tabs.Count) % Tabs.Count];
        }
    }

    /// <summary>새 탭을 열고 선택한다. 시작 폴더는 탐색기의 현재 폴더(없으면 선택된 탭의 폴더), 셸은 설정의 셸.</summary>
    [RelayCommand]
    private void NewTab()
    {
        if (Tabs.Count >= MaxTabs)
        {
            Notice?.Invoke(Loc.T($"탭은 최대 {MaxTabs}개까지 열 수 있습니다.", $"You can open up to {MaxTabs} tabs."), false);
            return;
        }
        string? dir = StartDirectoryProvider?.Invoke() ?? SelectedTab.Session.Cwd;
        TerminalTab tab = CreateTab(DefaultShell);
        Tabs.Add(tab);
        SelectedTab = tab; // 화면에 먼저 붙여 크기가 맞춰진 뒤 셸을 시작한다.
        tab.Session.Start(dir, tab.Shell);
        _log.Info($"터미널 새 탭 {tab.Number}: cwd={dir}, shell={tab.Shell}");
    }

    /// <summary>탭을 닫는다(없으면 선택된 탭). 마지막 탭은 닫지 않고, 명령이 실행 중이면 확인한다.</summary>
    [RelayCommand]
    private void CloseTab(TerminalTab? tab)
    {
        tab ??= SelectedTab;
        if (Tabs.Count <= 1 || !Tabs.Contains(tab))
        {
            return;
        }
        if (tab.Status == TerminalTabStatus.Running
            && !_dialogs.Confirm(Loc.T("터미널 탭 닫기", "Close terminal tab"), Loc.T("이 탭에서 실행 중인 작업이 있습니다. 탭을 닫으면 종료됩니다. 계속할까요?", "A task is still running in this tab. Closing the tab will end it. Continue?")))
        {
            return;
        }
        int index = Tabs.IndexOf(tab);
        bool wasSelected = ReferenceEquals(tab, SelectedTab);
        Tabs.Remove(tab);
        if (wasSelected)
        {
            SelectedTab = Tabs[Math.Min(index, Tabs.Count - 1)];
        }
        tab.Session.Dispose();
        _log.Info($"터미널 탭 {tab.Number} 닫음");
    }

    /// <summary>선택된 탭을 닫고 다른 셸을 같은 폴더에서 새로 시작한다. 이후 새 탭도 이 셸을 쓴다.</summary>
    public void RestartShell(string? workingDirectory, TerminalShell shell)
    {
        TerminalTab tab = SelectedTab;
        TerminalSession old = tab.Session;
        tab.Session = CreateSession(tab);
        tab.Shell = shell;
        DefaultShell = shell;
        Session = tab.Session;
        SetStatus(tab, TerminalTabStatus.Starting);
        SetPending(tab, false);
        OnPropertyChanged(nameof(Header));
        tab.Session.Start(workingDirectory, shell);
        old.Dispose();
    }

    /// <summary>패널 머리글에 표시할 셸 이름(선택된 탭의 셸).</summary>
    public string Header => SelectedTab.Shell == TerminalShell.CommandPrompt
        ? Loc.T("터미널 - 명령 프롬프트", "Terminal - Command Prompt")
        : Loc.T("터미널 - PowerShell", "Terminal - PowerShell");

    /// <summary>첫 탭의 셸을 시작한다(앱 시작 때 한 번).</summary>
    public void Start(string? workingDirectory, TerminalShell shell = TerminalShell.PowerShell)
    {
        DefaultShell = shell;
        SelectedTab.Shell = shell;
        OnPropertyChanged(nameof(Header));
        SelectedTab.Session.Start(workingDirectory, shell);
    }

    public string? CurrentPath => Session.Cwd;

    /// <summary>탐색기에서 온 이동 요청(선택된 탭으로). Busy이면 보류하고 상태 문구에 표시한다.</summary>
    public bool RequestChange(string path)
    {
        TerminalTab tab = SelectedTab;
        bool delivered = tab.Session.RequestChangeDirectory(path);
        if (!delivered)
        {
            SetStatus(tab, TerminalTabStatus.WaitingToMove, path);
            SetPending(tab, true);
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
        foreach (TerminalTab tab in Tabs)
        {
            tab.Session.Dispose();
        }
    }
}
