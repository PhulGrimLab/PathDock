using System.Text.Json;
using System.Text.Json.Serialization;
using PathDock.Core.Logging;
using PathDock.Core.Sync;
using PathDock.Core.Terminal;

namespace PathDock.Core.Settings;

/// <summary>즐겨찾기 한 칸. Path가 비어 있으면 빈 칸이다.</summary>
public sealed class FavoriteEntry
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

/// <summary>settings.json에 저장되는 사용자 설정(단순 DTO).</summary>
public sealed class AppSettings
{
    /// <summary>즐겨찾기 최대 칸 수(기획서: 약 10개).</summary>
    public const int MaxFavorites = 10;

    public const double DefaultUiFontSize = 12;
    public const double MinUiFontSize = 10;
    public const double MaxUiFontSize = 20;
    public const double DefaultTerminalFontSize = 14;
    public const double MinTerminalFontSize = 10;
    public const double MaxTerminalFontSize = 28;
    public List<FavoriteEntry> Favorites { get; set; } = new();
    public SyncMode SyncMode { get; set; } = SyncMode.Bidirectional;
    public string LastPath { get; set; } = string.Empty;
    /// <summary>저장된 창 위치(null이면 OS 기본 위치). NaN은 JSON으로 저장할 수 없어 nullable로 둔다.</summary>
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double WindowWidth { get; set; } = 1100;
    public double WindowHeight { get; set; } = 750;
    public bool WindowMaximized { get; set; }
    /// <summary>탐색기 영역 높이 비율(0~1, 나머지는 터미널).</summary>
    public double ExplorerRatio { get; set; } = 0.5;
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
    /// <summary>터미널에서 실행할 셸(다음 실행부터 적용).</summary>
    public TerminalShell TerminalShell { get; set; } = TerminalShell.PowerShell;
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
            AppSettings s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath), JsonOptions) ?? new AppSettings();
            if (s.Favorites.Count > AppSettings.MaxFavorites)
            {
                s.Favorites = s.Favorites.Take(AppSettings.MaxFavorites).ToList();
            }
            return s;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _log.Warn($"설정 로드 실패, 기본값 사용: {ex.Message}");
            return new AppSettings();
        }
    }

    /// <summary>설정을 저장한다. 실패해도 예외를 던지지 않고 로그만 남긴다(종료 흐름 보호).</summary>
    public void Save(AppSettings settings)
    {
        try
        {
            string tmp = _filePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(tmp, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _log.Warn($"설정 저장 실패: {ex.Message}");
        }
    }
}
