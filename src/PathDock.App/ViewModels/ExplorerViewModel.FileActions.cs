using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PathDock.App.Services;
using PathDock.Core.Explorer;
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
    private string _nameHeader = "이름";

    [ObservableProperty]
    private string _modifiedHeader = "수정한 날짜";

    [ObservableProperty]
    private string _sizeHeader = "크기";

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
        NameHeader = Mark(SortKey.Name, "이름");
        ModifiedHeader = Mark(SortKey.Modified, "수정한 날짜");
        SizeHeader = Mark(SortKey.Size, "크기");
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
        string? name = _dialogs.Prompt("이름 변경", "새 이름을 입력하세요.", item.Name);
        if (name is null || name == item.Name)
        {
            return; // 취소 또는 변화 없음
        }
        RunFileOperation($"이름을 바꿀 수 없습니다: {item.Name}", item.FullPath, () => FileOperations.Rename(item.FullPath, name));
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
            ? $"{(targets[0].IsDirectory ? "폴더(하위 항목 포함)" : "파일")} '{targets[0].Name}'"
            : $"선택한 {targets.Count}개 항목";
        string question = targets.All(t => FileOperations.UsesRecycleBin(t.FullPath))
            ? $"{what}을(를) 휴지통으로 보낼까요?"
            : $"{what}에 휴지통이 없는 드라이브의 항목이 있어 영구 삭제됩니다(복구 불가). 삭제할까요?";
        if (!_dialogs.Confirm("삭제 확인", question))
        {
            return;
        }
        RunBatch("삭제할 수 없습니다", targets.Select(t => t.FullPath).ToList(), p =>
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
            ErrorOccurred?.Invoke("클립보드를 사용할 수 없습니다. 잠시 후 다시 시도하세요.");
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
        var errors = new List<string>();
        var sources = new List<string>();
        await Task.Run(() =>
        {
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
        });
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
            ErrorOccurred?.Invoke($"붙여넣기 실패 {errors.Count}건 - {errors[0]}");
        }
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
            ErrorOccurred?.Invoke($"{failureMessage} ({errors.Count}건) - {errors[0]}");
        }
        if (_service.Current is { } current)
        {
            NavigateTo(current, ChangeOrigin.User);
        }
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
        string? name = _dialogs.Prompt("새 폴더", "새 폴더 이름을 입력하세요.", FileOperations.SuggestNewFolderName(current));
        if (name is null)
        {
            return;
        }
        RunFileOperation($"폴더를 만들 수 없습니다: {name}", current, () => FileOperations.CreateFolder(current, name));
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
