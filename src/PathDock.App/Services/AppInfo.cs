using System.IO;
using System.Reflection;
using PathDock.Core.Localization;
using PathDock.Core.Settings;

namespace PathDock.App.Services;

/// <summary>정보 창에 표시하는 제품/실행 환경 정보(상태 없음, 읽기 전용).</summary>
public static class AppInfo
{
    public const string ProductName = "PathDock";
    public static string Tagline => Loc.T(
        "폴더 즐겨찾기 · 파일 탐색 · 내장 터미널을 한 화면에 묶은 개발자용 작업 공간",
        "A developer workspace that brings folder favorites, file browsing and a built-in terminal into one window");
    public const string Author = "PhulgrimLab";
    public const string ContactEmail = "phulgrimlab@gmail.com";
    public const string Technologies = ".NET 10, WPF, MVVM(CommunityToolkit.Mvvm), WPF-UI(Fluent), Windows ConPTY";

    /// <summary>"1.0" 형태의 짧은 버전(리비전/빌드가 0이면 생략).</summary>
    public static string Version
    {
        get
        {
            Version? v = Assembly.GetExecutingAssembly().GetName().Version;
            if (v is null)
            {
                return "0.0";
            }
            return v.Revision == 0 && v.Build == 0 ? $"{v.Major}.{v.Minor}" : v.ToString(v.Revision == 0 ? 3 : 4);
        }
    }

    public static string DataDirectory => SettingsService.ResolveDataDir();
    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    /// <summary>빌드 시각(실행 파일 수정 시각). 읽을 수 없으면 null.</summary>
    public static string? BuildTime
    {
        get
        {
            try
            {
                // 단일 파일 배포에서는 Location이 빈 문자열이라(IL3000) 실행 파일 경로로 대신한다.
#pragma warning disable IL3000
                string path = Assembly.GetExecutingAssembly().Location;
#pragma warning restore IL3000
                if (string.IsNullOrEmpty(path))
                {
                    path = System.Environment.ProcessPath ?? string.Empty;
                }
                return string.IsNullOrEmpty(path) ? null : File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    /// <summary>제목 표시줄 문구. 예: "v1.0 · 빌드 2026-10-13"(빌드 시각을 읽을 수 없으면 버전만).</summary>
    public static string TitleBarText => BuildTime is { Length: >= 10 } t ? $"v{Version} · {Loc.T("빌드", "Built")} {t[..10]}" : $"v{Version}";

    /// <summary>한 줄 실행 환경 문구. 예: ".NET 10.0.0 · 64비트 · Windows 11 24H2 (빌드 26100)".</summary>
    public static string EnvironmentText => $".NET {System.Environment.Version} · {(System.Environment.Is64BitProcess ? Loc.T("64비트", "64-bit") : Loc.T("32비트", "32-bit"))} · {OsText()}";

    /// <summary>
    /// OS 이름과 빌드. 매니페스트에 따라 Environment.OSVersion이 부정확할 수 있어 레지스트리에서 읽는다(CodeAtlas와 같은 이유).
    /// </summary>
    private static string OsText()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string? build = key?.GetValue("CurrentBuildNumber") as string;
            string? display = key?.GetValue("DisplayVersion") as string;
            if (int.TryParse(build, out int number))
            {
                string name = number >= 22000 ? "Windows 11" : "Windows 10";
                return name + (string.IsNullOrEmpty(display) ? string.Empty : " " + display) + Loc.T($" (빌드 {build})", $" (build {build})");
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // 읽지 못하면 아래 기본 문구.
        }
        return System.Environment.OSVersion.VersionString;
    }

    /// <summary>문의할 때 붙여 넣을 수 있는 일반 텍스트 요약.</summary>
    public static string PlainText()
    {
        string build = BuildTime is { } t ? $"{Loc.T("빌드", "Built")} {t} · " : string.Empty;
        return $"{ProductName} {Version}{System.Environment.NewLine}{build}{EnvironmentText}{System.Environment.NewLine}{Author} · {ContactEmail}";
    }
}
