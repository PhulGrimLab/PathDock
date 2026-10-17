using PathDock.Core.Logging;
using PathDock.Core.Settings;

namespace PathDock.Tests;

/// <summary>
/// 손상되었거나 다른 프로그램이 바꾼 settings.json이 시작을 막지 않는지(코드리뷰 2026-10-16: <c>"Favorites": null</c>이면
/// NullReferenceException으로 앱이 뜨지 못하고 파일을 지울 때까지 반복되었다).
/// </summary>
[TestClass]
public class SettingsRobustnessTests
{
    private string _dir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "pd_setrob_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private AppSettings Load(string json)
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"), json);
        return new SettingsService(_dir, NullLog.Instance).Load();
    }

    [TestMethod]
    public void Load_NullFavoritesList_BecomesEmpty()
    {
        AppSettings s = Load("{\"Favorites\": null}");
        Assert.AreEqual(0, s.Favorites.Count);
    }

    [TestMethod]
    public void Load_NullAndHalfEmptyFavoriteEntries_AreSkippedOrFilled()
    {
        AppSettings s = Load("{\"Favorites\": [null, {\"Name\":\"a\",\"Path\":\"C:\\\\x\"}, {\"Name\":null,\"Path\":null}]}");
        Assert.AreEqual(2, s.Favorites.Count, "null 항목은 건너뛴다");
        Assert.AreEqual("a", s.Favorites[0].Name);
        Assert.AreEqual("C:\\x", s.Favorites[0].Path);
        Assert.AreEqual(string.Empty, s.Favorites[1].Name);
        Assert.AreEqual(string.Empty, s.Favorites[1].Path);
    }

    [TestMethod]
    public void Load_NullStrings_BecomeDefaults()
    {
        AppSettings s = Load("{\"LastPath\":null,\"Theme\":null,\"TerminalTheme\":null,\"TerminalForeground\":null,\"TerminalBackground\":null,\"Language\":null}");
        Assert.AreEqual(string.Empty, s.LastPath);
        Assert.AreEqual("Dark", s.Theme);
        Assert.IsFalse(string.IsNullOrEmpty(s.TerminalTheme));
        Assert.AreEqual(string.Empty, s.TerminalForeground);
        Assert.AreEqual(string.Empty, s.TerminalBackground);
        Assert.AreEqual(string.Empty, s.Language);
    }

    [TestMethod]
    public void Load_AbsurdNumbers_AreClampedToSaneRanges()
    {
        AppSettings s = Load("{\"WindowWidth\":1e300,\"WindowHeight\":-5,\"WindowLeft\":1e300,\"WindowTop\":-1e300,\"ExplorerRatio\":5,\"UiFontSize\":999,\"TerminalFontSize\":0.001}");
        Assert.AreEqual(20000, s.WindowWidth);
        Assert.AreEqual(150, s.WindowHeight);
        Assert.IsNull(s.WindowLeft, "화면 밖으로 터무니없이 먼 위치는 OS 기본 위치로");
        Assert.IsNull(s.WindowTop);
        Assert.AreEqual(1, s.ExplorerRatio);
        Assert.AreEqual(AppSettings.MaxUiFontSize, s.UiFontSize);
        Assert.AreEqual(AppSettings.MinTerminalFontSize, s.TerminalFontSize);
    }

    [TestMethod]
    public void Sanitize_NonFiniteNumbers_FallBackToDefaults()
    {
        var s = new AppSettings { WindowWidth = double.NaN, WindowHeight = double.PositiveInfinity, ExplorerRatio = double.NaN, UiFontSize = double.NegativeInfinity };
        SettingsService.Sanitize(s);
        Assert.AreEqual(1100, s.WindowWidth);
        Assert.AreEqual(750, s.WindowHeight);
        Assert.AreEqual(0.6, s.ExplorerRatio);
        Assert.AreEqual(AppSettings.DefaultUiFontSize, s.UiFontSize);
    }

    [TestMethod]
    public void Load_ValidSettings_AreNotChanged_AndMoreThanTenFavoritesAreTruncated()
    {
        var original = new AppSettings
        {
            WindowWidth = 1280, WindowHeight = 800, WindowLeft = 40, WindowTop = 30, ExplorerRatio = 0.5, UiFontSize = 14, TerminalFontSize = 16,
            Theme = "Light", Language = "en",
            Favorites = Enumerable.Range(1, 12).Select(i => new FavoriteEntry { Name = "f" + i, Path = "C:\\p" + i }).ToList(),
        };
        new SettingsService(_dir, NullLog.Instance).Save(original);
        AppSettings s = new SettingsService(_dir, NullLog.Instance).Load();
        Assert.AreEqual(1280, s.WindowWidth);
        Assert.AreEqual(800, s.WindowHeight);
        Assert.AreEqual(40, s.WindowLeft);
        Assert.AreEqual(0.5, s.ExplorerRatio);
        Assert.AreEqual(14, s.UiFontSize);
        Assert.AreEqual("Light", s.Theme);
        Assert.AreEqual("en", s.Language);
        Assert.AreEqual(AppSettings.MaxFavorites, s.Favorites.Count);
        Assert.AreEqual("f10", s.Favorites[9].Name);
    }
}
