using System.Windows;
using PathDock.Core.Settings;

namespace PathDock.App.Services;

/// <summary>화면 글자 크기 적용. 앱 리소스(PdFontSize, ControlContentThemeFontSize)를 바꾸면 DynamicResource로 참조하는 모든 곳이 따라온다. UI 스레드 전용.</summary>
public static class FontService
{
    public static double NormalizeUi(double size) =>
        double.IsNaN(size) ? AppSettings.DefaultUiFontSize : Math.Clamp(size, AppSettings.MinUiFontSize, AppSettings.MaxUiFontSize);

    public static double NormalizeTerminal(double size) =>
        double.IsNaN(size) ? AppSettings.DefaultTerminalFontSize : Math.Clamp(size, AppSettings.MinTerminalFontSize, AppSettings.MaxTerminalFontSize);

    public static void ApplyUi(double size)
    {
        double s = NormalizeUi(size);
        ResourceDictionary r = Application.Current.Resources;
        r["PdFontSize"] = s;
        r["PdFontSizeSmall"] = s - 1;
        r["ControlContentThemeFontSize"] = s;
    }
}