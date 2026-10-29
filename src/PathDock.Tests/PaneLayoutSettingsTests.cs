using PathDock.Core.Logging;
using PathDock.Core.Settings;

namespace PathDock.Tests;

/// <summary>폴더 영역 배치(4분할/2묶음/한 경로), 영역별 마지막 경로, 선택 영역이 settings.json에 저장되고 다시 읽히는지.</summary>
[TestClass]
public class PaneLayoutSettingsTests
{
    private string _dir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "pd_panelayout_" + Guid.NewGuid().ToString("N")[..8]);
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
    public void Defaults_AreSinglePathLayout_WithNoSavedPanePaths()
    {
        var s = new AppSettings();
        Assert.AreEqual(ExplorerLayout.Single, s.Layout, "기존 사용자는 지금과 같은 한 경로 화면으로 시작한다");
        Assert.AreEqual(0, s.PanePaths.Count);
        Assert.AreEqual(0, s.ActivePane);
    }

    [TestMethod]
    public void OldSettingsFile_WithoutNewFields_LoadsWithDefaults()
    {
        AppSettings s = Load("{\"LastPath\":\"C:\\\\old\",\"Theme\":\"Light\"}");
        Assert.AreEqual(ExplorerLayout.Single, s.Layout);
        Assert.AreEqual(0, s.PanePaths.Count);
        Assert.AreEqual(0, s.ActivePane);
        Assert.AreEqual("C:\\old", s.LastPath);
    }

    [TestMethod]
    public void SaveThenLoad_KeepsLayoutPanePathsAndActivePane()
    {
        var original = new AppSettings
        {
            Layout = ExplorerLayout.Quad,
            ActivePane = 2,
            PanePaths = new List<string> { "C:\\a", "D:\\b", "C:\\c d", "E:\\" },
        };
        new SettingsService(_dir, NullLog.Instance).Save(original);

        AppSettings s = new SettingsService(_dir, NullLog.Instance).Load();

        Assert.AreEqual(ExplorerLayout.Quad, s.Layout);
        Assert.AreEqual(2, s.ActivePane);
        CollectionAssert.AreEqual(new[] { "C:\\a", "D:\\b", "C:\\c d", "E:\\" }, s.PanePaths);
        Assert.IsTrue(File.ReadAllText(Path.Combine(_dir, "settings.json")).Contains("\"Quad\""), "배치는 숫자가 아니라 이름으로 저장된다(손으로 읽고 고치기 쉽게)");
    }

    [TestMethod]
    public void Load_BrokenPaneValues_AreCorrected()
    {
        AppSettings s = Load("{\"PanePaths\":[null,\"C:\\\\x\",\"1\",\"2\",\"3\",\"4\"],\"ActivePane\":9,\"Layout\":\"Dual\"}");
        Assert.AreEqual(AppSettings.PaneCount, s.PanePaths.Count, "영역 수를 넘는 항목은 버린다");
        Assert.AreEqual(string.Empty, s.PanePaths[0], "null은 빈 경로");
        Assert.AreEqual(AppSettings.PaneCount - 1, s.ActivePane, "범위를 넘는 선택 번호는 마지막 영역으로");
        Assert.AreEqual(ExplorerLayout.Dual, s.Layout);
    }

    [TestMethod]
    public void Load_NullPanePathsAndNegativeActivePane_BecomeDefaults()
    {
        AppSettings s = Load("{\"PanePaths\":null,\"ActivePane\":-3}");
        Assert.AreEqual(0, s.PanePaths.Count);
        Assert.AreEqual(0, s.ActivePane);
    }

    [TestMethod]
    public void Load_UnknownLayoutNumber_FallsBackToSinglePath()
    {
        AppSettings s = Load("{\"Layout\":42}");
        Assert.AreEqual(ExplorerLayout.Single, s.Layout);
    }

    [TestMethod]
    public void QuadExplorerRatio_DefaultsLargerThanNormal_AndIsSanitized()
    {
        var d = new AppSettings();
        Assert.IsTrue(d.QuadExplorerRatio > d.ExplorerRatio, "4분할은 탐색기를 더 크게(터미널을 더 아래로)");
        Assert.AreEqual(0.0, Load("{\"QuadExplorerRatio\":-5}").QuadExplorerRatio, "범위 밖 값은 0~1로 맞춘다");
        Assert.AreEqual(1.0, Load("{\"QuadExplorerRatio\":7}").QuadExplorerRatio);
    }
}
