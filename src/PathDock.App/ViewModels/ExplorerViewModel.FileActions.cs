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

    /// <summary>선택 항목을 휴지통으로(Delete). 확인 창에서 "예"를 눌러야 실행되며 복구 가능하다.</summary>
    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedItem is not { } item)
        {
            return;
        }
        string kind = item.IsDirectory ? "폴더(하위 항목 포함)" : "파일";
        if (!_dialogs.Confirm("삭제 확인", $"{kind} '{item.Name}'을(를) 휴지통으로 보낼까요?"))
        {
            return;
        }
        RunFileOperation($"삭제할 수 없습니다: {item.Name}", item.FullPath, () =>
        {
            FileOperations.DeleteToRecycleBin(item.FullPath);
            return null;
        });
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
