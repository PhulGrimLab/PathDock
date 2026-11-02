using PathDock.Core.Logging;
using PathDock.Core.Paths;

namespace PathDock.Core.Sync;

/// <summary>경로 동기화 모드(기획서 3절).</summary>
public enum SyncMode
{
    Bidirectional,
    ExplorerToTerminal,
    TerminalToExplorer,
    Off,
}

/// <summary>경로 변경의 출처. User=사용자의 직접 조작, Sync=동기화로 인한 변경(재전파 금지).</summary>
public enum ChangeOrigin
{
    User,
    Sync,
}

/// <summary>동기화 대상(탐색기/터미널) 쪽이 구현하는 인터페이스. 실제 구현은 ViewModel이 제공한다.</summary>
public interface IPathSyncTarget
{
    /// <summary>현재 경로(정규화 여부 무관). 아직 모르면 null.</summary>
    string? CurrentPath { get; }

    /// <summary>
    /// 경로 변경을 요청한다. 전달 가능하면 true, 대기 정책으로 보류(예: 터미널 Busy)했다면 false를 반환한다.
    /// 호출은 항상 UI 스레드에서 이루어진다.
    /// </summary>
    bool RequestChange(string path);
}

/// <summary>동기화 중 사용자에게 알려야 하는 경고(경로 없음 등).</summary>
public sealed record SyncWarning(string Message);

/// <summary>
/// 탐색기와 터미널 사이의 경로 변경을 중재한다(기획서 3절 규칙). Owner: MainViewModel, Lifetime: 앱 전체.
/// 모든 메서드는 UI 스레드에서만 호출한다(내부 동기화 없음). 터미널 읽기 스레드의 이벤트는 호출 전에 UI 스레드로 옮겨야 한다.
/// </summary>
public sealed class PathSyncService
{
    private static readonly TimeSpan UncCheckTimeout = TimeSpan.FromSeconds(3);

    private readonly IPathSyncTarget _explorer;
    private readonly IPathSyncTarget _terminal;
    private readonly ILog _log;

    public SyncMode Mode { get; set; } = SyncMode.Bidirectional;

    /// <summary>경고 발생 이벤트(UI는 InfoBar로 표시).</summary>
    public event Action<SyncWarning>? Warning;

    public PathSyncService(IPathSyncTarget explorer, IPathSyncTarget terminal, ILog log)
    {
        _explorer = explorer;
        _terminal = terminal;
        _log = log;
    }

    /// <summary>탐색기 경로가 바뀌었음을 알린다. 모드가 허용하면 터미널로 전파한다.</summary>
    public void OnExplorerNavigated(string path, ChangeOrigin origin)
    {
        Propagate("Explorer->Terminal", path, origin, Mode is SyncMode.Bidirectional or SyncMode.ExplorerToTerminal, _terminal);
    }

    /// <summary>터미널 cwd가 바뀌었음을 알린다. 모드가 허용하면 탐색기로 전파한다.</summary>
    public void OnTerminalCwdChanged(string path, ChangeOrigin origin)
    {
        Propagate("Terminal->Explorer", path, origin, Mode is SyncMode.Bidirectional or SyncMode.TerminalToExplorer, _explorer);
    }

    /// <summary>규칙 2~7(doc/02 5절)을 순서대로 적용하고 판정 결과를 로그에 남긴다.</summary>
    private void Propagate(string direction, string path, ChangeOrigin origin, bool modeAllows, IPathSyncTarget target)
    {
        if (origin == ChangeOrigin.Sync)
        {
            _log.Info($"sync {direction} 무시: 동기화로 발생한 변경 ({path})");
            return;
        }
        if (!modeAllows)
        {
            _log.Info($"sync {direction} 무시: 모드 {Mode} ({path})");
            return;
        }

        string? normalized = PathNormalizer.Normalize(path);
        // 네트워크(UNC) 경로는 응답 없는 서버를 기다리는 동안 UI 스레드가 멈추므로 시간 제한을 둔다(그 안에 답이 없으면 "찾을 수 없음"으로 처리).
        bool exists = normalized is not null && (SafeIo.IsNetworkPath(normalized)
            ? SafeIo.DirectoryExistsWithTimeout(normalized, UncCheckTimeout)
            : Directory.Exists(normalized));
        if (normalized is null || !exists)
        {
            _log.Warn($"sync {direction} 실패: 경로 없음 ({path})");
            Warning?.Invoke(new SyncWarning(Localization.Loc.T($"폴더를 찾을 수 없어 동기화하지 않았습니다: {path}", $"Not synced because the folder could not be found: {path}")));
            return;
        }
        if (PathNormalizer.AreSame(target.CurrentPath, normalized))
        {
            _log.Info($"sync {direction} 무시: 이미 같은 경로 ({normalized})");
            return;
        }

        bool delivered = target.RequestChange(normalized);
        _log.Info($"sync {direction} {(delivered ? "전달" : "보류")}: {normalized}");
    }
}
