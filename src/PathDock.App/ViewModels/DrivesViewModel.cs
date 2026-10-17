using System.Collections.ObjectModel;
using System.IO;
using PathDock.Core.Explorer;
using PathDock.Core.Logging;

namespace PathDock.App.ViewModels;

/// <summary>
/// 드라이브 패널: 드라이브 목록과 사용량, 현재 드라이브 강조, 클릭하면 그 드라이브 루트로 이동.
/// Owner: MainViewModel, Lifetime: 앱 전체. UI 스레드 전용.
/// </summary>
public sealed class DrivesViewModel
{
    private readonly ILog _log;
    private readonly Action<string> _navigate;
    private readonly Func<string?> _currentPath;
    private bool _refreshing;

    public ObservableCollection<DriveItemViewModel> Items { get; } = new();

    /// <param name="navigate">드라이브 루트로 이동하는 동작(탐색기에 연결).</param>
    /// <param name="currentPath">탐색기의 현재 경로(현재 드라이브 강조에 쓴다).</param>
    public DrivesViewModel(ILog log, Action<string> navigate, Func<string?> currentPath)
    {
        _log = log;
        _navigate = navigate;
        _currentPath = currentPath;
    }

    /// <summary>
    /// 드라이브 목록과 용량을 백그라운드에서 읽어 갱신한다(네트워크 드라이브가 UI를 막지 않도록).
    /// 이미 갱신 중이면 건너뛴다. 결과 적용은 UI 스레드(await 복귀 지점)에서 한다.
    /// </summary>
    public async Task RefreshAsync()
    {
        if (_refreshing)
        {
            return; // 진행 중인 갱신이 최신 용량을 가져오므로 중복 실행하지 않는다.
        }
        _refreshing = true;
        try
        {
            List<DriveEntry> entries = await Task.Run(DriveService.List);
            Items.Clear();
            foreach (DriveEntry e in entries)
            {
                Items.Add(new DriveItemViewModel(e, drive => _navigate(drive.Root)));
            }
            MarkCurrent(_currentPath());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"드라이브 목록 갱신 실패: {ex.Message}");
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>현재 경로가 속한 드라이브만 강조한다.</summary>
    public void MarkCurrent(string? path)
    {
        string? root = DriveService.RootOf(path);
        foreach (DriveItemViewModel d in Items)
        {
            d.IsCurrent = root is not null && string.Equals(d.Root, root, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void RefreshLanguage()
    {
        foreach (DriveItemViewModel drive in Items)
        {
            drive.RefreshLanguage();
        }
    }
}
