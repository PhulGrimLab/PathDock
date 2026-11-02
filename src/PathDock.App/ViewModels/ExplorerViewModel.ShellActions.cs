using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using PathDock.App.Services;
using PathDock.Core.Localization;

namespace PathDock.App.ViewModels;

/// <summary>
/// ExplorerViewModel의 셸 연동 동작(우클릭 메뉴): 경로 복사, 연결 프로그램 선택, 탐색기에서 보기, 속성.
/// 같은 클래스의 partial이라 선택 상태(<see cref="Targets"/>)를 공유한다. 모든 멤버는 UI 스레드 전용.
/// </summary>
public sealed partial class ExplorerViewModel
{
    /// <summary>성공을 알리는 문구(알림 막대에 초록으로 표시).</summary>
    public event Action<string>? Notified;

    /// <summary>선택한 항목의 전체 경로를 클립보드에 복사한다(여러 개면 줄바꿈으로 구분). 선택이 없으면 현재 폴더 경로.</summary>
    [RelayCommand]
    private void CopyPath()
    {
        List<string> paths = Targets().Select(t => t.FullPath).ToList();
        if (paths.Count == 0 && _service.Current is { } current)
        {
            paths.Add(current);
        }
        if (paths.Count == 0)
        {
            return;
        }
        if (ClipboardText.TryCopy(string.Join(Environment.NewLine, paths)))
        {
            Notified?.Invoke(paths.Count == 1
                ? Loc.T("경로를 복사했습니다", "Path copied")
                : Loc.T($"경로 {paths.Count}개를 복사했습니다", $"{paths.Count} paths copied"));
        }
        else
        {
            ErrorOccurred?.Invoke(Loc.T("경로를 복사하지 못했습니다(클립보드를 다른 프로그램이 사용 중입니다)", "Could not copy the path (the clipboard is in use by another program)"));
        }
    }

    /// <summary>터미널 입력줄에 넣을 경로 글자를 요청한다(MainViewModel이 활성 터미널로 전달). 인수는 선택한 전체 경로들.</summary>
    public event Action<IReadOnlyList<string>>? SendPathsToTerminalRequested;

    /// <summary>선택한 항목(없으면 현재 폴더)의 경로를 터미널 입력줄에 넣는다. Enter는 누르지 않으므로 명령이 바로 실행되지 않는다.</summary>
    [RelayCommand]
    private void SendPathsToTerminal()
    {
        List<string> paths = Targets().Select(t => t.FullPath).ToList();
        if (paths.Count == 0 && _service.Current is { } current)
        {
            paths.Add(current);
        }
        if (paths.Count > 0)
        {
            SendPathsToTerminalRequested?.Invoke(paths);
        }
    }

    /// <summary>탐색기(Windows 파일 탐색기)에서 선택한 항목을 선택한 상태로 보여 준다. 선택이 없으면 현재 폴더를 연다.</summary>
    [RelayCommand]
    private void ShowInExplorer()
    {
        string? path = Targets().FirstOrDefault()?.FullPath ?? _service.Current;
        if (path is null)
        {
            return;
        }
        // 전체 경로로 실행한다: 이름만 주면 앱 폴더에서 같은 이름의 파일을 먼저 찾을 수 있다(이 앱은 관리자 권한).
        string explorerExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        bool select = Targets().Count > 0;
        string arguments = select ? $"/select,\"{path}\"" : $"\"{path}\"";
        RunShell(() => Process.Start(new ProcessStartInfo(explorerExe, arguments) { UseShellExecute = false })?.Dispose(), path);
    }

    /// <summary>"연결 프로그램 선택" 창을 연다(파일만). 관리자 권한 앱이라 선택한 프로그램도 관리자로 뜨지 않도록 탐색기(일반 권한)에 맡긴다.</summary>
    [RelayCommand]
    private void OpenWith()
    {
        FileItemViewModel? item = Targets().FirstOrDefault(t => !t.IsDirectory);
        if (item is null)
        {
            return;
        }
        string folder = Path.GetDirectoryName(item.FullPath) ?? string.Empty;
        if (UnelevatedLauncher.IsElevated() && UnelevatedLauncher.TryOpen(item.FullPath, folder, "openas"))
        {
            return;
        }
        RunShell(() => Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true, Verb = "openas", WorkingDirectory = folder })?.Dispose(), item.FullPath);
    }

    /// <summary>선택한 항목(없으면 현재 폴더)의 속성 창을 연다.</summary>
    [RelayCommand]
    private void ShowProperties()
    {
        string? path = Targets().FirstOrDefault()?.FullPath ?? _service.Current;
        if (path is null)
        {
            return;
        }
        RunShell(() => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "properties" })?.Dispose(), path);
    }

    /// <summary>셸 동작을 실행하고, 실패(사용자 취소 제외)는 알림으로 알린다.</summary>
    private void RunShell(Action action, string path)
    {
        try
        {
            action();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            // 사용자가 취소함: 오류가 아니다.
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            _log.Warn($"셸 동작 실패: {path} ({ex.Message})");
            ErrorOccurred?.Invoke(Loc.T($"실행할 수 없습니다: {Path.GetFileName(path)} ({ex.Message})", $"Could not run it: {Path.GetFileName(path)} ({ex.Message})"));
        }
    }
}
