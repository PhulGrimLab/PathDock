using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace PathDock.App.Services;

/// <summary>다크/라이트 테마 적용(WPF-UI 테마 매니저 래퍼). UI 스레드에서만 호출한다.</summary>
public static class ThemeService
{
    public const string Dark = "Dark";
    public const string Light = "Light";

    /// <summary>알 수 없는 값은 다크로 취급한다.</summary>
    public static string Normalize(string? theme) =>
        string.Equals(theme, Light, StringComparison.OrdinalIgnoreCase) ? Light : Dark;

    public static void Apply(string? theme)
    {
        ApplicationTheme t = Normalize(theme) == Light ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(t, WindowBackdropType.Mica, updateAccent: true);
    }
}
