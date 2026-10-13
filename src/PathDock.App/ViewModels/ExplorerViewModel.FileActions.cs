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
        Items.Clear();
        foreach (FileItemViewModel i in sorted)
        {
            Items.Add(i);
        }
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

    /// <summary>클립보드의 파일을 현재 폴더에 붙여넣는다(Ctrl+V). 복사/이동은 백그라운드에서 실행한다.</summary>
    [RelayCommand]
    private async Task PasteAsync()
    {
        if (_service.Current is not { } dest || !FileClipboard.TryGet(out List<string> paths, out bool cut))
        {
            return;
        }
        (List<string> sources, List<string> errors) = await Task.Run(() => CopyOrMoveInto(paths, dest, cut));
        _log.Info($"붙여넣기({(cut ? "이동" : "복사")}): 성공 {sources.Count}, 실패 {errors.Count} -> {dest}");
        _cache.InvalidateWithAncestors(dest);
        if (cut)
        {
            foreach (string p in sources)
            {
                _cache.InvalidateWithAncestors(p);
            }
            FileClipboard.Clear();
        }
        if (errors.Count > 0)
        {
            ErrorOccurred?.Invoke(Loc.T($"붙여넣기 실패 {errors.Count}건 - {errors[0]}", $"Paste failed for {errors.Count} item(s) - {errors[0]}"));
        }
        Reload();
    }

    /// <summary>각 경로를 대상 폴더로 복사(또는 이동)한다. 성공한 원본 경로와 실패 문구를 따로 모아 돌려준다. 백그라운드 스레드용.</summary>
    private static (List<string> Sources, List<string> Errors) CopyOrMoveInto(IReadOnlyList<string> paths, string dest, bool cut)
    {
        var sources = new List<string>();
        var errors = new List<string>();
        foreach (string p in paths)
        {
            try
            {
                if (cut)
                {
                    FileOperations.MoveInto(p, dest);
                }
                else
                {
                    FileOperations.CopyInto(p, dest);
                }
                sources.Add(p);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                errors.Add($"{Path.GetFileName(p)}: {ex.Message}");
            }
        }
        return (sources, errors);
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
