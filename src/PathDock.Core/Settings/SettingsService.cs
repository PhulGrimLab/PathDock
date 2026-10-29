using System.Text.Json;
using System.Text.Json.Serialization;
using PathDock.Core.Logging;
using PathDock.Core.Paths;
using PathDock.Core.Sync;
using PathDock.Core.Terminal;

namespace PathDock.Core.Settings;

/// <summary>즐겨찾기 한 칸. Path가 비어 있으면 빈 칸이다.</summary>
public sealed class FavoriteEntry
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

/// <summary>폴더 탐색 영역의 배치. Quad=4개 폴더를 2열2행으로, Dual=상위 폴더 열+폴더를 한 묶음으로 2묶음, Single=한 경로를 상위 폴더 3개와 함께.</summary>
public enum ExplorerLayout
{
    Quad,
    Dual,
    Single,
}

/// <summary>settings.json에 저장되는 사용자 설정(단순 DTO).</summary>
public sealed class AppSettings
{
    /// <summary>즐겨찾기 최대 칸 수(기획서: 약 10개).</summary>
    public const int MaxFavorites = 10;

    /// <summary>폴더 영역 개수(4분할 배치의 칸 수).</summary>
    public const int PaneCount = 4;

    public const double DefaultQuadExplorerRatio = 0.78;

    public const double DefaultUiFontSize = 12;
    public const double MinUiFontSize = 10;
    public const double MaxUiFontSize = 20;
    public const double DefaultTerminalFontSize = 14;
    public const double MinTerminalFontSize = 10;
    public const double MaxTerminalFontSize = 28;
    public List<FavoriteEntry> Favorites { get; set; } = new();
    public SyncMode SyncMode { get; set; } = SyncMode.Bidirectional;
    public string LastPath { get; set; } = string.Empty;
    /// <summary>폴더 탐색 영역 배치(기본은 한 경로를 보여주는 Single).</summary>
    public ExplorerLayout Layout { get; set; } = ExplorerLayout.Single;
    /// <summary>폴더 영역 4개가 마지막으로 보던 경로(왼쪽 위, 오른쪽 위, 왼쪽 아래, 오른쪽 아래 순). 비어 있으면 저장된 적 없음.</summary>
    public List<string> PanePaths { get; set; } = new();
    /// <summary>마지막으로 선택(포커스)했던 폴더 영역의 번호(0~3).</summary>
    public int ActivePane { get; set; }
    /// <summary>저장된 창 위치(null이면 OS 기본 위치). NaN은 JSON으로 저장할 수 없어 nullable로 둔다.</summary>
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double WindowWidth { get; set; } = 1100;
    public double WindowHeight { get; set; } = 750;
    public bool WindowMaximized { get; set; }
    /// <summary>탐색기 영역 높이 비율(0~1, 나머지는 터미널).</summary>
    public double ExplorerRatio { get; set; } = 0.6;
    /// <summary>폴더 4분할 배치일 때의 탐색기 영역 높이 비율(폴더가 네 개라 터미널을 더 아래로 둔다).</summary>
    public double QuadExplorerRatio { get; set; } = DefaultQuadExplorerRatio;
    /// <summary>화면 테마("Dark" 또는 "Light").</summary>
    public string Theme { get; set; } = "Dark";
    /// <summary>화면(탐색기/즐겨찾기/상태 표시줄 등) 글자 크기.</summary>
    public double UiFontSize { get; set; } = DefaultUiFontSize;
    /// <summary>터미널 글자 크기.</summary>
    public double TerminalFontSize { get; set; } = DefaultTerminalFontSize;
    /// <summary>터미널 색 테마 키(TerminalThemes).</summary>
    public string TerminalTheme { get; set; } = TerminalColorScheme.DefaultKey;
    /// <summary>터미널 글자색 직접 지정(#RRGGBB, 비우면 테마 값).</summary>
    public string TerminalForeground { get; set; } = string.Empty;
    /// <summary>터미널 배경색 직접 지정(#RRGGBB, 비우면 테마 값).</summary>
    public string TerminalBackground { get; set; } = string.Empty;
    /// <summary>시작할 때 마지막으로 열었던 폴더에서 시작할지(false면 사용자 폴더).</summary>
    public bool StartAtLastPath { get; set; } = true;
    /// <summary>숨김/시스템 파일·폴더를 목록에 보여줄지.</summary>
    public bool ShowHiddenFiles { get; set; }
    /// <summary>터미널에서 실행할 셸(다음 실행부터 적용).</summary>
    public TerminalShell TerminalShell { get; set; } = TerminalShell.PowerShell;
    /// <summary>화면 언어 코드("ko" 또는 "en"). 비어 있으면 운영체제 표시 언어를 따른다.</summary>
    public string Language { get; set; } = string.Empty;
    /// <summary>터미널 패널을 접은 상태로 시작할지.</summary>
    public bool TerminalCollapsed { get; set; }
}

/// <summary>
/// 설정 JSON 로드/저장. Owner: 앱(MainViewModel), Lifetime: 앱 전체. 호출은 UI 스레드에서만 한다(내부 동기화 없음).
/// 저장은 임시 파일에 쓴 뒤 교체하여 중간 손상을 피한다.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;
    private readonly ILog _log;

    /// <summary>데이터 폴더: 환경 변수 PATHDOCK_DATA_DIR 우선, 없으면 %APPDATA%\PathDock.</summary>
    public static string ResolveDataDir()
    {
        string? overridden = Environment.GetEnvironmentVariable("PATHDOCK_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return overridden;
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PathDock");
    }

    public SettingsService(string dataDir, ILog log)
    {
        Directory.CreateDirectory(dataDir);
        _filePath = Path.Combine(dataDir, "settings.json");
        _log = log;
    }

    /// <summary>설정을 읽는다. 파일이 없거나 손상되었으면 기본값을 돌려준다(손상 시 경고 로그).</summary>
    public AppSettings Load()
    {
        if (!File.Exists(_filePath))
        {
            return new AppSettings();
        }
        try
        {
            return Sanitize(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath), JsonOptions) ?? new AppSettings());
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _log.Warn($"설정 로드 실패, 기본값 사용: {ex.Message}");
            return new AppSettings();
        }
    }

    /// <summary>
    /// 읽은 설정을 안전한 값으로 다듬는다. 파일이 손으로 편집되었거나 다른 프로그램이 바꿔 `"Favorites": null`, `[null]` 같은 값이 들어오면
    /// 시작 때 NullReferenceException으로 앱이 뜨지 못하고(파일을 지울 때까지 반복), 터무니없이 큰 창 크기도 그대로 쓰이므로,
    /// null은 기본값으로 바꾸고 숫자는 범위 안으로 맞춘다.
    /// </summary>
    public static AppSettings Sanitize(AppSettings s)
    {
        s.Favorites = (s.Favorites ?? new List<FavoriteEntry>())
            .Where(f => f is not null)
            .Select(f => new FavoriteEntry { Name = f.Name ?? string.Empty, Path = f.Path ?? string.Empty })
            .Take(AppSettings.MaxFavorites)
            .ToList();
        s.LastPath ??= string.Empty;
        s.PanePaths = (s.PanePaths ?? new List<string>()).Take(AppSettings.PaneCount).Select(p => p ?? string.Empty).ToList();
        s.ActivePane = Math.Clamp(s.ActivePane, 0, AppSettings.PaneCount - 1);
        if (!Enum.IsDefined(s.Layout))
        {
            s.Layout = ExplorerLayout.Single;
        }
        s.Theme ??= "Dark";
        s.TerminalTheme ??= TerminalColorScheme.DefaultKey;
        s.TerminalForeground ??= string.Empty;
        s.TerminalBackground ??= string.Empty;
        s.Language ??= string.Empty;
        s.WindowWidth = Within(s.WindowWidth, 200, 20000, 1100);
        s.WindowHeight = Within(s.WindowHeight, 150, 20000, 750);
        s.WindowLeft = s.WindowLeft is { } l && Math.Abs(l) <= 100000 ? l : null;
        s.WindowTop = s.WindowTop is { } t && Math.Abs(t) <= 100000 ? t : null;
        s.ExplorerRatio = Within(s.ExplorerRatio, 0, 1, 0.6);
        s.QuadExplorerRatio = Within(s.QuadExplorerRatio, 0, 1, AppSettings.DefaultQuadExplorerRatio);
        s.UiFontSize = Within(s.UiFontSize, AppSettings.MinUiFontSize, AppSettings.MaxUiFontSize, AppSettings.DefaultUiFontSize);
        s.TerminalFontSize = Within(s.TerminalFontSize, AppSettings.MinTerminalFontSize, AppSettings.MaxTerminalFontSize, AppSettings.DefaultTerminalFontSize);
        return s;
    }

    /// <summary>유한한 값을 [min, max] 안으로 맞춘다. NaN/무한대는 <paramref name="fallback"/>.</summary>
    private static double Within(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    /// <summary>설정을 저장한다. 실패해도 예외를 던지지 않고 로그만 남긴다(종료 흐름 보호).</summary>
    public void Save(AppSettings settings)
    {
        try
        {
            // 링크를 따라가지 않는 안전한 교체 저장(관리자 권한 앱이 사용자 쓰기 가능한 폴더에 쓰므로).
            SafeIo.WriteAllTextAtomic(_filePath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _log.Warn($"설정 저장 실패: {ex.Message}");
        }
    }
}
