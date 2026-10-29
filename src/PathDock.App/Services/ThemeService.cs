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

    private static string? _theme = Dark;
    private static bool _watching;

    /// <summary>
    /// 테마를 적용한다. Windows 고대비 모드이면 사용자의 다크/라이트 선택 대신 시스템 고대비 색을 따른다.
    /// 고대비 설정을 켜거나 끄면 자동으로 다시 적용한다.
    /// </summary>
    public static void Apply(string? theme)
    {
        _theme = theme;
        if (!_watching)
        {
            _watching = true;
            System.Windows.SystemParameters.StaticPropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(System.Windows.SystemParameters.HighContrast))
                {
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Apply(_theme));
                }
            };
        }
        bool highContrast = System.Windows.SystemParameters.HighContrast;
        ApplicationTheme t = highContrast ? ApplicationTheme.HighContrast : Normalize(theme) == Light ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(t, WindowBackdropType.Mica, updateAccent: true);
        ApplyAddressBarColors(t == ApplicationTheme.Light, highContrast);
        ApplyTextContrast(t == ApplicationTheme.Light);
    }

    /// <summary>주소 표시줄을 주변 카드와 구분되는 슬레이트 블루로 칠한다(테마별 값).</summary>
    private static void ApplyAddressBarColors(bool light, bool highContrast)
    {
        System.Windows.ResourceDictionary res = System.Windows.Application.Current.Resources;
        if (highContrast)
        {
            res["PdAddressBarBackgroundBrush"] = System.Windows.SystemColors.WindowBrush;
            res["PdAddressBarBorderBrush"] = System.Windows.SystemColors.WindowTextBrush;
            res["PdDrivePanelBackgroundBrush"] = System.Windows.SystemColors.WindowBrush;
            res["PdPaneActiveBrush"] = System.Windows.SystemColors.HighlightBrush;
            res["PdRefreshBackgroundBrush"] = System.Windows.SystemColors.ControlBrush;
            res["PdRefreshHoverBrush"] = System.Windows.SystemColors.HighlightBrush;
            res["PdRefreshForegroundBrush"] = System.Windows.SystemColors.ControlTextBrush;
            res["PdCopyPathBackgroundBrush"] = System.Windows.SystemColors.ControlBrush;
            res["PdCopyPathHoverBrush"] = System.Windows.SystemColors.HighlightBrush;
            foreach (string name in new[] { "Clear", "Save", "Collapse" })
            {
                res[$"PdTerm{name}BackgroundBrush"] = System.Windows.SystemColors.ControlBrush;
                res[$"PdTerm{name}HoverBrush"] = System.Windows.SystemColors.HighlightBrush;
            }
            return;
        }
        res["PdDrivePanelBackgroundBrush"] = Frozen(light ? "#E8EEF7" : "#1B2433");
        // 선택된 폴더 영역 테두리: 파란 경로 탭·강조색과 겹치지 않는 따뜻한 색(다크=앰버, 라이트=진한 주황으로 흰 배경 대비 확보).
        res["PdPaneActiveBrush"] = Frozen(light ? "#D9480F" : "#FFC83D");
        res["PdAddressBarBackgroundBrush"] = Frozen(light ? "#E6EFFA" : "#1E2F45");
        res["PdAddressBarBorderBrush"] = Frozen(light ? "#9DB8D9" : "#3B5A80");
        res["PdRefreshBackgroundBrush"] = Frozen("#2E7D32");
        res["PdRefreshHoverBrush"] = Frozen("#3E9B44");
        res["PdRefreshForegroundBrush"] = Frozen("#FFFFFF");
        res["PdCopyPathBackgroundBrush"] = Frozen("#C2410C");
        res["PdCopyPathHoverBrush"] = Frozen("#E0561A");
        res["PdTermClearBackgroundBrush"] = Frozen("#C62828");
        res["PdTermClearHoverBrush"] = Frozen("#E53935");
        res["PdTermSaveBackgroundBrush"] = Frozen("#1565C0");
        res["PdTermSaveHoverBrush"] = Frozen("#1E88E5");
        res["PdTermCollapseBackgroundBrush"] = Frozen("#6A1B9A");
        res["PdTermCollapseHoverBrush"] = Frozen("#8E24AA");
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
