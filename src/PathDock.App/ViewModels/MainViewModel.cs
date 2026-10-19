using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PathDock.Core.Explorer;
using PathDock.Core.Localization;
using PathDock.Core.Logging;
using PathDock.Core.Paths;
using PathDock.Core.Settings;
using PathDock.Core.Sync;
using PathDock.Core.Terminal;

namespace PathDock.App.ViewModels;

/// <summary>동기화 모드 콤보박스 항목. 언어가 바뀌면 <see cref="RefreshLabel"/>로 이름을 다시 읽는다.</summary>
public sealed class SyncModeOption : ObservableObject
{
    private readonly string _korean;
    private readonly string _english;

    public SyncModeOption(SyncMode mode, string korean, string english)
    {
        Mode = mode;
        _korean = korean;
        _english = english;
    }

    public SyncMode Mode { get; }

    public string Label => Loc.T(_korean, _english);

    public void RefreshLabel() => OnPropertyChanged(nameof(Label));

    /// <summary>UI Automation/접근성에서 항목 이름으로 쓰이도록 레이블만 반환한다.</summary>
    public override string ToString() => Label;
}

/// <summary>
/// 화면 전체의 루트 ViewModel. Owner: App, Lifetime: 앱 전체.
/// 하위 ViewModel(탐색기, 터미널, 즐겨찾기, 드라이브, 사용량, 알림 막대, 설정)을 조립하고, <see cref="PathSyncService"/>로
/// 탐색기와 터미널을 연결한다(둘은 서로를 모른다). 이 클래스에는 조립·시작·저장·동기화 모드·테마 전환·제목 표시줄 명령만 둔다.
/// 모든 멤버는 UI 스레드에서만 사용한다.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan StartPathCheckTimeout = TimeSpan.FromSeconds(2);

    private readonly SettingsService _settingsService;
    private readonly FolderSizeCache _sizeCache;
    private readonly ILog _log;
    private readonly PathSyncService _sync;
    private readonly Services.IDialogService _dialogs;

    /// <summary>설정 파일 내용(창 위치/크기, 즐겨찾기, 모드 등). 설정 창의 화면 상태는 <see cref="Preferences"/>.</summary>
    public AppSettings Settings { get; }

    public ExplorerViewModel Explorer { get; }
    public TerminalViewModel Terminal { get; }
    public FavoritesViewModel Favorites { get; }
    public DrivesViewModel Drives { get; }
    public UsageViewModel Usage { get; }
    public InfoBarViewModel Info { get; }

    /// <summary>설정 창이 쓰는 ViewModel(언어, 글자 크기, 시작 폴더, 터미널 셸·색).</summary>
    public SettingsViewModel Preferences { get; }

    // ----- 경로 동기화 모드 -----

    public IReadOnlyList<SyncModeOption> SyncModes { get; } = new[]
    {
        new SyncModeOption(SyncMode.Bidirectional, "양방향", "Both ways"),
        new SyncModeOption(SyncMode.ExplorerToTerminal, "탐색기 → 터미널", "Explorer → Terminal"),
        new SyncModeOption(SyncMode.TerminalToExplorer, "터미널 → 탐색기", "Terminal → Explorer"),
        new SyncModeOption(SyncMode.Off, "동기화 끄기", "Sync off"),
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncSummary))]
    private SyncModeOption _selectedSyncMode;

    partial void OnSelectedSyncModeChanged(SyncModeOption value)
    {
        _sync.Mode = value.Mode;
        Settings.SyncMode = value.Mode;
        _log.Info($"동기화 모드 변경: {value.Mode}");
    }

    /// <summary>상태 표시줄 왼쪽에 보이는 동기화 요약. 예: "경로 동기화: 양방향".</summary>
    public string SyncSummary => $"{Loc.T("경로 동기화", "Path sync")}: {SelectedSyncMode.Label}";

    // ----- 테마 -----

    /// <summary>현재 다크 테마인지(테마 버튼 아이콘/툴팁 선택용).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThemeToolTip))]
    private bool _isDarkTheme;

    public string ThemeToolTip => IsDarkTheme ? Loc.T("라이트 테마로 전환", "Switch to light theme") : Loc.T("다크 테마로 전환", "Switch to dark theme");

    /// <summary>테마 전환(다크 ↔ 라이트). 선택은 설정에 저장되어 다음 실행에도 유지된다.</summary>
    [RelayCommand]
    private void ToggleTheme()
    {
        Settings.Theme = IsDarkTheme ? Services.ThemeService.Light : Services.ThemeService.Dark;
        IsDarkTheme = !IsDarkTheme;
        Services.ThemeService.Apply(Settings.Theme);
        _log.Info($"테마 변경: {Settings.Theme}");
    }

    // ----- 제목 표시줄 명령 -----

    /// <summary>제목 표시줄 계산기 버튼: Windows 계산기를 연다.</summary>
    [RelayCommand]
    private void OpenCalculator()
    {
        if (Services.CalculatorLauncher.TryActivateExisting())
        {
            return;
        }
        try
        {
            // 전체 경로로 실행한다(이름만 주면 앱 폴더 등에서 같은 이름의 파일을 먼저 찾을 수 있다 - 이 앱은 관리자 권한).
            string calc = Path.Combine(Environment.SystemDirectory, "calc.exe");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(calc) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Info.Show(Loc.T($"계산기를 열 수 없습니다: {ex.Message}", $"Could not open Calculator: {ex.Message}"));
        }
    }

    /// <summary>제목 표시줄 휴지통 버튼: 확인 후 모든 드라이브의 휴지통을 비운다.</summary>
    [RelayCommand]
    private async Task EmptyRecycleBinAsync()
    {
        (long Items, long Bytes)? content = await Task.Run(Services.RecycleBinService.Query);
        if (content is { Items: 0 })
        {
            Info.ShowSuccess(Loc.T("휴지통이 이미 비어 있습니다.", "The Recycle Bin is already empty."));
            return;
        }
        string what = content is { } c
            ? Loc.T($"{c.Items:N0}개 항목({SizeFormatter.Format(c.Bytes)})을 ", $"{c.Items:N0} items ({SizeFormatter.Format(c.Bytes)})")
            : Loc.T("모든 항목을 ", "all items");
        if (!_dialogs.Confirm(Loc.T("휴지통 비우기", "Empty Recycle Bin"),
                Loc.T($"휴지통의 {what}영구 삭제합니다. 되돌릴 수 없습니다. 계속할까요?", $"This permanently deletes {what} from the Recycle Bin. This cannot be undone. Continue?")))
        {
            return;
        }
        try
        {
            await Task.Run(Services.RecycleBinService.Empty);
            Info.ShowSuccess(Loc.T("휴지통을 비웠습니다.", "The Recycle Bin has been emptied."));
            _log.Info("휴지통 비움");
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or System.ComponentModel.Win32Exception)
        {
            _log.Warn($"휴지통 비우기 실패: {ex.Message}");
            Info.Show(Loc.T($"휴지통을 비우지 못했습니다: {ex.Message}", $"Could not empty the Recycle Bin: {ex.Message}"));
        }
    }

    /// <summary>제목 표시줄 톱니 버튼: 설정 창.</summary>
    [RelayCommand]
    private void ShowSettings() => _dialogs.ShowSettings(Preferences);

    /// <summary>제목 표시줄 로고 클릭: 프로그램 정보 창.</summary>
    [RelayCommand]
    private void ShowAbout() => _dialogs.ShowAbout();

    // ----- 조립 -----

    /// <param name="progress">시작 중 로딩 팝업에 보여줄 "지금 하는 일" 문구를 받는 콜백(선택, UI 스레드에서 호출).</param>
    /// <param name="dialogs">대화상자 구현(없으면 WPF 구현). 시험에서 바꿔 끼울 수 있다.</param>
    public MainViewModel(SettingsService settingsService, FolderSizeCache sizeCache, ILog log, Action<string>? progress = null, Services.IDialogService? dialogs = null)
    {
        _settingsService = settingsService;
        _sizeCache = sizeCache;
        _log = log;
        _dialogs = dialogs ?? new Services.WpfDialogService();
        Settings = settingsService.Load();

        progress?.Invoke(Loc.T("폴더 크기 캐시 불러오는 중...", "Loading folder size cache..."));
        _sizeCache.Load();
        progress?.Invoke(Loc.T("탐색기와 터미널 준비 중...", "Preparing explorer and terminal..."));
        Info = new InfoBarViewModel();
        Explorer = new ExplorerViewModel(log, _sizeCache, _dialogs);
        Explorer.ShowHidden = Settings.ShowHiddenFiles;
        Explorer.ShowHiddenChanged += v => { Settings.ShowHiddenFiles = v; SaveSettings(); };
        Terminal = new TerminalViewModel(log, _dialogs) { StartDirectoryProvider = () => Explorer.CurrentPath };
        Favorites = new FavoritesViewModel(Settings.Favorites, () => Explorer.CurrentPath, path => Explorer.NavigateTo(path, ChangeOrigin.User), _dialogs, log, SaveSettings);
        Drives = new DrivesViewModel(log, path => Explorer.NavigateTo(path, ChangeOrigin.User), () => Explorer.CurrentPath);
        Preferences = new SettingsViewModel(Settings, Terminal, Explorer, Favorites, _dialogs);
        _isDarkTheme = Services.ThemeService.Normalize(Settings.Theme) == Services.ThemeService.Dark;
        Terminal.IsCollapsed = Settings.TerminalCollapsed;
        _sync = new PathSyncService(Explorer, Terminal, log) { Mode = Settings.SyncMode };
        _selectedSyncMode = SyncModes.First(o => o.Mode == Settings.SyncMode);
        Loc.LanguageChanged += OnLanguageChanged;
        WireEvents();
        Usage = new UsageViewModel(log); // 하단 시스템 사용량: 측정은 백그라운드 타이머에서, 표시 갱신은 UI 스레드에서.
    }

    /// <summary>탐색기·터미널·동기화 서비스의 이벤트를 서로 연결한다.</summary>
    private void WireEvents()
    {
        Explorer.Navigated += (path, origin) =>
        {
            Info.Clear();
            _sync.OnExplorerNavigated(path, origin);
            Drives.MarkCurrent(path);
            _ = Drives.RefreshAsync();
        };
        Explorer.ErrorOccurred += Info.Show;
        Explorer.Notified += Info.ShowSuccess;
        Explorer.SendPathsToTerminalRequested += SendPathsToTerminal;
        Terminal.Notice += Notify;
        Terminal.CwdChanged += (path, fromRequest) =>
            _sync.OnTerminalCwdChanged(path, fromRequest ? ChangeOrigin.Sync : ChangeOrigin.User);
        _sync.Warning += w => Info.Show(w.Message);
    }

    private void Notify(string message, bool success)
    {
        if (success)
        {
            Info.ShowSuccess(message);
        }
        else
        {
            Info.Show(message);
        }
    }


    /// <summary>터미널로 포커스를 옮겨 달라는 요청(창이 처리한다).</summary>
    public event Action? TerminalFocusRequested;

    /// <summary>경로들을 활성 터미널 입력줄에 넣는다(Enter 없음). 터미널이 끝났거나 쓸 수 없는 경로뿐이면 알림 막대로 알린다.</summary>
    public void SendPathsToTerminal(IReadOnlyList<string> paths)
    {
        TerminalTab tab = Terminal.SelectedTab;
        if (tab.Status == TerminalTabStatus.Exited)
        {
            Info.Show(Loc.T("터미널이 종료되어 경로를 입력할 수 없습니다", "The terminal has exited, so the path cannot be typed into it"));
            return;
        }
        string text = TerminalPathText.Join(paths, Settings.TerminalShell);
        if (text.Length == 0)
        {
            Info.Show(Loc.T("경로에 제어 문자가 있어 터미널에 입력하지 않았습니다", "The path contains control characters, so it was not typed into the terminal"));
            return;
        }
        tab.Session.SendInput(text);
        TerminalFocusRequested?.Invoke();
    }
    /// <summary>시작 경로(마지막 경로 → 사용자 폴더)로 탐색기를 열고 터미널을 같은 경로에서 시작한다.</summary>
    public void Initialize(Action<string>? progress = null)
    {
        string start = ResolveStartPath();
        progress?.Invoke(Loc.T("폴더 목록 읽는 중...", "Reading folder contents..."));
        Explorer.NavigateTo(start, ChangeOrigin.Sync);
        progress?.Invoke(Loc.T("터미널 시작 중...", "Starting terminal..."));
        Terminal.Start(Explorer.CurrentPath ?? start, Settings.TerminalShell);
        Preferences.TerminalStarted = true;
        _ = Drives.RefreshAsync();
        _log.Info($"앱 시작: start={start}, mode={Settings.SyncMode}");
    }

    private string ResolveStartPath()
    {
        string? last = PathNormalizer.Normalize(Settings.LastPath);
        // 마지막 경로가 꺼진 네트워크 공유면 확인이 OS 시간 제한까지(수십 초) UI 스레드를 막으므로 2초만 기다린다(그 안에 답이 없으면 사용자 폴더로 시작).
        if (Settings.StartAtLastPath && last is not null && SafeIo.DirectoryExistsWithTimeout(last, StartPathCheckTimeout))
        {
            return last;
        }
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    /// <summary>언어가 바뀌면 코드에서 만든 문구(속성, 목록 항목)를 새 언어로 다시 읽게 한다. XAML 문구는 LocSource가 따로 갱신한다.</summary>
    private void OnLanguageChanged()
    {
        foreach (SyncModeOption option in SyncModes)
        {
            option.RefreshLabel();
        }
        OnPropertyChanged(string.Empty); // 이 ViewModel의 모든 바인딩(SyncSummary, 테마 툴팁 등)을 다시 읽는다.
        Preferences.RefreshLanguage();
        Explorer.RefreshLanguage();
        Terminal.RefreshLanguage();
        Drives.RefreshLanguage();
        Favorites.RefreshLanguage();
        Usage.RefreshLanguage();
        Info.RefreshLanguage();
    }

    /// <summary>
    /// 처리되지 않은 예외를 사용자에게 알린다(앱은 계속 실행). 예외는 호출 측(App)이 이미 로그에 남겼다.
    /// 상태가 어긋났을 수 있으므로 알림에 로그 확인을 안내한다.
    /// </summary>
    public void ReportUnexpectedError(Exception ex)
    {
        Info.Show(Loc.T(
            $"예기치 않은 오류가 발생했지만 계속 실행합니다. 문제가 반복되면 로그를 확인하세요: {ex.Message}",
            $"An unexpected error occurred, but the app keeps running. If it keeps happening, check the log: {ex.Message}"));
    }

    /// <summary>현재 상태(즐겨찾기, 마지막 경로, 터미널 접힘)를 설정에 반영해 저장한다. 창 종료 때와 즐겨찾기가 바뀔 때 호출된다.</summary>
    public void SaveSettings()
    {
        Settings.Favorites = Favorites.ToEntries();
        Settings.LastPath = Explorer.CurrentPath ?? string.Empty;
        Settings.TerminalCollapsed = Terminal.IsCollapsed;
        _settingsService.Save(Settings);
        _sizeCache.Save();
        _log.Info("설정 저장");
    }

    public void Dispose()
    {
        Loc.LanguageChanged -= OnLanguageChanged;
        Usage.Dispose();
        Terminal.Dispose();
    }
}
