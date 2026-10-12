using System.Threading.Channels;

namespace PathDock.Core.Logging;

/// <summary>로그 기록 인터페이스. 호출 측은 파일 I/O를 기다리지 않는다.</summary>
public interface ILog
{
    void Info(string message);
    void Warn(string message);
    void Error(string message);
}

/// <summary>아무것도 기록하지 않는 로거(테스트 기본값).</summary>
public sealed class NullLog : ILog
{
    public static readonly NullLog Instance = new();
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message) { }
}

/// <summary>
/// 비동기 파일 로거. Owner: 앱, Lifetime: 앱 전체(Dispose에서 큐를 비우고 종료).
/// 공유자원: Channel(스레드 안전) - 여러 스레드가 쓰고 전용 Writer 스레드 1개가 파일에 쓴다.
/// 큐가 가득 차면 가장 오래된 항목을 버려 호출 측이 절대 대기하지 않는다.
/// </summary>
public sealed class AsyncFileLogger : ILog, IDisposable
{
    private const int QueueCapacity = 2048;
    private const int FlushWaitMs = 2000;

    private readonly Channel<string> _queue = Channel.CreateBounded<string>(
        new BoundedChannelOptions(QueueCapacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly Thread _writer;
    private readonly string _dir;

    public AsyncFileLogger(string dataDir)
    {
        _dir = Path.Combine(dataDir, "logs");
        Directory.CreateDirectory(_dir);
        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "PathDock.LogWriter" };
        _writer.Start();
    }

    public void Info(string message) => Enqueue("INFO ", message);
    public void Warn(string message) => Enqueue("WARN ", message);
    public void Error(string message) => Enqueue("ERROR", message);

    private void Enqueue(string level, string message)
    {
        _queue.Writer.TryWrite($"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}");
    }

    /// <summary>Writer 스레드 본체: 큐가 완료될 때까지 날짜별 파일에 한 줄씩 기록한다.</summary>
    private void WriteLoop()
    {
        StreamWriter? writer = null;
        string? currentFile = null;
        try
        {
            foreach (string line in _queue.Reader.ReadAllAsync().ToBlockingEnumerable())
            {
                string file = Path.Combine(_dir, $"pathdock-{DateTime.Now:yyyyMMdd}.log");
                if (file != currentFile)
                {
                    writer?.Dispose();
                    writer = new StreamWriter(new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
                    currentFile = file;
                }
                writer!.WriteLine(line);
            }
        }
        catch (IOException)
        {
            // 로그 파일을 열 수 없으면 조용히 포기한다(로깅이 앱 동작을 막으면 안 됨).
        }
        finally
        {
            writer?.Dispose();
        }
    }

    public void Dispose()
    {
        _queue.Writer.TryComplete();
        _writer.Join(FlushWaitMs);
    }
}
