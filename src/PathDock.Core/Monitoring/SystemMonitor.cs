using System.Diagnostics;
using System.Runtime.InteropServices;
using PathDock.Core.Logging;

namespace PathDock.Core.Monitoring;

/// <summary>시스템 사용량 한 번의 측정값. Gpu가 null이면 GPU 카운터를 쓸 수 없는 환경(드라이버/OS 미지원)이다.</summary>
public sealed record SystemUsage(double CpuPercent, double MemoryPercent, long MemoryUsedBytes, long MemoryTotalBytes, double? GpuPercent);

/// <summary>
/// CPU/메모리/GPU 사용률을 주기적으로 측정한다. Owner: MainViewModel, Lifetime: 앱 전체(Dispose에서 타이머 종료).
/// 스레드: 전용 Timer 스레드(스레드 풀)에서 <see cref="Sample"/>을 호출하고 <see cref="Updated"/>를 발생시킨다 -
/// 구독자가 UI 스레드로 옮겨야 한다. Sample은 내부 lock으로 직렬화되어 타이머와 수동 호출이 겹쳐도 안전하다.
/// 방식: CPU=GetSystemTimes 증분, 메모리=GlobalMemoryStatusEx, GPU=성능 카운터 "GPU Engine"의
/// Utilization Percentage를 엔진 종류별로 합산한 값 중 최댓값(작업 관리자와 같은 방식).
/// </summary>
public sealed class SystemMonitor : IDisposable
{
    private const int FirstDelayMs = 1000;
    private const int GpuCategoryReadErrorLimit = 3;
    private const string GpuCategory = "GPU Engine";
    private const string GpuCounter = "Utilization Percentage";

    private readonly object _lock = new();
    private readonly ManualResetEventSlim _gpuReady = new(false);
    private readonly ILog _log;
    private readonly Timer? _timer;
    private ulong _prevIdle, _prevKernel, _prevUser;
    private PerformanceCounterCategory? _gpuCategory;
    private Dictionary<string, CounterSample> _gpuPrev = new();
    private bool _gpuAvailable;
    private int _gpuErrors;
    private bool _disposed;

    /// <summary>새 측정값(Timer 스레드에서 발생).</summary>
    public event Action<SystemUsage>? Updated;

    /// <param name="intervalMs">측정 주기(0이면 타이머 없이 수동 <see cref="Sample"/>만 사용).</param>
    public SystemMonitor(ILog log, int intervalMs)
    {
        _log = log;
        ReadCpuTimes(out _prevIdle, out _prevKernel, out _prevUser);
        // GPU 카운터의 첫 읽기는 수 초 걸리므로 시작을 막지 않게 백그라운드에서 한다. Sample은 끝날 때까지 기다린다(첫 값에만 영향).
        _ = Task.Run(() =>
        {
            try
            {
                InitGpu();
            }
            finally
            {
                _gpuReady.Set();
            }
        });
        if (intervalMs > 0)
        {
            _timer = new Timer(_ => Tick(), null, FirstDelayMs, intervalMs);
        }
    }

    private void InitGpu()
    {
        try
        {
            if (PerformanceCounterCategory.Exists(GpuCategory))
            {
                _gpuCategory = new PerformanceCounterCategory(GpuCategory);
                _gpuAvailable = true;
                ReadGpuTotals(); // 증분 계산의 기준 샘플
            }
            else
            {
                _log.Info("GPU 카운터 없음: GPU 사용률은 표시하지 않음");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _gpuAvailable = false;
            _log.Warn($"GPU 카운터 초기화 실패: {ex.Message}");
        }
    }

    private void Tick()
    {
        if (_disposed)
        {
            return;
        }
        try
        {
            Updated?.Invoke(Sample());
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // 종료 중 경쟁: 무시.
            _log.Warn($"시스템 사용량 측정 실패: {ex.Message}");
        }
    }

    /// <summary>직전 측정 이후의 사용률을 계산해 반환한다(첫 호출은 생성 시점부터의 증분).</summary>
    public SystemUsage Sample()
    {
        _gpuReady.Wait();
        lock (_lock)
        {
            double cpu = SampleCpu();
            (double mem, long used, long total) = SampleMemory();
            double? gpu = SampleGpu();
            return new SystemUsage(cpu, mem, used, total, gpu);
        }
    }

    private double SampleCpu()
    {
        ReadCpuTimes(out ulong idle, out ulong kernel, out ulong user);
        ulong dIdle = idle - _prevIdle, dKernel = kernel - _prevKernel, dUser = user - _prevUser;
        _prevIdle = idle;
        _prevKernel = kernel;
        _prevUser = user;
        ulong total = dKernel + dUser; // kernel 시간에 idle이 포함되어 있다.
        if (total == 0)
        {
            return 0;
        }
        return Math.Clamp((1.0 - (double)dIdle / total) * 100.0, 0, 100);
    }

    private static (double Percent, long Used, long Total) SampleMemory()
    {
        var m = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref m) || m.ullTotalPhys == 0)
        {
            return (0, 0, 0);
        }
        long total = (long)m.ullTotalPhys;
        long used = total - (long)m.ullAvailPhys;
        return (Math.Clamp(m.dwMemoryLoad, 0, 100), used, total);
    }

    private double? SampleGpu()
    {
        if (!_gpuAvailable)
        {
            return null;
        }
        try
        {
            double v = ReadGpuTotals();
            _gpuErrors = 0;
            return v;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // 프로세스가 사라지며 인스턴스가 바뀌는 순간 등 일시 오류. 반복되면 포기하고 N/A로 표시한다.
            if (++_gpuErrors >= GpuCategoryReadErrorLimit)
            {
                _gpuAvailable = false;
                _log.Warn($"GPU 사용률 측정 중단: {ex.Message}");
            }
            return null;
        }
    }

    /// <summary>엔진 종류(3D, VideoDecode, Copy 등)별로 모든 프로세스의 사용률을 합산하고 그중 최댓값(0~100)을 돌려준다.</summary>
    private double ReadGpuTotals()
    {
        var data = _gpuCategory!.ReadCategory();
        if (!data.Contains(GpuCounter))
        {
            throw new InvalidOperationException("GPU 카운터 항목 없음");
        }
        var current = new Dictionary<string, CounterSample>();
        var perType = new Dictionary<string, double>();
        foreach (InstanceData inst in data[GpuCounter].Values)
        {
            current[inst.InstanceName] = inst.Sample;
            if (_gpuPrev.TryGetValue(inst.InstanceName, out CounterSample prev))
            {
                string type = EngineType(inst.InstanceName);
                perType[type] = perType.GetValueOrDefault(type) + CounterSample.Calculate(prev, inst.Sample);
            }
            else
            {
                // 새로 나타난 인스턴스: 다음 측정부터 증분 계산.
            }
        }
        _gpuPrev = current;
        return perType.Count == 0 ? 0 : Math.Clamp(perType.Values.Max(), 0, 100);
    }

    /// <summary>인스턴스 이름 "pid_12_luid_0x0_0x1_phys_0_eng_0_engtype_3D"에서 엔진 종류("3D")를 뽑는다. 없으면 "unknown".</summary>
    public static string EngineType(string instanceName)
    {
        const string Marker = "engtype_";
        int i = instanceName.LastIndexOf(Marker, StringComparison.OrdinalIgnoreCase);
        return i < 0 ? "unknown" : instanceName[(i + Marker.Length)..];
    }

    public void Dispose()
    {
        _disposed = true;
        _timer?.Dispose();
    }

    #region Win32

    private static void ReadCpuTimes(out ulong idle, out ulong kernel, out ulong user)
    {
        GetSystemTimes(out FileTime i, out FileTime k, out FileTime u);
        idle = i.ToUInt64();
        kernel = k.ToUInt64();
        user = u.ToUInt64();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low, High;
        public readonly ulong ToUInt64() => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    #endregion
}
