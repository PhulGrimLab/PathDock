using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PathDock.App.Services;
using PathDock.Core.Explorer;
using PathDock.Core.Localization;
using PathDock.Core.Logging;

namespace PathDock.App.ViewModels;

/// <summary>정리 창의 항목 한 줄(임시 폴더 묶음 / 휴지통 / 메모리). 크기는 검사가 끝난 뒤에 보인다.</summary>
public sealed partial class CleanupItemViewModel : ObservableObject
{
    private readonly string _korean;
    private readonly string _english;
    private readonly string _descriptionKo;
    private readonly string _descriptionEn;

    public CleanupItemViewModel(string id, string korean, string english, string descriptionKo, string descriptionEn, CleanupFolderSpec? spec = null)
    {
        Id = id;
        _korean = korean;
        _english = english;
        _descriptionKo = descriptionKo;
        _descriptionEn = descriptionEn;
        Spec = spec;
    }

    public string Id { get; }

    /// <summary>임시 폴더 묶음이면 그 정의, 휴지통/메모리면 null.</summary>
    public CleanupFolderSpec? Spec { get; }

    public string Name => Loc.T(_korean, _english);

    public string Description => Loc.T(_descriptionKo, _descriptionEn);

    [ObservableProperty]
    private bool _isSelected = true;

    /// <summary>지울 수 있는 크기(폴더/휴지통). 메모리 항목은 사용 중인 물리 메모리.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText))]
    private long _bytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText))]
    private bool _isScanned;

    public string SizeText => !IsScanned ? "..."
        : Id == CleanupViewModel.MemoryId ? Loc.T($"사용 중 {SizeFormatter.Format(Bytes)}", $"{SizeFormatter.Format(Bytes)} in use")
        : SizeFormatter.Format(Bytes);
}

/// <summary>
/// 정리 팝업의 ViewModel. Owner: MainViewModel(창을 열 때마다 새로 만듦), Lifetime: 창이 열려 있는 동안.
/// 열리면 자동으로 검사해 항목별 크기를 보여주고, 사용자가 고른 항목만 확인 후 정리한다.
/// 파일 작업은 모두 UI 스레드 밖에서 하고, 창을 닫으면 <see cref="Cancel"/>로 중단한다.
/// </summary>
public sealed partial class CleanupViewModel : ObservableObject
{
    public const string RecycleBinId = "RecycleBin";
    public const string MemoryId = "Memory";

    private readonly ILog _log;
    private readonly Services.IDialogService _dialogs;
    private readonly CancellationTokenSource _cts = new();

    public CleanupViewModel(ILog log, Services.IDialogService dialogs)
    {
        _log = log;
        _dialogs = dialogs;
        var items = CleanupService.FolderSpecs
            .Select(s => new CleanupItemViewModel(s.Id, s.Korean, s.English, s.DescriptionKo, s.DescriptionEn, s))
            .ToList();
        items.Add(new CleanupItemViewModel(RecycleBinId, "휴지통", "Recycle Bin",
            "모든 드라이브의 휴지통을 비움", "Empties the Recycle Bin of every drive"));
        items.Add(new CleanupItemViewModel(MemoryId, "메모리 정리", "Free memory",
            "다른 프로그램이 잡고 있는 여분의 메모리를 Windows에 돌려줌(효과는 일시적이며 열어 둔 프로그램은 그대로)",
            "Returns spare memory held by other programs to Windows (temporary; open programs stay as they are)"));
        Items = new ReadOnlyCollection<CleanupItemViewModel>(items);
        foreach (CleanupItemViewModel item in Items)
        {
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(CleanupItemViewModel.IsSelected) or nameof(CleanupItemViewModel.Bytes))
                {
                    OnPropertyChanged(nameof(SummaryText));
                    CleanCommand.NotifyCanExecuteChanged();
                }
            };
        }
    }

    public ReadOnlyCollection<CleanupItemViewModel> Items { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand), nameof(ScanCommand))]
    private bool _isBusy;

    /// <summary>진행 중인 일이나 마지막 결과를 알리는 한 줄.</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>고른 항목 중 저장소에서 확보되는 크기의 합(메모리 제외).</summary>
    private long SelectedBytes => Items.Where(i => i.IsSelected && i.Id != MemoryId).Sum(i => i.Bytes);

    public string SummaryText => Loc.T($"선택한 항목에서 약 {SizeFormatter.Format(SelectedBytes)}를 확보할 수 있습니다.",
        $"About {SizeFormatter.Format(SelectedBytes)} can be freed from the selected items.");

    /// <summary>창을 닫을 때 진행 중인 검사/정리를 멈춘다.</summary>
    public void Cancel() => _cts.Cancel();

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ScanAsync()
    {
        IsBusy = true;
        try
        {
            await ScanAllAsync();
            StatusText = string.Empty;
        }
        catch (OperationCanceledException)
        {
            StatusText = Loc.T("검사를 중단했습니다.", "Scan stopped.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ScanAllAsync()
    {
        foreach (CleanupItemViewModel item in Items)
        {
            StatusText = Loc.T($"검사 중: {item.Name}", $"Scanning: {item.Name}");
            item.Bytes = await Task.Run(() => ScanItem(item), _cts.Token);
            item.IsScanned = true;
        }
    }

    private long ScanItem(CleanupItemViewModel item)
    {
        if (item.Spec is { } spec)
        {
            return CleanupService.Scan(spec, _cts.Token).Bytes;
        }
        return item.Id == RecycleBinId ? RecycleBinService.Query()?.Bytes ?? 0 : CleanupService.QueryMemory()?.Used ?? 0;
    }

    private bool CanRun() => !IsBusy;

    private bool CanClean() => !IsBusy && Items.Any(i => i.IsSelected);

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        List<CleanupItemViewModel> selected = Items.Where(i => i.IsSelected).ToList();
        string names = string.Join(", ", selected.Select(i => i.Name));
        string message = Loc.T(
            $"다음 항목의 파일을 영구 삭제합니다(약 {SizeFormatter.Format(SelectedBytes)}). 되돌릴 수 없습니다.\n\n{names}\n\n계속할까요?",
            $"This permanently deletes the files of these items (about {SizeFormatter.Format(SelectedBytes)}). This cannot be undone.\n\n{names}\n\nContinue?");
        if (!_dialogs.Confirm(Loc.T("PC 정리", "Clean up PC"), message))
        {
            return;
        }
        IsBusy = true;
        try
        {
            long freed = 0;
            int skipped = 0;
            string memoryNote = string.Empty;
            // 메모리는 파일을 다 지운 뒤 마지막에 줄여, 사용 가능한 메모리가 얼마나 늘었는지 잰다.
            foreach (CleanupItemViewModel item in selected.OrderBy(i => i.Id == MemoryId))
            {
                StatusText = Loc.T($"정리 중: {item.Name}", $"Cleaning: {item.Name}");
                if (item.Id == MemoryId)
                {
                    memoryNote = await Task.Run(TrimMemory, _cts.Token);
                }
                else if (item.Spec is { } spec)
                {
                    CleanupTotals t = await Task.Run(() => CleanupService.Clean(spec, _cts.Token), _cts.Token);
                    freed += t.Bytes;
                    skipped += t.Skipped;
                }
                else
                {
                    freed += await Task.Run(EmptyRecycleBin, _cts.Token);
                }
            }
            await ScanAllAsync();
            string result = Loc.T($"정리했습니다: {SizeFormatter.Format(freed)} 확보.", $"Done: freed {SizeFormatter.Format(freed)}.");
            if (skipped > 0)
            {
                result += Loc.T($" 사용 중이어서 건너뛴 파일 {skipped:N0}개.", $" {skipped:N0} files in use were skipped.");
            }
            StatusText = result + memoryNote;
            _log.Info($"PC 정리: {SizeFormatter.Format(freed)} 확보, 건너뜀 {skipped}개, 항목 {string.Join(",", selected.Select(i => i.Id))}");
        }
        catch (OperationCanceledException)
        {
            StatusText = Loc.T("정리를 중단했습니다. 일부 항목은 이미 지워졌을 수 있습니다.", "Cleanup was stopped. Some items may already have been deleted.");
            _log.Info("PC 정리 중단");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private long EmptyRecycleBin()
    {
        long before = RecycleBinService.Query()?.Bytes ?? 0;
        try
        {
            RecycleBinService.Empty();
            return before;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or System.ComponentModel.Win32Exception)
        {
            _log.Warn($"휴지통 비우기 실패: {ex.Message}");
            return 0;
        }
    }

    private string TrimMemory()
    {
        long usedBefore = CleanupService.QueryMemory()?.Used ?? 0;
        int count = CleanupService.TrimMemory(_cts.Token);
        long gained = Math.Max(0, usedBefore - (CleanupService.QueryMemory()?.Used ?? usedBefore));
        return Loc.T($" 메모리: 프로세스 {count:N0}개에서 {SizeFormatter.Format(gained)} 확보.", $" Memory: freed {SizeFormatter.Format(gained)} from {count:N0} processes.");
    }
}
