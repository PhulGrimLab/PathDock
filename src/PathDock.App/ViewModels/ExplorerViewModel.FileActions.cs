using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PathDock.App.Services;
using PathDock.Core.Explorer;
using PathDock.Core.Localization;
using PathDock.Core.Sync;

namespace PathDock.App.ViewModels;

/// <summary>
/// ExplorerViewModel의 정렬과 파일 조작(이름 변경/삭제/새 폴더) 부분. 같은 클래스의 partial이라 상태(Items, 캐시)를 공유한다.
/// 조작이 성공하면 폴더 크기 캐시를 무효화(해당 폴더 + 상위)하고 목록을 새로 읽는다. 모든 멤버는 UI 스레드 전용.
/// </summary>
public sealed partial class ExplorerViewModel
{
    private readonly IDialogService _dialogs;
    private SortKey _sortKey = SortKey.Name;
    private bool _sortAscending = true;

    [ObservableProperty]
    private string _nameHeader = string.Empty;

    [ObservableProperty]
    private string _modifiedHeader = string.Empty;

    [ObservableProperty]
    private string _sizeHeader = string.Empty;

    private IReadOnlyList<FileItemViewModel> _selection = Array.Empty<FileItemViewModel>();

    /// <summary>목록의 현재 다중 선택(뷰가 선택이 바뀔 때 알려 준다). 비어 있으면 <see cref="SelectedItem"/>을 대상으로 한다.</summary>
    public void SetSelection(IEnumerable<FileItemViewModel> items) => _selection = items.ToList();

    private List<FileItemViewModel> Targets() =>
        _selection.Count > 0 ? _selection.ToList() : (SelectedItem is { } one ? new List<FileItemViewModel> { one } : new List<FileItemViewModel>());

    public SortKey CurrentSortKey => _sortKey;
    public bool SortAscending => _sortAscending;

    /// <summary>열 머리글 클릭: 같은 기준이면 방향 반전, 다른 기준이면 오름차순으로 시작(크기/날짜는 내림차순이 자연스럽지만 일관성을 우선).</summary>
    [RelayCommand]
    private void SortBy(SortKey key)
    {
        if (_sortKey == key)
        {
            _sortAscending = !_sortAscending;
        }
        else
        {
            _sortKey = key;
            _sortAscending = true;
        }
        UpdateHeaders();
        ResortItems();
        _log.Info($"정렬: {_sortKey} {(_sortAscending ? "오름차순" : "내림차순")}");
    }

    private void UpdateHeaders()
    {
        string Mark(SortKey k, string text) => _sortKey == k ? $"{text} {(_sortAscending ? "▲" : "▼")}" : text;
        NameHeader = Mark(SortKey.Name, Loc.T("이름", "Name"));
        ModifiedHeader = Mark(SortKey.Modified, Loc.T("수정한 날짜", "Date modified"));
        SizeHeader = Mark(SortKey.Size, Loc.T("크기", "Size"));
    }

    /// <summary>언어가 바뀌었을 때 코드에서 만든 문구(머리글, 폴더 이름, 요약, 각 줄의 크기·개수)를 새 언어로 다시 만든다.</summary>
    public void RefreshLanguage()
    {
        UpdateHeaders();
        OnPropertyChanged(nameof(CurrentFolderName));
        foreach (FileItemViewModel item in Items)
        {
            item.RefreshLanguage();
        }
        UpdateSummary(Items.ToList(), Items.Count(i => i.IsDirectory && i.Stats is not null && !i.IsStale));
    }

    private List<FileItemViewModel> SortItems(IEnumerable<FileItemViewModel> items)
    {
        return FileSorter.Sort(items, _sortKey, _sortAscending,
            i => i.IsDirectory, i => i.Name, i => i.Item.Modified,
            i => i.IsDirectory ? i.Stats?.TotalSize : i.Item.Size);
    }

    /// <summary>현재 목록을 다시 정렬한다(선택 항목 유지).</summary>
    private void ResortItems()
    {
        FileItemViewModel? selected = SelectedItem;
        List<FileItemViewModel> sorted = SortItems(Items.ToList());
        Items.ReplaceAll(sorted);
        SelectedItem = selected;
    }

    private void ResortIfBySize()
    {
        if (_sortKey == SortKey.Size)
        {
            ResortItems();
        }
        else
        {
            // 이름/날짜 정렬은 폴더 크기 도착과 무관하다.
        }
    }

    /// <summary>선택 항목 이름 변경(F2): 입력창 → 검증 → 변경 → 캐시 무효화 → 목록 갱신 → 새 이름 선택.</summary>
    [RelayCommand]
    private void RenameSelected()
    {
        if (SelectedItem is not { } item)
        {
            return;
        }
        string? name = _dialogs.Prompt(Loc.T("이름 변경", "Rename"), Loc.T("새 이름을 입력하세요.", "Enter the new name."), item.Name);
        if (name is null || name == item.Name)
        {
            return; // 취소 또는 변화 없음
        }
        RunFileOperation(Loc.T($"이름을 바꿀 수 없습니다: {item.Name}", $"Could not rename: {item.Name}"), item.FullPath, () => FileOperations.Rename(item.FullPath, name));
    }

    /// <summary>
    /// 선택 항목(여러 개 가능)을 삭제(Delete). 확인 창에서 "예"를 눌러야 실행된다.
    /// 고정 디스크는 휴지통으로(복구 가능), 그 밖의 드라이브는 영구 삭제이며 문구로 알린다.
    /// </summary>
    [RelayCommand]
    private void DeleteSelected()
    {
        List<FileItemViewModel> targets = Targets();
        if (targets.Count == 0)
        {
            return;
        }
        string what = targets.Count == 1
            ? Loc.T($"{(targets[0].IsDirectory ? "폴더(하위 항목 포함)" : "파일")} '{targets[0].Name}'", $"{(targets[0].IsDirectory ? "the folder (including its contents)" : "the file")} '{targets[0].Name}'")
            : Loc.T($"선택한 {targets.Count}개 항목", $"the {targets.Count} selected items");
        string question = targets.All(t => FileOperations.UsesRecycleBin(t.FullPath))
            ? Loc.T($"{what}을(를) 휴지통으로 보낼까요?", $"Move {what} to the Recycle Bin?")
            : Loc.T($"{what}에 휴지통이 없는 드라이브의 항목이 있어 영구 삭제됩니다(복구 불가). 삭제할까요?", $"Some of {what} are on a drive without a Recycle Bin and will be permanently deleted (cannot be recovered). Delete?");
        if (!_dialogs.Confirm(Loc.T("삭제 확인", "Confirm delete"), question))
        {
            return;
        }
        RunBatch(Loc.T("삭제할 수 없습니다", "Could not delete"), targets.Select(t => t.FullPath).ToList(), p =>
        {
            FileOperations.DeleteToRecycleBin(p);
            return p;
        });
    }

    /// <summary>선택 항목을 클립보드에 복사한다(Ctrl+C).</summary>
    [RelayCommand]
    private void CopySelected() => PutOnClipboard(cut: false);

    /// <summary>선택 항목을 클립보드에 잘라내기한다(Ctrl+X). 붙여넣기 때 원본이 옮겨진다.</summary>
    [RelayCommand]
    private void CutSelected() => PutOnClipboard(cut: true);

    private void PutOnClipboard(bool cut)
    {
        List<string> paths = Targets().Select(t => t.FullPath).ToList();
        if (paths.Count == 0)
        {
            return;
        }
        if (!FileClipboard.TrySet(paths, cut))
        {
            ErrorOccurred?.Invoke(Loc.T("클립보드를 사용할 수 없습니다. 잠시 후 다시 시도하세요.", "The clipboard is not available. Please try again in a moment."));
        }
        else
        {
            _log.Info($"클립보드 {(cut ? "잘라내기" : "복사")}: {paths.Count}개");
        }
    }

    // ----- 붙여넣기 진행 표시: 0.3초 넘게 걸리면 목록 아래에 진행 막대와 취소 버튼을 보여준다. -----

    private const int OperationVisibleDelayMs = 300;
    private CancellationTokenSource? _operationCts;
    private bool _operationBusy;

    [ObservableProperty]
    private bool _isOperationVisible;

    [ObservableProperty]
    private string _operationText = string.Empty;

    /// <summary>진행률(0~100). 전체 크기를 아직 모르면 0.</summary>
    [ObservableProperty]
    private double _operationPercent;

    /// <summary>진행 중인 붙여넣기를 취소한다. 이미 끝난 항목은 남고, 복사 중이던 항목의 반쯤 복사된 결과는 지운다.</summary>
    [RelayCommand]
    private void CancelOperation() => _operationCts?.Cancel();

    private bool CanPaste() => !_operationBusy;

    /// <summary>클립보드의 파일을 현재 폴더에 붙여넣는다(Ctrl+V). 복사/이동은 백그라운드에서 실행하고 진행 상황을 보여 준다.</summary>
    [RelayCommand(CanExecute = nameof(CanPaste))]
    private async Task PasteAsync()
    {
        if (_operationBusy || _service.Current is not { } dest || !FileClipboard.TryGet(out List<string> paths, out bool cut))
        {
            return;
        }
        await TransferAsync(paths, dest, cut, fromClipboard: true);
    }

    /// <summary>끌어 놓기(드롭)로 항목을 대상 폴더에 복사/이동한다. 붙여넣기와 같은 진행 표시·취소·오류 처리를 쓴다.</summary>
    public async Task DropAsync(IReadOnlyList<string> sources, string destFolder, DropEffect effect)
    {
        if (effect == DropEffect.None || sources.Count == 0)
        {
            return;
        }
        if (_operationBusy)
        {
            ErrorOccurred?.Invoke(Loc.T("다른 복사/이동 작업이 진행 중입니다. 끝난 뒤에 다시 시도하세요", "Another copy/move is in progress. Try again when it finishes"));
            return;
        }
        await TransferAsync(sources.ToList(), destFolder, effect == DropEffect.Move, fromClipboard: false);
    }

    /// <summary>끌어 놓을 때의 동작(복사/이동/불가)을 정한다(규칙은 Core의 <see cref="DropRules"/>).</summary>
    public DropEffect GetDropEffect(IReadOnlyList<string> sources, string destFolder, bool ctrlHeld, bool shiftHeld)
    {
        // DragOver는 마우스가 움직이는 내내 수십 ms마다 불리는데 규칙 판정은 디스크를 확인한다(폴더 존재, 링크를 푼 실제 경로).
        // 같은 입력이면 잠깐(DropEffectCacheMs) 직전 결과를 재사용해 UI 스레드의 I/O를 줄인다. UI 스레드에서만 호출된다.
        string key = string.Join('|', sources) + '|' + destFolder + (ctrlHeld ? "C" : "-") + (shiftHeld ? "S" : "-");
        long now = Environment.TickCount64;
        if (key == _dropEffectKey && now - _dropEffectTicks < DropEffectCacheMs)
        {
            return _dropEffect;
        }
        _dropEffect = DropRules.Decide(sources, destFolder, ctrlHeld, shiftHeld);
        _dropEffectKey = key;
        _dropEffectTicks = now;
        return _dropEffect;
    }

    private const int DropEffectCacheMs = 500;
    private string? _dropEffectKey;
    private long _dropEffectTicks;
    private DropEffect _dropEffect;

    /// <summary>복사/이동 본체: 크기를 재고, 진행 표시와 함께 백그라운드에서 실행하고, 끝나면 캐시를 무효화하고 목록을 새로 읽는다.</summary>
    private async Task TransferAsync(List<string> paths, string dest, bool cut, bool fromClipboard)
    {
        _operationBusy = true;
        PasteCommand.NotifyCanExecuteChanged();
        using var cts = new CancellationTokenSource();
        _operationCts = cts;
        string verb = cut ? Loc.T("이동", "Moving") : Loc.T("복사", "Copying");
        OperationText = Loc.T($"{verb} 준비 중...", $"{verb}: preparing...");
        OperationPercent = 0;
        _ = ShowOperationAfterDelayAsync(cts);
        var watch = Stopwatch.StartNew();
        List<string> sources = new();
        List<string> errors = new();
        bool cancelled = false;
        try
        {
            long total = await Task.Run(() => paths.Sum(p => FileOperations.MeasureBytes(p, cts.Token)), cts.Token);
            long done = 0;
            var progress = new Progress<long>(delta =>
            {
                done += delta; // Progress<T>는 만든 스레드(UI)에서 호출된다.
                OperationPercent = total > 0 ? Math.Min(100, done * 100.0 / total) : 0;
                OperationText = Loc.T($"{verb} 중... {SizeFormatter.Format(done)} / {SizeFormatter.Format(total)} ({OperationPercent:0}%)", $"{verb}... {SizeFormatter.Format(done)} / {SizeFormatter.Format(total)} ({OperationPercent:0}%)");
            });
            (sources, errors, cancelled) = await Task.Run(() => CopyOrMoveInto(paths, dest, cut, progress, cts.Token));
        }
        catch (OperationCanceledException)
        {
            cancelled = true; // 준비(크기 계산) 중에 취소됨.
        }
        finally
        {
            bool wasVisible = IsOperationVisible;
            _operationCts = null;
            _operationBusy = false;
            IsOperationVisible = false;
            PasteCommand.NotifyCanExecuteChanged();
            _log.Info($"붙여넣기({(cut ? "이동" : "복사")}): 성공 {sources.Count}, 실패 {errors.Count}{(cancelled ? ", 취소됨" : string.Empty)} -> {dest} ({watch.ElapsedMilliseconds}ms)");
            _cache.InvalidateWithAncestors(dest);
            if (cut)
            {
                foreach (string p in sources)
                {
                    _cache.InvalidateWithAncestors(p);
                }
                if (!cancelled && fromClipboard)
                {
                    FileClipboard.Clear();
                }
            }
            if (errors.Count > 0)
            {
                ErrorOccurred?.Invoke(Loc.T($"붙여넣기 실패 {errors.Count}건 - {errors[0]}", $"Paste failed for {errors.Count} item(s) - {errors[0]}"));
            }
            else if (cancelled)
            {
                Notified?.Invoke(Loc.T($"취소했습니다(완료된 {sources.Count}개는 그대로 남아 있습니다)", $"Cancelled ({Loc.Plural(sources.Count, "item", "items")} already done remain)"));
            }
            else if (wasVisible)
            {
                Notified?.Invoke(cut ? Loc.T("이동을 마쳤습니다", "Move finished") : Loc.T("복사를 마쳤습니다", "Copy finished"));
            }
            Reload();
        }
    }

    private async Task ShowOperationAfterDelayAsync(CancellationTokenSource cts)
    {
        await Task.Delay(OperationVisibleDelayMs);
        if (_operationBusy && ReferenceEquals(_operationCts, cts))
        {
            IsOperationVisible = true; // 금방 끝나는 작업은 막대를 번쩍이지 않는다.
        }
    }

    /// <summary>각 경로를 대상 폴더로 복사(또는 이동)한다. 성공한 원본 경로·실패 문구·취소 여부를 돌려준다. 백그라운드 스레드용.</summary>
    private static (List<string> Sources, List<string> Errors, bool Cancelled) CopyOrMoveInto(IReadOnlyList<string> paths, string dest, bool cut, IProgress<long> progress, CancellationToken ct)
    {
        var sources = new List<string>();
        var errors = new List<string>();
        foreach (string p in paths)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                if (cut)
                {
                    FileOperations.MoveInto(p, dest, progress, ct);
                }
                else
                {
                    FileOperations.CopyInto(p, dest, progress, ct);
                }
                sources.Add(p);
            }
            catch (OperationCanceledException)
            {
                return (sources, errors, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
            {
                // 경로에 ':' 같은 글자가 있어 NotSupportedException이 나는 항목 하나 때문에 나머지 복사가 멈추지 않게 항목 단위로 처리한다.
                errors.Add($"{Path.GetFileName(p)}: {ex.Message}");
            }
        }
        return (sources, errors, false);
    }


    /// <summary>현재 폴더를 같은 경로로 다시 읽는다(Navigated 이벤트는 발생하지 않는다).</summary>
    private void Reload()
    {
        if (_service.Current is { } current)
        {
            NavigateTo(current, ChangeOrigin.User);
        }
    }


    /// <summary>여러 경로에 같은 조작을 적용한다. 실패는 모아서 한 번에 알리고, 끝나면 캐시를 무효화하고 목록을 새로 읽는다.</summary>
    private void RunBatch(string failureMessage, IReadOnlyList<string> paths, Func<string, string?> operation)
    {
        var errors = new List<string>();
        foreach (string p in paths)
        {
            try
            {
                operation(p);
                _cache.InvalidateWithAncestors(p);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OperationCanceledException)
            {
                _log.Warn($"파일 조작 실패: {p} ({ex.GetType().Name}: {ex.Message})");
                errors.Add($"{Path.GetFileName(p)}: {ex.Message}");
            }
        }
        _log.Info($"파일 조작 완료: {paths.Count - errors.Count}/{paths.Count}");
        if (errors.Count > 0)
        {
            ErrorOccurred?.Invoke(Loc.T($"{failureMessage} ({errors.Count}건) - {errors[0]}", $"{failureMessage} ({errors.Count} item(s)) - {errors[0]}"));
        }
        Reload();
    }

    /// <summary>현재 폴더에 새 폴더 만들기(Ctrl+Shift+N).</summary>
    [RelayCommand]
    private void NewFolder()
    {
        string? current = _service.Current;
        if (current is null)
        {
            return;
        }
        string? name = _dialogs.Prompt(Loc.T("새 폴더", "New folder"), Loc.T("새 폴더 이름을 입력하세요.", "Enter the new folder name."), FileOperations.SuggestNewFolderName(current));
        if (name is null)
        {
            return;
        }
        RunFileOperation(Loc.T($"폴더를 만들 수 없습니다: {name}", $"Could not create the folder: {name}"), current, () => FileOperations.CreateFolder(current, name));
    }

    /// <summary>
    /// 파일 조작 공통 처리: 실행 → 실패 시 오류 알림 → 성공 시 영향받는 경로의 크기 캐시 무효화 → 목록 새로 읽기 →
    /// 결과 경로(이름 변경/새 폴더)가 있으면 선택. <paramref name="affectedPath"/>는 변경 전 대상(또는 새 폴더의 부모)이다.
    /// </summary>
    private void RunFileOperation(string failureMessage, string affectedPath, Func<string?> operation)
    {
        string? resultPath;
        try
        {
            resultPath = operation();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OperationCanceledException)
        {
            _log.Warn($"파일 조작 실패: {failureMessage} ({ex.GetType().Name}: {ex.Message})");
            ErrorOccurred?.Invoke($"{failureMessage} - {ex.Message}");
            return;
        }

        _cache.InvalidateWithAncestors(affectedPath);
        _log.Info($"파일 조작 완료: {affectedPath} -> {resultPath ?? "(삭제)"}");
        if (_service.Current is { } current)
        {
            NavigateTo(current, ChangeOrigin.User); // 같은 경로 새로고침: Navigated 이벤트는 발생하지 않는다.
            if (resultPath is not null)
            {
                SelectedItem = Items.FirstOrDefault(i => Core.Paths.PathNormalizer.AreSame(i.FullPath, resultPath));
            }
        }
    }
}
