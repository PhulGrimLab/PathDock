using System.Collections.ObjectModel;
using PathDock.Core.Localization;
using PathDock.Core.Logging;
using PathDock.Core.Settings;

namespace PathDock.App.ViewModels;

/// <summary>
/// 즐겨찾기 10칸: 클릭하면 이동(빈 칸이면 현재 폴더 등록), 이름 바꾸기, 순서 이동, 비우기, 전체 초기화.
/// Owner: MainViewModel, Lifetime: 앱 전체. UI 스레드 전용. 바뀔 때마다 <c>persist</c>로 저장을 요청한다
/// (종료 때만 저장하면 비정상 종료 시 이번 세션의 변경이 사라진다).
/// </summary>
public sealed class FavoritesViewModel
{
    private readonly ILog _log;
    private readonly Func<string?> _currentPath;
    private readonly Action<string> _navigate;
    private readonly Services.IDialogService _dialogs;
    private readonly Action _persist;

    public ObservableCollection<FavoriteSlotViewModel> Slots { get; } = new();

    /// <param name="saved">저장되어 있던 즐겨찾기(앞에서부터 칸에 채운다).</param>
    /// <param name="currentPath">탐색기의 현재 경로(빈 칸 클릭 시 등록할 폴더).</param>
    /// <param name="navigate">등록된 폴더로 이동하는 동작.</param>
    /// <param name="persist">변경을 저장하는 동작.</param>
    public FavoritesViewModel(IReadOnlyList<FavoriteEntry> saved, Func<string?> currentPath, Action<string> navigate, Services.IDialogService dialogs, ILog log, Action persist)
    {
        _currentPath = currentPath;
        _navigate = navigate;
        _dialogs = dialogs;
        _log = log;
        _persist = persist;
        for (int i = 0; i < AppSettings.MaxFavorites; i++)
        {
            var slot = new FavoriteSlotViewModel(i + 1, OnClick, RegisterCurrentFolder, Clear, Rename, Move);
            if (i < saved.Count)
            {
                slot.Name = saved[i].Name;
                slot.Path = saved[i].Path;
            }
            Slots.Add(slot);
        }
    }

    /// <summary>현재 칸 내용을 저장용 목록으로 만든다.</summary>
    public List<FavoriteEntry> ToEntries() =>
        Slots.Select(f => new FavoriteEntry { Name = f.Name, Path = f.Path }).ToList();

    public void RefreshLanguage()
    {
        foreach (FavoriteSlotViewModel slot in Slots)
        {
            slot.RefreshLanguage();
        }
    }

    /// <summary>즐겨찾기 클릭: 등록된 칸이면 이동, 빈 칸이면 현재 폴더를 등록한다.</summary>
    private void OnClick(FavoriteSlotViewModel slot)
    {
        if (slot.IsEmpty)
        {
            RegisterCurrentFolder(slot);
        }
        else
        {
            _navigate(slot.Path);
        }
    }

    private void RegisterCurrentFolder(FavoriteSlotViewModel slot)
    {
        string? current = _currentPath();
        if (current is null)
        {
            return;
        }
        slot.Path = current;
        slot.Name = System.IO.Path.GetFileName(current) is { Length: > 0 } n ? n : current;
        _log.Info($"즐겨찾기 등록 #{slot.Index}: {current}");
        _persist();
    }

    /// <summary>즐겨찾기 표시 이름을 바꾼다(경로는 그대로). 같은 이름의 폴더를 구분할 때 쓴다.</summary>
    private void Rename(FavoriteSlotViewModel slot)
    {
        if (slot.IsEmpty)
        {
            return;
        }
        string? name = _dialogs.Prompt(Loc.T("즐겨찾기 이름 변경", "Rename favorite"), Loc.T("표시할 이름을 입력하세요.", "Enter the name to display."), slot.Name);
        if (name is not null)
        {
            slot.Name = name;
            _persist();
        }
    }

    /// <summary>칸의 내용(이름+경로)을 옆 칸과 맞바꿔 순서를 바꾼다. 끝 칸에서는 아무것도 하지 않는다.</summary>
    private void Move(FavoriteSlotViewModel slot, int delta)
    {
        int to = slot.Index - 1 + delta;
        if (to < 0 || to >= Slots.Count)
        {
            return;
        }
        FavoriteSlotViewModel other = Slots[to];
        (slot.Name, other.Name) = (other.Name, slot.Name);
        (slot.Path, other.Path) = (other.Path, slot.Path);
        _persist();
    }

    private void Clear(FavoriteSlotViewModel slot)
    {
        slot.Path = string.Empty;
        slot.Name = string.Empty;
        _log.Info($"즐겨찾기 삭제 #{slot.Index}");
        _persist();
    }

    /// <summary>즐겨찾기 10칸을 모두 비운다(확인 후).</summary>
    public void ResetAll()
    {
        if (!_dialogs.Confirm(Loc.T("즐겨찾기 초기화", "Reset favorites"), Loc.T("등록된 즐겨찾기를 모두 비웁니다. 계속할까요?", "This clears all registered favorites. Continue?")))
        {
            return;
        }
        foreach (FavoriteSlotViewModel slot in Slots)
        {
            slot.Path = string.Empty;
            slot.Name = string.Empty;
        }
        _log.Info("즐겨찾기 전체 초기화");
        _persist();
    }
}
