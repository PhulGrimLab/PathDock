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
        ApplyAddressBarColors(t == ApplicationTheme.Light);
        ApplyTextContrast(t == ApplicationTheme.Light);
    }

    /// <summary>주소 표시줄을 주변 카드와 구분되는 슬레이트 블루로 칠한다(테마별 값).</summary>
    private static void ApplyAddressBarColors(bool light)
    {
        System.Windows.ResourceDictionary res = System.Windows.Application.Current.Resources;
        res["PdAddressBarBackgroundBrush"] = Frozen(light ? "#E6EFFA" : "#1E2F45");
        res["PdAddressBarBorderBrush"] = Frozen(light ? "#9DB8D9" : "#3B5A80");
    }

    /// <summary>
    /// 밝은 테마에서 보조 글자색(회색)은 배경 대비가 약 3:1이라 읽기 어렵다. 4.5:1 이상이 되도록 더 진하게 덮어쓰고,
    /// 어두운 테마에서는 덮어쓴 값을 지워 WPF-UI 기본값을 쓴다.
    /// </summary>
    private static void ApplyTextContrast(bool light)
    {
        System.Windows.ResourceDictionary res = System.Windows.Application.Current.Resources;
        if (light)
        {
            res["TextFillColorSecondaryBrush"] = Frozen("#595959");
            res["TextFillColorTertiaryBrush"] = Frozen("#6B6B6B");
        }
        else
        {
            res.Remove("TextFillColorSecondaryBrush");
            res.Remove("TextFillColorTertiaryBrush");
        }
    }

    private static System.Windows.Media.SolidColorBrush Frozen(string hex)
    {
        var brush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
