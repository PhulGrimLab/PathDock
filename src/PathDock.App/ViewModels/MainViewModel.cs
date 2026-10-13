using System.IO;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;
using PathDock.Core.Explorer;
using PathDock.Core.Logging;
using PathDock.Core.Monitoring;
using PathDock.Core.Paths;
using PathDock.Core.Settings;
using PathDock.Core.Sync;
using PathDock.Core.Terminal;

namespace PathDock.App.ViewModels;

/// <summary>동기화 모드 콤보박스 항목.</summary>
public sealed record SyncModeOption(SyncMode Mode, string Label)
{
    /// <summary>UI Automation/접근성에서 항목 이름으로 쓰이도록 레이블만 반환한다.</summary>
    public override string ToString() => Label;
}

/// <summary>
/// 화면 전체의 루트 ViewModel. Owner: App, Lifetime: 앱 전체.
/// 탐색기/터미널/즐겨찾기/설정을 조립하고 <see cref="PathSyncService"/>로 둘을 연결한다(둘은 서로를 모른다).
/// 모든 멤버는 UI 스레드에서만 사용한다.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly FolderSizeCache _sizeCache;
    private readonly SystemMonitor _monitor;
    private readonly ILog _log;
    private readonly PathSyncService _sync;
    private readonly Services.IDialogService _dialogs = new Services.WpfDialogService();

    public AppSettings Settings { get; }
    public ExplorerViewModel Explorer { get; }
    public TerminalViewModel Terminal { get; }
    public ObservableCollection<FavoriteSlotViewModel> Favorites { get; } = new();
    public ObservableCollection<DriveItemViewModel> Drives { get; } = new();
    private bool _drivesRefreshing;

    public IReadOnlyList<SyncModeOption> SyncModes { get; } = new[]
    {
        new SyncModeOption(SyncMode.Bidirectional, "양방향"),
        new SyncModeOption(SyncMode.ExplorerToTerminal, "탐색기 → 터미널"),
        new SyncModeOption(SyncMode.TerminalToExplorer, "터미널 → 탐색기"),
        new SyncModeOption(SyncMode.Off, "동기화 끄기"),
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncSummary))]
    private SyncModeOption _selectedSyncMode;

    /// <summary>InfoBar 표시 내용(비어 있으면 숨김).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInfo))]
    private string _infoMessage = string.Empty;

    /// <summary>InfoBar 종류(성공은 초록, 그 밖의 알림/오류는 경고색).</summary>
    [ObservableProperty]
    private Wpf.Ui.Controls.InfoBarSeverity _infoSeverity = Wpf.Ui.Controls.InfoBarSeverity.Warning;

    [ObservableProperty]
    private string _infoTitle = "알림";

    private bool _nextInfoIsSuccess;
    private System.Windows.Threading.DispatcherTimer? _infoTimer;

    public bool HasInfo => !string.IsNullOrEmpty(InfoMessage);

    /// <summary>작업이 잘 끝났음을 알린다. 초록 InfoBar로 보였다가 몇 초 뒤 저절로 사라진다.</summary>
    private void NotifySuccess(string message)
    {
        InfoMessage = string.Empty; // 같은 문구도 다시 보이게 한다.
        _nextInfoIsSuccess = true;
        InfoMessage = message;
    }

    partial void OnInfoMessageChanged(string value)
    {
        _infoTimer?.Stop();
        if (string.IsNullOrEmpty(value))
        {
            _nextInfoIsSuccess = false;
            return;
        }
        bool success = _nextInfoIsSuccess;
        _nextInfoIsSuccess = false;
        InfoSeverity = success ? Wpf.Ui.Controls.InfoBarSeverity.Success : Wpf.Ui.Controls.InfoBarSeverity.Warning;
        InfoTitle = success ? "완료" : "알림";
        // 성공은 5초, 경고/오류는 읽을 시간을 더 주고 10초 뒤 저절로 닫는다(직접 닫기도 가능).
        if (_infoTimer is null)
        {
            _infoTimer = new System.Windows.Threading.DispatcherTimer();
            _infoTimer.Tick += (_, _) =>
            {
                _infoTimer.Stop();
                InfoMessage = string.Empty;
            };
        }
        _infoTimer.Interval = TimeSpan.FromSeconds(success ? 5 : 10);
        _infoTimer.Start();
    }

    private const int MonitorIntervalMs = 2000;

    /// <summary>하단 오른쪽 시스템 사용량 문구. 예: "CPU 12% | GPU 3% | 메모리 8.1/15.7 GB (52%)".</summary>
    [ObservableProperty]
    private string _usageText = "CPU - | GPU - | 메모리 -";

    // 상태 표시줄 미터(UsageMeter)용 값. 각 미터의 이름은 "CPU 12%"처럼 라벨+값 문구로 접근성에 노출된다.
    [ObservableProperty] private double _cpuPercent;
    [ObservableProperty] private string _cpuText = "-";
    [ObservableProperty] private double _gpuPercent;
    [ObservableProperty] private string _gpuText = "-";
    [ObservableProperty] private bool _gpuAvailable = true;
    [ObservableProperty] private double _memoryPercent;
    [ObservableProperty] private string _memoryText = "-";

    /// <summary>측정값을 미터/문구 속성에 반영한다(UI 스레드).</summary>
    private void ApplyUsage(SystemUsage u)
    {
        CpuPercent = u.CpuPercent;
        CpuText = $"{u.CpuPercent:0}%";
        GpuAvailable = u.GpuPercent is not null;
        GpuPercent = u.GpuPercent ?? 0;
        GpuText = u.GpuPercent is { } g ? $"{g:0}%" : "N/A";
        MemoryPercent = u.MemoryPercent;
        MemoryText = $"{SizeFormatter.Format(u.MemoryUsedBytes)} / {SizeFormatter.Format(u.MemoryTotalBytes)} ({u.MemoryPercent:0}%)";
        UsageText = FormatUsage(u);
    }

    // ----- 테마 / 정보 창 -----

    /// <summary>현재 다크 테마인지(테마 버튼 아이콘/툴팁 선택용).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThemeToolTip))]
    private bool _isDarkTheme;

    public string ThemeToolTip => IsDarkTheme ? "라이트 테마로 전환" : "다크 테마로 전환";

    /// <summary>테마 전환(다크 ↔ 라이트). 선택은 설정에 저장되어 다음 실행에도 유지된다.</summary>
    [RelayCommand]
    private void ToggleTheme()
    {
        Settings.Theme = IsDarkTheme ? Services.ThemeService.Light : Services.ThemeService.Dark;
        IsDarkTheme = !IsDarkTheme;
        Services.ThemeService.Apply(Settings.Theme);
        _log.Info($"테마 변경: {Settings.Theme}");
    }

    // ----- 설정 창 -----

    /// <summary>화면 글자 크기. 바꾸면 즉시 반영되고 설정에 저장된다.</summary>
    [ObservableProperty]
    private double _uiFontSize;

    /// <summary>터미널 글자 크기. 바꾸면 즉시 반영되고 설정에 저장된다.</summary>
    [ObservableProperty]
    private double _terminalFontSize;

    /// <summary>시작 폴더: true면 마지막으로 열었던 폴더, false면 사용자 폴더.</summary>
    [ObservableProperty]
    private bool _startAtLastPath;

    partial void OnStartAtLastPathChanged(bool value) => Settings.StartAtLastPath = value;

    /// <summary>터미널 셸 선택(0=PowerShell, 1=명령 프롬프트). 다음 실행부터 적용된다.</summary>
    [ObservableProperty]
    private int _terminalShellIndex;

    private bool _terminalStarted;
    private bool _revertingShell;

    partial void OnTerminalShellIndexChanged(int oldValue, int newValue)
    {
        if (_revertingShell)
        {
            return;
        }
        TerminalShell shell = newValue == 1 ? TerminalShell.CommandPrompt : TerminalShell.PowerShell;
        if (_terminalStarted)
        {
            if (!Terminal.Session.IsIdle
                && !_dialogs.Confirm("터미널 셸 변경", "실행 중인 작업이 있습니다. 터미널을 새 셸로 다시 시작하면 종료됩니다. 계속할까요?"))
            {
                // 콤보박스 갱신이 끝난 뒤 이전 값으로 되돌린다.
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    _revertingShell = true;
                    TerminalShellIndex = oldValue;
                    _revertingShell = false;
                });
                return;
            }
            Terminal.RestartShell(Terminal.CurrentPath ?? Explorer.CurrentPath, shell);
        }
        Settings.TerminalShell = shell;
    }

    /// <summary>설정 창 콤보박스에 보여줄 터미널 테마 이름(TerminalThemes.All과 같은 순서).</summary>
    public IReadOnlyList<string> TerminalThemeNames { get; } = TerminalThemes.All.Select(t => t.DisplayName).ToList();

    [ObservableProperty]
    private int _terminalThemeIndex;

    /// <summary>터미널 글자색(#RRGGBB). 비우면 테마 기본값.</summary>
    [ObservableProperty]
    private string _terminalForeground = string.Empty;

    /// <summary>터미널 배경색(#RRGGBB). 비우면 테마 기본값.</summary>
    [ObservableProperty]
    private string _terminalBackground = string.Empty;

    partial void OnTerminalThemeIndexChanged(int value) => ApplyTerminalColors();

    partial void OnTerminalForegroundChanged(string value)
    {
        ApplyTerminalColors();
        OnPropertyChanged(nameof(ColorErrorText));
        OnPropertyChanged(nameof(HasColorError));
    }

    partial void OnTerminalBackgroundChanged(string value)
    {
        ApplyTerminalColors();
        OnPropertyChanged(nameof(ColorErrorText));
        OnPropertyChanged(nameof(HasColorError));
    }

    /// <summary>글자색/배경색 입력이 #RRGGBB 형식이 아닐 때의 안내(올바르면 빈 문자열).</summary>
    public string ColorErrorText
    {
        get
        {
            var bad = new List<string>();
            if (!string.IsNullOrWhiteSpace(TerminalForeground) && TerminalThemes.TryParseColor(TerminalForeground) is null)
            {
                bad.Add("글자색");
            }
            if (!string.IsNullOrWhiteSpace(TerminalBackground) && TerminalThemes.TryParseColor(TerminalBackground) is null)
            {
                bad.Add("배경색");
            }
            return bad.Count == 0 ? string.Empty : $"{string.Join("·", bad)} 형식이 올바르지 않습니다. #RRGGBB 형식(예: #FFD700)으로 입력하세요. 그 전까지는 테마 색을 씁니다.";
        }
    }

    public bool HasColorError => ColorErrorText.Length > 0;

    private void ApplyTerminalColors()
    {
        int index = Math.Clamp(TerminalThemeIndex, 0, TerminalThemes.All.Count - 1);
        Settings.TerminalTheme = TerminalThemes.All[index].Key;
        Settings.TerminalForeground = TerminalForeground;
        Settings.TerminalBackground = TerminalBackground;
        Terminal.ColorScheme = TerminalThemes.Resolve(Settings.TerminalTheme, TerminalForeground, TerminalBackground);
    }

    /// <summary>터미널 색을 기본 테마로 되돌린다.</summary>
    [RelayCommand]
    private void ResetTerminalColors()
    {
        TerminalThemeIndex = 0;
        TerminalForeground = string.Empty;
        TerminalBackground = string.Empty;
    }

    partial void OnUiFontSizeChanged(double value)
    {
        Settings.UiFontSize = Services.FontService.NormalizeUi(value);
        Services.FontService.ApplyUi(Settings.UiFontSize);
    }

    partial void OnTerminalFontSizeChanged(double value)
    {
        Settings.TerminalFontSize = Services.FontService.NormalizeTerminal(value);
        Terminal.FontSize = Settings.TerminalFontSize;
    }

    /// <summary>제목 표시줄 계산기 버튼: Windows 계산기를 연다.</summary>
    [RelayCommand]
    private void OpenCalculator()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("calc.exe") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            InfoMessage = $"계산기를 열 수 없습니다: {ex.Message}";
        }
    }

    /// <summary>제목 표시줄 휴지통 버튼: 확인 후 모든 드라이브의 휴지통을 비운다.</summary>
    [RelayCommand]
    private async Task EmptyRecycleBinAsync()
    {
        (long Items, long Bytes)? content = await Task.Run(Services.RecycleBinService.Query);
        if (content is { Items: 0 })
        {
            NotifySuccess("휴지통이 이미 비어 있습니다.");
            return;
        }
        string what = content is { } c ? $"{c.Items:N0}개 항목({SizeFormatter.Format(c.Bytes)})을 " : "모든 항목을 ";
        if (!_dialogs.Confirm("휴지통 비우기", $"휴지통의 {what}영구 삭제합니다. 되돌릴 수 없습니다. 계속할까요?"))
        {
            return;
        }
        try
        {
            await Task.Run(Services.RecycleBinService.Empty);
            NotifySuccess("휴지통을 비웠습니다.");
            _log.Info("휴지통 비움");
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or System.ComponentModel.Win32Exception)
        {
            _log.Warn($"휴지통 비우기 실패: {ex.Message}");
            InfoMessage = $"휴지통을 비우지 못했습니다: {ex.Message}";
        }
    }

    /// <summary>제목 표시줄 톱니 버튼: 설정 창.</summary>
    [RelayCommand]
    private void ShowSettings() => _dialogs.ShowSettings(this);

    /// <summary>글자 크기를 기본값으로 되돌린다.</summary>
    [RelayCommand]
    private void ResetFonts()
    {
        UiFontSize = AppSettings.DefaultUiFontSize;
        TerminalFontSize = AppSettings.DefaultTerminalFontSize;
    }

    /// <summary>즐겨찾기 10칸을 모두 비운다(확인 후).</summary>
    [RelayCommand]
    private void ResetFavorites()
    {
        if (!_dialogs.Confirm("즐겨찾기 초기화", "등록된 즐겨찾기를 모두 비웁니다. 계속할까요?"))
        {
            return;
        }
        foreach (FavoriteSlotViewModel slot in Favorites)
        {
            slot.Path = string.Empty;
            slot.Name = string.Empty;
        }
        _log.Info("즐겨찾기 전체 초기화");
    }

    /// <summary>제목 표시줄 로고 클릭: 프로그램 정보 창.</summary>
    [RelayCommand]
    private void ShowAbout() => _dialogs.ShowAbout();

    /// <summary>상태 표시줄 왼쪽에 보이는 동기화 요약. 예: "경로 동기화: 양방향".</summary>
    public string SyncSummary => $"경로 동기화: {SelectedSyncMode.Label}";

    /// <summary>측정값을 표시 문구로 바꾼다. GPU 카운터가 없는 환경이면 "N/A".</summary>
    public static string FormatUsage(SystemUsage u)
    {
        string gpu = u.GpuPercent is { } g ? $"{g:0}%" : "N/A";
        return $"CPU {u.CpuPercent:0}% | GPU {gpu} | 메모리 {SizeFormatter.Format(u.MemoryUsedBytes)} / {SizeFormatter.Format(u.MemoryTotalBytes)} ({u.MemoryPercent:0}%)";
    }

    public MainViewModel(SettingsService settingsService, FolderSizeCache sizeCache, ILog log)
    {
        _settingsService = settingsService;
        _sizeCache = sizeCache;
        _log = log;
        Settings = settingsService.Load();

        _sizeCache.Load();
        Explorer = new ExplorerViewModel(log, _sizeCache, _dialogs);
        _isDarkTheme = Services.ThemeService.Normalize(Settings.Theme) == Services.ThemeService.Dark;
        Terminal = new TerminalViewModel(log, _dialogs);
        _startAtLastPath = Settings.StartAtLastPath;
        _terminalThemeIndex = Math.Max(0, TerminalThemes.All.ToList().FindIndex(t => t.Key == TerminalThemes.Find(Settings.TerminalTheme).Key));
        _terminalForeground = Settings.TerminalForeground ?? string.Empty;
        _terminalBackground = Settings.TerminalBackground ?? string.Empty;
        Terminal.ColorScheme = TerminalThemes.Resolve(Settings.TerminalTheme, _terminalForeground, _terminalBackground);
        _terminalShellIndex =Settings.TerminalShell == TerminalShell.CommandPrompt ? 1 : 0;
        _uiFontSize = Services.FontService.NormalizeUi(Settings.UiFontSize);
        _terminalFontSize = Services.FontService.NormalizeTerminal(Settings.TerminalFontSize);
        Terminal.FontSize = _terminalFontSize;
        Terminal.IsCollapsed = Settings.TerminalCollapsed;
        _sync = new PathSyncService(Explorer, Terminal, log) { Mode = Settings.SyncMode };
        _selectedSyncMode = SyncModes.First(o => o.Mode == Settings.SyncMode);

        BuildFavoriteSlots();

        Explorer.Navigated += (path, origin) =>
        {
            ClearInfo();
            _sync.OnExplorerNavigated(path, origin);
            MarkCurrentDrive(path);
            _ = RefreshDrivesAsync();
        };
        Explorer.ErrorOccurred += msg => InfoMessage = msg;
        Terminal.Notice += (msg, success) =>
        {
            if (success)
            {
                NotifySuccess(msg);
            }
            else
            {
                InfoMessage = msg;
            }
        };
        Terminal.CwdChanged += (path, fromRequest) =>
            _sync.OnTerminalCwdChanged(path, fromRequest ? ChangeOrigin.Sync : ChangeOrigin.User);
        _sync.Warning += w => InfoMessage = w.Message;

        // 하단 시스템 사용량: 측정은 백그라운드 타이머에서, 표시 갱신은 UI 스레드에서.
        _monitor = new SystemMonitor(log, MonitorIntervalMs);
        _monitor.Updated += u => Application.Current?.Dispatcher.BeginInvoke(() => ApplyUsage(u));
    }

    /// <summary>시작 경로(마지막 경로 → 사용자 폴더)로 탐색기를 열고 터미널을 같은 경로에서 시작한다.</summary>
    public void Initialize()
    {
        string start = ResolveStartPath();
        Explorer.NavigateTo(start, ChangeOrigin.Sync);
        Terminal.Start(Explorer.CurrentPath ?? start, Settings.TerminalShell);
        _terminalStarted = true;
        _ = RefreshDrivesAsync();
        _log.Info($"앱 시작: start={start}, mode={Settings.SyncMode}");
    }

    /// <summary>
    /// 드라이브 목록과 용량을 백그라운드에서 읽어 갱신한다(네트워크 드라이브가 UI를 막지 않도록).
    /// 이미 갱신 중이면 건너뛴다. 결과 적용은 UI 스레드(await 복귀 지점)에서 한다.
    /// </summary>
    private async Task RefreshDrivesAsync()
    {
        if (_drivesRefreshing)
        {
            return; // 진행 중인 갱신이 최신 용량을 가져오므로 중복 실행하지 않는다.
        }
        _drivesRefreshing = true;
        try
        {
            List<Core.Explorer.DriveEntry> entries = await Task.Run(Core.Explorer.DriveService.List);
            Drives.Clear();
            foreach (var e in entries)
            {
                Drives.Add(new DriveItemViewModel(e, OnDriveClick));
            }
            MarkCurrentDrive(Explorer.CurrentPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"드라이브 목록 갱신 실패: {ex.Message}");
        }
        finally
        {
            _drivesRefreshing = false;
        }
    }

    private void OnDriveClick(DriveItemViewModel drive)
    {
        Explorer.NavigateTo(drive.Root, ChangeOrigin.User);
    }

    /// <summary>현재 경로가 속한 드라이브만 강조한다.</summary>
    private void MarkCurrentDrive(string? path)
    {
        string? root = Core.Explorer.DriveService.RootOf(path);
        foreach (DriveItemViewModel d in Drives)
        {
            d.IsCurrent = root is not null && string.Equals(d.Root, root, StringComparison.OrdinalIgnoreCase);
        }
    }

    private string ResolveStartPath()
    {
        string? last = PathNormalizer.Normalize(Settings.LastPath);
        if (Settings.StartAtLastPath && last is not null && Directory.Exists(last))
        {
            return last;
        }
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    partial void OnSelectedSyncModeChanged(SyncModeOption value)
    {
        _sync.Mode = value.Mode;
        Settings.SyncMode = value.Mode;
        _log.Info($"동기화 모드 변경: {value.Mode}");
    }

    [RelayCommand]
    private void ClearInfo() => InfoMessage = string.Empty;

    private void BuildFavoriteSlots()
    {
        for (int i = 0; i < AppSettings.MaxFavorites; i++)
        {
            var slot = new FavoriteSlotViewModel(i + 1, OnFavoriteClick, RegisterCurrentFolder, ClearFavorite, RenameFavorite, MoveFavorite);
            if (i < Settings.Favorites.Count)
            {
                slot.Name = Settings.Favorites[i].Name;
                slot.Path = Settings.Favorites[i].Path;
            }
            Favorites.Add(slot);
        }
    }

    /// <summary>즐겨찾기 클릭: 등록된 칸이면 이동, 빈 칸이면 현재 폴더를 등록한다.</summary>
    private void OnFavoriteClick(FavoriteSlotViewModel slot)
    {
        if (slot.IsEmpty)
        {
            RegisterCurrentFolder(slot);
        }
        else
        {
            Explorer.NavigateTo(slot.Path, ChangeOrigin.User);
        }
    }

    private void RegisterCurrentFolder(FavoriteSlotViewModel slot)
    {
        string? current = Explorer.CurrentPath;
        if (current is null)
        {
            return;
        }
        slot.Path = current;
        slot.Name = Path.GetFileName(current) is { Length: > 0 } n ? n : current;
        _log.Info($"즐겨찾기 등록 #{slot.Index}: {current}");
    }

    /// <summary>즐겨찾기 표시 이름을 바꾼다(경로는 그대로). 같은 이름의 폴더를 구분할 때 쓴다.</summary>
    private void RenameFavorite(FavoriteSlotViewModel slot)
    {
        if (slot.IsEmpty)
        {
            return;
        }
        string? name = _dialogs.Prompt("즐겨찾기 이름 변경", "표시할 이름을 입력하세요.", slot.Name);
        if (name is not null)
        {
            slot.Name = name;
        }
    }

    /// <summary>칸의 내용(이름+경로)을 옆 칸과 맞바꿔 순서를 바꾼다. 끝 칸에서는 아무것도 하지 않는다.</summary>
    private void MoveFavorite(FavoriteSlotViewModel slot, int delta)
    {
        int to = slot.Index - 1 + delta;
        if (to < 0 || to >= Favorites.Count)
        {
            return;
        }
        FavoriteSlotViewModel other = Favorites[to];
        (slot.Name, other.Name) = (other.Name, slot.Name);
        (slot.Path, other.Path) = (other.Path, slot.Path);
    }

    private void ClearFavorite(FavoriteSlotViewModel slot)
    {
        slot.Path = string.Empty;
        slot.Name = string.Empty;
        _log.Info($"즐겨찾기 삭제 #{slot.Index}");
    }

    /// <summary>창 종료 시 호출: 현재 상태를 설정에 반영해 저장한다.</summary>
    public void SaveSettings()
    {
        Settings.Favorites = Favorites
            .Select(f => new FavoriteEntry { Name = f.Name, Path = f.Path })
            .ToList();
        Settings.LastPath = Explorer.CurrentPath ?? string.Empty;
        Settings.TerminalCollapsed = Terminal.IsCollapsed;
        _settingsService.Save(Settings);
        _sizeCache.Save();
        _log.Info("설정 저장");
    }

    public void Dispose()
    {
        _monitor.Dispose();
        Terminal.Dispose();
    }
}
