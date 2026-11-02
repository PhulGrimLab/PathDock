using System.Text.Json;
using PathDock.Core.Logging;
using PathDock.Core.Paths;

namespace PathDock.Core.Explorer;

/// <summary>캐시 조회 결과 상태.</summary>
public enum CacheFreshness
{
    /// <summary>캐시에 없음.</summary>
    Missing,
    /// <summary>유효기간 안이고 폴더 수정 시각도 같음 - 다시 계산하지 않아도 된다.</summary>
    Fresh,
    /// <summary>값은 있지만 오래되었거나 폴더가 바뀜 - 우선 보여주고 백그라운드에서 다시 계산한다.</summary>
    Stale,
}

/// <summary>
/// 폴더 크기 집계 결과 캐시(메모리 + JSON 파일). Owner: MainViewModel, Lifetime: 앱 전체.
/// 스레드: 모든 공개 메서드가 내부 lock으로 보호된다(임의 스레드 호출 가능).
/// 신선도 규칙: (현재 시각 - 계산 시각) &lt; TTL 이고 폴더 자신의 LastWriteTime이 저장 당시와 같으면 Fresh.
/// 폴더 자신의 수정 시각은 깊은 하위 변경을 반영하지 못하므로 TTL이 상한선 역할을 하고, 사용자가 새로 고침하면 강제 재계산한다.
/// </summary>
public sealed class FolderSizeCache
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(30);
    private const int MaxEntries = 5000;
    private const string FileName = "folder-size-cache.json";

    public sealed class Entry
    {
        public long TotalSize { get; set; }
        public int FileCount { get; set; }
        public int FolderCount { get; set; }
        public bool Incomplete { get; set; }
        public DateTime ComputedUtc { get; set; }
        public DateTime FolderWriteUtc { get; set; }
    }

    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _entries = new();
    private readonly string? _filePath;
    private readonly TimeSpan _ttl;
    private readonly TimeProvider _time;
    private readonly ILog _log;

    /// <param name="dataDir">저장 폴더(null이면 메모리 전용).</param>
    public FolderSizeCache(string? dataDir, ILog log, TimeSpan? ttl = null, TimeProvider? time = null)
    {
        _filePath = dataDir is null ? null : Path.Combine(dataDir, FileName);
        _log = log;
        _ttl = ttl ?? DefaultTtl;
        _time = time ?? TimeProvider.System;
    }

    public int Count
    {
        get { lock (_lock) { return _entries.Count; } }
    }

    private static string? KeyOf(string path)
    {
        return PathNormalizer.Normalize(path)?.ToUpperInvariant();
    }

    /// <summary>캐시를 조회한다. 값이 있으면(Fresh/Stale) <paramref name="stats"/>에 채운다.</summary>
    public CacheFreshness Lookup(string path, out FolderStats? stats)
    {
        stats = null;
        string? key = KeyOf(path);
        if (key is null)
        {
            return CacheFreshness.Missing;
        }
        Entry? e;
        lock (_lock)
        {
            if (!_entries.TryGetValue(key, out e))
            {
                return CacheFreshness.Missing;
            }
        }
        stats = new FolderStats(e.TotalSize, e.FileCount, e.FolderCount, e.Incomplete);
        bool young = _time.GetUtcNow().UtcDateTime - e.ComputedUtc < _ttl;
        bool unchanged = SafeWriteTime(path) == e.FolderWriteUtc;
        return young && unchanged ? CacheFreshness.Fresh : CacheFreshness.Stale;
    }

    /// <summary>계산 결과를 저장한다(기존 값은 덮어쓴다).</summary>
    public void Store(string path, FolderStats stats)
    {
        string? key = KeyOf(path);
        if (key is null)
        {
            return;
        }
        var e = new Entry
        {
            TotalSize = stats.TotalSize,
            FileCount = stats.FileCount,
            FolderCount = stats.FolderCount,
            Incomplete = stats.Incomplete,
            ComputedUtc = _time.GetUtcNow().UtcDateTime,
            FolderWriteUtc = SafeWriteTime(path),
        };
        lock (_lock)
        {
            _entries[key] = e;
            if (_entries.Count > MaxEntries * 2)
            {
                // 저장할 때만 자르면 오래 켜 둔 세션에서 메모리가 계속 늘어난다: 상한의 두 배를 넘으면 오래된 것부터 상한까지 줄인다.
                foreach (string old in _entries.OrderBy(kv => kv.Value.ComputedUtc).Take(_entries.Count - MaxEntries).Select(kv => kv.Key).ToList())
                {
                    _entries.Remove(old);
                }
            }
        }
    }

    /// <summary>해당 폴더와 그 아래 모든 폴더의 캐시를 지운다(파일 조작 후 무효화용).</summary>
    public void Invalidate(string path)
    {
        string? key = KeyOf(path);
        if (key is null)
        {
            return;
        }
        string prefix = key.TrimEnd('\\') + "\\";
        lock (_lock)
        {
            foreach (string k in _entries.Keys.Where(k => k == key || k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            {
                _entries.Remove(k);
            }
        }
    }

    /// <summary>
    /// 파일 조작 후 무효화: 해당 폴더와 하위 전체, 그리고 모든 상위 폴더의 항목을 지운다
    /// (상위 폴더의 총 크기에도 이 변경이 포함되어 있으므로).
    /// </summary>
    public void InvalidateWithAncestors(string path)
    {
        Invalidate(path);
        string? p = Path.GetDirectoryName(PathNormalizer.Normalize(path));
        lock (_lock)
        {
            while (!string.IsNullOrEmpty(p))
            {
                if (KeyOf(p) is { } k)
                {
                    _entries.Remove(k);
                }
                p = Path.GetDirectoryName(p);
            }
        }
    }

    private static DateTime SafeWriteTime(string path)
    {
        try
        {
            return Directory.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return DateTime.MinValue;
        }
    }

    /// <summary>파일에서 읽는다. 없거나 손상되면 빈 캐시로 시작한다(손상은 경고 로그).</summary>
    public void Load()
    {
        if (_filePath is null || !File.Exists(_filePath))
        {
            return;
        }
        try
        {
            var loaded = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(_filePath));
            if (loaded is null)
            {
                return;
            }
            lock (_lock)
            {
                _entries.Clear();
                // 손으로 편집되었거나 손상된 파일의 null 값은 조회 때 NullReferenceException이 되므로 버린다. 항목이 너무 많으면 최근 것만 남긴다.
                foreach (var kv in loaded.Where(kv => kv.Value is not null).OrderByDescending(kv => kv.Value.ComputedUtc).Take(MaxEntries))
                {
                    _entries[kv.Key] = kv.Value;
                }
            }
            _log.Info($"폴더 크기 캐시 로드: {loaded.Count}개");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _log.Warn($"폴더 크기 캐시 로드 실패, 비어 있는 캐시로 시작: {ex.Message}");
        }
    }

    /// <summary>파일에 저장한다. 항목이 너무 많으면 가장 오래된 것부터 버린다. 실패해도 예외를 던지지 않는다.</summary>
    public void Save()
    {
        if (_filePath is null)
        {
            return;
        }
        Dictionary<string, Entry> snapshot;
        lock (_lock)
        {
            snapshot = _entries
                .OrderByDescending(kv => kv.Value.ComputedUtc)
                .Take(MaxEntries)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }
        try
        {
            SafeIo.WriteAllTextAtomic(_filePath, JsonSerializer.Serialize(snapshot)); // 링크를 따라가지 않는 안전한 교체 저장
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"폴더 크기 캐시 저장 실패: {ex.Message}");
        }
    }
}
