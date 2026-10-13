using System.IO;
using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PathDock.Core.Explorer;
using PathDock.Core.Logging;
using PathDock.Core.Sync;

namespace PathDock.App.ViewModels;

/// <summary>
/// 파일 탐색 패널의 ViewModel. Owner: MainViewModel, Lifetime: 앱 전체. UI 스레드 전용.
/// 탐색 결과는 <see cref="Navigated"/> 이벤트로 알리고(출처 포함), 동기화 서비스의 대상(<see cref="IPathSyncTarget"/>)으로도 쓰인다.
/// </summary>
public sealed partial class ExplorerViewModel : ObservableObject, IPathSyncTarget
{
    private readonly ExplorerService _service = new();
    private readonly ILog _log;

    public ObservableCollection<FileItemViewModel> Items { get; } = new();

    /// <summary>경로 표시줄의 텍스트(편집 가능). Enter로 이동한다.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentFolderName))]
    private string _pathText = string.Empty;

    /// <summary>현재 폴더 이름(카드 머리글용). 드라이브 루트면 "C:\" 그대로.</summary>
    public string CurrentFolderName
    {
        get
        {
            string p = _service.Current ?? PathText;
            if (string.IsNullOrEmpty(p))
            {
                return "폴더";
            }
            string leaf = Path.GetFileName(p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return leaf.Length > 0 ? leaf : p;
        }
    }

    [ObservableProperty]
    private FileItemViewModel? _selectedItem;

    // ----- 이름 필터(Ctrl+F): 현재 폴더 목록에서 이름에 입력한 글자가 들어간 항목만 보인다. 이동하면 해제된다. -----

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private bool _isFilterVisible;

    partial void OnFilterTextChanged(string value)
    {
        string needle = value.Trim();
        System.ComponentModel.ICollectionView view = System.Windows.Data.CollectionViewSource.GetDefaultView(Items);
        view.Filter = needle.Length == 0
            ? null
            : o => o is FileItemViewModel f && f.Name.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private void ShowFilter() => IsFilterVisible = true;

    [RelayCommand]
    private void CloseFilter()
    {
        FilterText = string.Empty;
        IsFilterVisible = false;
    }
    /// <summary>상위 2단계 폴더 열(왼쪽부터 조부모, 부모). 현재 폴더 왼쪽에 Mdir 스타일로 표시한다.</summary>
    public AncestorColumnViewModel GrandParent { get; }
    public AncestorColumnViewModel Parent { get; }

    /// <summary>목록 아래 요약 줄. 예: "폴더 3개 · 파일 8개 (12.3 MB) · 전체 45 MB".</summary>
    [ObservableProperty]
    private string _summaryText = string.Empty;

    // 폴더 크기 계산/상위 열 로딩을 이동마다 취소·무효화하기 위한 토큰. UI 스레드에서만 접근.
    private CancellationTokenSource? _backgroundCts;
    private const int SizeWorkers = 3;
    private const int ErrorCancelled = 1223; // ERROR_CANCELLED
    private readonly FolderSizeCache _cache;
    // 새로 고침 버튼으로 요청한 강제 재계산 표시(다음 StartBackgroundWork에서 소비).
    private bool _forceNextRecalc;

    /// <summary>이동이 성공했을 때(경로, 출처). 같은 경로 새로고침은 알리지 않는다.</summary>
    public event Action<string, ChangeOrigin>? Navigated;

    /// <summary>이동 실패 등 사용자에게 보여줄 오류.</summary>
    public event Action<string>? ErrorOccurred;

    public ExplorerViewModel(ILog log, FolderSizeCache cache, Services.IDialogService dialogs)
    {
        _log = log;
        _cache = cache;
        _dialogs = dialogs;
        UpdateHeaders();
        GrandParent = new AncestorColumnViewModel("AncestorColumn_2", p => NavigateTo(p, ChangeOrigin.User), marksHiddenAncestors: true);
        Parent = new AncestorColumnViewModel("AncestorColumn_1", p => NavigateTo(p, ChangeOrigin.User));
    }

    public string? CurrentPath => _service.Current;

    /// <summary>동기화 요청: 터미널에서 온 경로로 이동한다. 항상 즉시 처리(true).</summary>
    bool IPathSyncTarget.RequestChange(string path)
    {
        NavigateTo(path, ChangeOrigin.Sync);
        return true;
    }

    /// <summary>
    /// 경로로 이동한다. 실패하면 현재 위치를 유지하고 오류를 알린 뒤 false. 성공하면 목록을 갱신하고
    /// (경로가 실제로 바뀐 경우에만) <see cref="Navigated"/>를 발생시킨다.
    /// </summary>
    public bool NavigateTo(string path, ChangeOrigin origin)
    {
        string? previous = _service.Current;
        List<FileItem> items;
        try
        {
            items = _service.NavigateTo(path);
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or UnauthorizedAccessException or IOException or ArgumentException)
        {
            ReportNavigationFailure(path, origin, ex);
            return false;
        }

        ApplyListing(items);
        bool changed = !Core.Paths.PathNormalizer.AreSame(previous, _service.Current);
        _log.Info($"탐색기 이동: {_service.Current} (origin={origin}, changed={changed})");
        if (changed)
        {
            Navigated?.Invoke(_service.Current!, origin);
        }
        else
        {
            // 새로고침: 목록만 갱신했으므로 알림 없음.
        }
        return true;
    }

    /// <summary>이동 실패를 기록하고 알린다. 주소창을 현재 경로로 되돌리되, 사용자가 직접 친 경로면 고쳐 쓸 수 있게 남겨 둔다.</summary>
    private void ReportNavigationFailure(string path, ChangeOrigin origin, Exception ex)
    {
        _log.Warn($"탐색기 이동 실패: {path} ({ex.GetType().Name})");
        bool typedByUser = origin == ChangeOrigin.User && string.Equals(PathText, path, StringComparison.Ordinal);
        if (!typedByUser)
        {
            PathText = _service.Current ?? string.Empty;
        }
        ErrorOccurred?.Invoke(ex is UnauthorizedAccessException
            ? $"접근 권한이 없습니다: {path}"
            : $"폴더를 열 수 없습니다: {path}");
    }

    private void ApplyListing(List<FileItem> items)
    {
        if (FilterText.Length > 0 || IsFilterVisible)
        {
            CloseFilter(); // 다른 폴더로 이동하면 필터를 해제한다(새로고침도 포함).
        }
        Items.Clear();
        foreach (FileItemViewModel vm in SortItems(items.Select(i => new FileItemViewModel(i))))
        {
            Items.Add(vm);
        }
        PathText = _service.Current ?? string.Empty;
        OnPropertyChanged(nameof(CurrentFolderName));
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
        GoUpCommand.NotifyCanExecuteChanged();
        StartBackgroundWork();
    }

    /// <summary>
    /// 이동 직후 시작하는 백그라운드 작업: (1) 상위 2단계 열 로딩, (2) 하위 폴더별 크기 계산.
    /// 새 이동이 시작되면 이전 작업은 취소되고, 늦게 끝난 결과는 버려진다.
    /// </summary>
    private void StartBackgroundWork()
    {
        _backgroundCts?.Cancel();
        _backgroundCts?.Dispose();
        var cts = new CancellationTokenSource();
        _backgroundCts = cts;

        string? current = _service.Current;
        var snapshot = Items.ToList();
        UpdateSummary(snapshot, 0);
        _ = LoadAncestorsAsync(current, cts.Token);
        bool force = _forceNextRecalc;
        _forceNextRecalc = false;
        _ = CalculateFolderSizesAsync(snapshot, force, cts.Token);
    }

    /// <summary>부모/조부모 폴더의 하위 폴더 목록을 읽어 열에 채운다(현재 경로가 지나는 폴더를 선택 표시).</summary>
    private async Task LoadAncestorsAsync(string? current, CancellationToken ct)
    {
        string? parent = current is null ? null : ExplorerService.GetParent(current);
        string? grand = parent is null ? null : ExplorerService.GetParent(parent);
        try
        {
            (List<FileItem> parentDirs, List<FileItem> grandDirs) = await Task.Run(() => (
                parent is null ? new List<FileItem>() : ExplorerService.ListDirectories(parent),
                grand is null ? new List<FileItem>() : ExplorerService.ListDirectories(grand)), ct);
            ct.ThrowIfCancellationRequested();
            Parent.Load(parent, parentDirs, current);
            GrandParent.Load(grand, grandDirs, parent);
        }
        catch (OperationCanceledException)
        {
            // 그 사이 다른 폴더로 이동함: 결과를 적용하지 않는다.
        }
    }

    /// <summary>
    /// 현재 목록의 폴더들의 전체 크기/개수를 최대 <see cref="SizeWorkers"/>개씩 병렬 계산해 채운다.
    /// 큰 폴더는 오래 걸리므로 항목마다 완료되는 대로 표시하고 요약을 갱신한다. 취소되면 조용히 끝난다.
    /// </summary>
    private async Task CalculateFolderSizesAsync(List<FileItemViewModel> snapshot, bool force, CancellationToken ct)
    {
        var dirs = snapshot.Where(i => i.IsDirectory).ToList();
        if (dirs.Count == 0)
        {
            return;
        }

        (int done, List<FileItemViewModel> todo) = ApplyCachedSizes(dirs, force);
        UpdateSummary(snapshot, done);
        ResortIfBySize();
        if (todo.Count == 0)
        {
            return;
        }

        await ComputeSizesAsync(snapshot, todo, done, ct);
        if (!ct.IsCancellationRequested)
        {
            ResortIfBySize(); // 크기 정렬 중이면 모든 폴더 크기가 확정된 뒤 한 번 정렬한다(계산 중 행이 계속 움직이지 않도록).
        }
    }

    /// <summary>
    /// 캐시 선적용: Fresh는 그대로 확정(재계산 없음), Stale은 값을 먼저 보여주되 다시 계산, 강제 새로 고침이면 캐시를 무시한다.
    /// 확정된 폴더 수와 아직 계산해야 할 폴더 목록을 돌려준다.
    /// </summary>
    private (int Done, List<FileItemViewModel> Todo) ApplyCachedSizes(List<FileItemViewModel> dirs, bool force)
    {
        int done = 0;
        var todo = new List<FileItemViewModel>();
        foreach (FileItemViewModel d in dirs)
        {
            CacheFreshness state = CacheFreshness.Missing;
            FolderStats? hit = null;
            if (!force)
            {
                state = _cache.Lookup(d.FullPath, out hit);
            }
            if (hit is not null)
            {
                d.SetStats(hit, stale: state == CacheFreshness.Stale);
            }
            if (state == CacheFreshness.Fresh)
            {
                done++;
            }
            else
            {
                todo.Add(d);
            }
        }
        return (done, todo);
    }

    /// <summary>캐시에 없거나 오래된 폴더만 백그라운드에서 계산한다(최대 SizeWorkers개 동시). 끝나는 대로 표시와 요약을 갱신한다.</summary>
    private async Task ComputeSizesAsync(List<FileItemViewModel> snapshot, List<FileItemViewModel> todo, int done, CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(SizeWorkers);
        var tasks = todo.Select(async d =>
        {
            try
            {
                await gate.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            try
            {
                FolderStats stats = await Task.Run(() => FolderSizeCalculator.Calculate(d.FullPath, ct), ct);
                ct.ThrowIfCancellationRequested(); // 계산이 끝난 직후 다른 폴더로 이동했다면 오래된 결과로 요약을 덮어쓰지 않는다.
                _cache.Store(d.FullPath, stats);
                d.SetStats(stats);
                done++; // await 뒤에는 UI 스레드로 돌아오므로 동시 증가 문제가 없다.
                UpdateSummary(snapshot, done);
            }
            catch (OperationCanceledException)
            {
                // 이동으로 취소됨.
            }
            finally
            {
                gate.Release();
            }
        }).ToList();
        await Task.WhenAll(tasks);
    }
    /// <summary>요약 줄 갱신. 폴더 크기 계산이 끝나지 않았으면 진행 상황을 함께 표시한다.</summary>
    private void UpdateSummary(List<FileItemViewModel> snapshot, int dirsDone)
    {
        int dirCount = snapshot.Count(i => i.IsDirectory);
        var files = snapshot.Where(i => !i.IsDirectory).ToList();
        long fileBytes = files.Sum(f => f.Item.Size);
        string text = $"폴더 {dirCount:N0}개 · 파일 {files.Count:N0}개 ({SizeFormatter.Format(fileBytes)})";
        if (dirCount > 0)
        {
            if (dirsDone < dirCount)
            {
                text += $" · 폴더 크기 계산 중 {dirsDone}/{dirCount}";
            }
            else
            {
                long dirBytes = snapshot.Where(i => i.IsDirectory && i.Stats is not null).Sum(i => i.Stats!.TotalSize);
                bool capped = snapshot.Any(i => i.IsDirectory && i.OverLimit);
                text += $" · 전체 {SizeFormatter.Format(fileBytes + dirBytes)}{(capped ? " 이상" : string.Empty)}";
            }
        }
        SummaryText = text;
    }

    [RelayCommand]
    private void NavigatePath()
    {
        NavigateTo(PathText, ChangeOrigin.User);
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        MoveInHistory(_service.GoBack());
    }

    private bool CanGoBack() => _service.CanGoBack;

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward()
    {
        MoveInHistory(_service.GoForward());
    }

    private bool CanGoForward() => _service.CanGoForward;

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    private void GoUp()
    {
        if (_service.Current is not null && ExplorerService.GetParent(_service.Current) is { } parent)
        {
            NavigateTo(parent, ChangeOrigin.User);
        }
        else
        {
            // 루트: 위로 갈 곳 없음.
        }
    }

    private bool CanGoUp() => _service.Current is not null && ExplorerService.GetParent(_service.Current) is not null;

    [RelayCommand]
    private void Refresh()
    {
        if (_service.Current is not null)
        {
            _forceNextRecalc = true; // 새로 고침은 폴더 크기도 캐시를 무시하고 다시 계산한다.
            NavigateTo(_service.Current, ChangeOrigin.User);
        }
    }

    /// <summary>폴더는 진입, 파일은 연결 프로그램으로 연다.</summary>
    [RelayCommand]
    private void OpenItem(FileItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }
        if (item.IsDirectory)
        {
            NavigateTo(item.FullPath, ChangeOrigin.User);
            return;
        }
        try
        {
            // 셸 연결(UseShellExecute): exe는 실행, 이미지/문서/영상 등은 Windows가 지정한 대표 프로그램으로 연다.
            // 작업 폴더를 파일이 있는 폴더로 두어 상대 경로를 쓰는 실행 파일/스크립트가 정상 동작하게 한다.
            Process.Start(new ProcessStartInfo(item.FullPath)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(item.FullPath) ?? string.Empty,
            })?.Dispose();
            _log.Info($"파일 열기: {item.FullPath}");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            // 사용자가 UAC 승격 창에서 취소함: 오류가 아니므로 알리지 않는다.
            _log.Info($"파일 열기 취소(UAC): {item.FullPath}");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // 연결 프로그램이 없는 형식(ERROR_NO_ASSOCIATION 등)도 여기로 온다.
            _log.Warn($"파일 열기 실패: {item.FullPath} ({ex.Message})");
            ErrorOccurred?.Invoke($"파일을 열 수 없습니다: {item.Name} (연결된 프로그램이 없거나 실행할 수 없습니다)");
        }
    }

    /// <summary>뒤로/앞으로: 이력 포인터는 이미 움직였으므로 목록만 읽고, 실패하면 오류를 알린다.</summary>
    private void MoveInHistory(string? target)
    {
        if (target is null)
        {
            return;
        }
        try
        {
            ApplyListing(ExplorerService.List(target));
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or UnauthorizedAccessException or IOException)
        {
            _log.Warn($"이력 이동 실패: {target} ({ex.GetType().Name})");
            ErrorOccurred?.Invoke($"폴더를 열 수 없습니다: {target}");
            return;
        }
        _log.Info($"탐색기 이력 이동: {target}");
        Navigated?.Invoke(target, ChangeOrigin.User);
    }
}
