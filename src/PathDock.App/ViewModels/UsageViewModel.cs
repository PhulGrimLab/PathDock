using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using PathDock.Core.Explorer;
using PathDock.Core.Localization;
using PathDock.Core.Logging;
using PathDock.Core.Monitoring;

namespace PathDock.App.ViewModels;

/// <summary>
/// 하단 상태 표시줄의 CPU/GPU/메모리 사용량(미터). Owner: MainViewModel, Lifetime: 앱 전체(Dispose에서 측정 종료).
/// 측정은 <see cref="SystemMonitor"/>의 백그라운드 타이머에서, 값 반영은 UI 스레드에서 한다.
/// 각 미터의 이름은 "CPU 12%"처럼 라벨+값 문구로 접근성에 노출된다.
/// </summary>
public sealed partial class UsageViewModel : ObservableObject, IDisposable
{
    private const int MonitorIntervalMs = 2000;

    private readonly SystemMonitor _monitor;

    [ObservableProperty] private double _cpuPercent;
    [ObservableProperty] private string _cpuText = "-";
    [ObservableProperty] private double _gpuPercent;
    [ObservableProperty] private string _gpuText = "-";
    [ObservableProperty] private bool _gpuAvailable = true;
    [ObservableProperty] private double _memoryPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MemoryAutomationName))]
    private string _memoryText = "-";

    public UsageViewModel(ILog log)
    {
        _monitor = new SystemMonitor(log, MonitorIntervalMs);
        _monitor.Updated += u => Application.Current?.Dispatcher.BeginInvoke(() => Apply(u));
    }

    /// <summary>메모리 미터의 접근성 이름. 예: "메모리 8.1 GB / 15.7 GB (52%)".</summary>
    public string MemoryAutomationName => $"{Loc.T("메모리", "Memory")} {MemoryText}";

    /// <summary>측정값을 미터 속성에 반영한다(UI 스레드).</summary>
    public void Apply(SystemUsage u)
    {
        CpuPercent = u.CpuPercent;
        CpuText = $"{u.CpuPercent:0}%";
        GpuAvailable = u.GpuPercent is not null;
        GpuPercent = u.GpuPercent ?? 0;
        GpuText = u.GpuPercent is { } g ? $"{g:0}%" : "N/A";
        MemoryPercent = u.MemoryPercent;
        MemoryText = $"{SizeFormatter.Format(u.MemoryUsedBytes)} / {SizeFormatter.Format(u.MemoryTotalBytes)} ({u.MemoryPercent:0}%)";
    }

    /// <summary>언어가 바뀌었을 때 코드에서 만든 문구(메모리 미터 이름)를 다시 읽게 한다.</summary>
    public void RefreshLanguage() => OnPropertyChanged(nameof(MemoryAutomationName));

    public void Dispose() => _monitor.Dispose();
}
