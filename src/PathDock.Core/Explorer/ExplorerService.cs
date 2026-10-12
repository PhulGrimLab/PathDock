using PathDock.Core.Paths;

namespace PathDock.Core.Explorer;

/// <summary>목록 한 줄(폴더 또는 파일). 단순 DTO.</summary>
public sealed record FileItem(string Name, string FullPath, bool IsDirectory, long Size, DateTime Modified);

/// <summary>
/// 폴더 열거와 이동 이력(뒤로/앞으로) 관리. Owner: ExplorerViewModel, Lifetime: 앱 전체. UI 스레드 전용(동기화 없음).
/// 열거 자체는 순수 함수(<see cref="List"/>)로 분리해 테스트하기 쉽게 했다.
/// </summary>
public sealed class ExplorerService
{
    private readonly List<string> _history = new();
    private int _index = -1;

    /// <summary>현재 경로(정규화됨). 아직 이동 전이면 null.</summary>
    public string? Current => _index >= 0 ? _history[_index] : null;
    public bool CanGoBack => _index > 0;
    public bool CanGoForward => _index >= 0 && _index < _history.Count - 1;

    /// <summary>
    /// 폴더 내용을 열거한다(폴더 먼저, 이름순). 숨김/시스템 항목은 제외.
    /// 폴더가 없으면 DirectoryNotFoundException, 권한이 없으면 UnauthorizedAccessException을 그대로 던진다.
    /// </summary>
    public static List<FileItem> List(string path)
    {
        var dir = new DirectoryInfo(path);
        if (!dir.Exists)
        {
            throw new DirectoryNotFoundException(path);
        }

        var options = new EnumerationOptions
        {
            // false: 폴더 자체에 접근 권한이 없으면 UnauthorizedAccessException으로 알린다(비재귀 열거라 개별 항목에는 영향 없음).
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };
        var result = new List<FileItem>();
        foreach (FileSystemInfo e in dir.EnumerateFileSystemInfos("*", options))
        {
            bool isDir = e is DirectoryInfo;
            long size = e is FileInfo f ? f.Length : 0;
            result.Add(new FileItem(e.Name, e.FullName, isDir, size, e.LastWriteTime));
        }
        return result
            .OrderByDescending(i => i.IsDirectory)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 새 경로로 이동하고 목록을 반환한다. 성공 시에만 이력이 갱신된다(실패하면 현재 위치 유지).
    /// 같은 경로 재이동은 이력을 늘리지 않는다. 실패 시 예외를 그대로 전파한다.
    /// </summary>
    public List<FileItem> NavigateTo(string path)
    {
        string? normalized = PathNormalizer.Normalize(path) ?? throw new ArgumentException("잘못된 경로", nameof(path));
        List<FileItem> items = List(normalized);

        if (!PathNormalizer.AreSame(Current, normalized))
        {
            // 앞으로 이력은 새 이동으로 폐기한다(브라우저와 동일).
            if (_index < _history.Count - 1)
            {
                _history.RemoveRange(_index + 1, _history.Count - _index - 1);
            }
            _history.Add(normalized);
            _index = _history.Count - 1;
        }
        else
        {
            // 같은 경로: 이력은 그대로 두고 목록만 새로 고친다.
        }
        return items;
    }

    /// <summary>이력상 뒤로 이동한 경로를 반환(없으면 null). 목록은 호출 측이 NavigateTo 없이 <see cref="Refresh"/>로 읽는다.</summary>
    public string? GoBack()
    {
        if (!CanGoBack)
        {
            return null;
        }
        _index--;
        return _history[_index];
    }

    public string? GoForward()
    {
        if (!CanGoForward)
        {
            return null;
        }
        _index++;
        return _history[_index];
    }

    /// <summary>현재 경로의 목록을 다시 읽는다.</summary>
    public List<FileItem> Refresh()
    {
        return Current is null ? new List<FileItem>() : List(Current);
    }

    /// <summary>
    /// 하위 폴더만 이름순으로 나열한다(상위 단계 열 표시용). 숨김/시스템 제외.
    /// 접근 불가/없음이면 빈 목록(상위 열은 보조 정보라 오류로 알리지 않는다).
    /// </summary>
    public static List<FileItem> ListDirectories(string path)
    {
        try
        {
            var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
            return new DirectoryInfo(path).EnumerateDirectories("*", options)
                .Select(d => new FileItem(d.Name, d.FullName, true, 0, d.LastWriteTime))
                .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            return new List<FileItem>();
        }
    }

    /// <summary>상위 폴더 경로(루트면 null).</summary>
    public static string? GetParent(string path)
    {
        return Directory.GetParent(path)?.FullName;
    }
}
