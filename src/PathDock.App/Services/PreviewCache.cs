using System.IO;

namespace PathDock.App.Services;

/// <summary>
/// 파일 미리보기용 작은 캐시. 같은 파일(경로·수정 시각·크기가 같음)은 최근 <c>capacity</c>개까지 다시 읽지 않는다.
/// UI 스레드 전용(동기화 없음).
/// </summary>
public sealed class PreviewCache<T> where T : class
{
    private readonly Dictionary<string, T> _items = new();
    private readonly Queue<string> _order = new();
    private readonly int _capacity;

    public PreviewCache(int capacity)
    {
        _capacity = capacity;
    }

    /// <summary>경로·수정 시각·크기로 만든 캐시 키. 파일이 없거나 읽을 수 없거나 <paramref name="maxBytes"/>보다 크면 null.</summary>
    public static string? KeyOf(string path, long maxBytes = long.MaxValue)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length <= maxBytes ? $"{path}|{info.LastWriteTimeUtc.Ticks}|{info.Length}" : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    public bool TryGet(string key, out T? value) => _items.TryGetValue(key, out value);

    public void Add(string key, T value)
    {
        if (_items.TryAdd(key, value))
        {
            _order.Enqueue(key);
            while (_order.Count > _capacity)
            {
                _items.Remove(_order.Dequeue());
            }
        }
    }
}
