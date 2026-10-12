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

    public bool HasInfo => !string.IsNullOrEmpty(InfoMessage);

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
        MemoryText = $"{SizeFormatter.Format(u.MemoryUsedBytes)} / {SizeFormatter.Format(u.MemoryTotalBytes)}";
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
        Terminal = new TerminalViewModel(log);
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
        Terminal.Start(Explorer.CurrentPath ?? start);
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
        if (last is not null && Directory.Exists(last))
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
            var slot = new FavoriteSlotViewModel(i + 1, OnFavoriteClick, RegisterCurrentFolder, ClearFavorite);
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
