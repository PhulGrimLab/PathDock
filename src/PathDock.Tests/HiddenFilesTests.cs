using PathDock.Core.Explorer;
using PathDock.Core.Logging;
using PathDock.Core.Settings;

namespace PathDock.Tests;

/// <summary>숨김/시스템 파일 표시 전환: 기본은 제외, 켜면 포함하고 IsHidden으로 구분한다.</summary>
[TestClass]
public class HiddenFilesTests
{
    private static string MakeHiddenFile(TempTree t, string name)
    {
        string p = t.File(name);
        File.SetAttributes(p, File.GetAttributes(p) | FileAttributes.Hidden);
        return p;
    }

    private static string MakeHiddenDir(TempTree t, string name)
    {
        string p = t.Dir(name);
        File.SetAttributes(p, File.GetAttributes(p) | FileAttributes.Hidden);
        return p;
    }

    [TestMethod]
    public void List_DefaultExcludesHiddenAndSystem()
    {
        using var t = new TempTree();
        t.File("visible.txt"); MakeHiddenFile(t, "secret.txt"); MakeHiddenDir(t, ".git");
        CollectionAssert.AreEqual(new[] { "visible.txt" }, ExplorerService.List(t.Root).Select(i => i.Name).ToList());
    }

    [TestMethod]
    public void List_IncludeHidden_ShowsThemAndMarksIsHidden()
    {
        using var t = new TempTree();
        t.File("visible.txt"); MakeHiddenFile(t, "secret.txt"); MakeHiddenDir(t, ".git");
        List<FileItem> items = ExplorerService.List(t.Root, includeHidden: true);
        CollectionAssert.AreEqual(new[] { ".git", "secret.txt", "visible.txt" }, items.Select(i => i.Name).ToList());
        Assert.IsTrue(items.Single(i => i.Name == ".git").IsHidden);
        Assert.IsTrue(items.Single(i => i.Name == "secret.txt").IsHidden);
        Assert.IsFalse(items.Single(i => i.Name == "visible.txt").IsHidden);
    }

    [TestMethod]
    public void Service_ShowHidden_AppliesToNavigateAndRefresh()
    {
        using var t = new TempTree();
        t.File("visible.txt"); MakeHiddenFile(t, "secret.txt");
        var service = new ExplorerService();
        Assert.AreEqual(1, service.NavigateTo(t.Root).Count);
        service.ShowHidden = true;
        Assert.AreEqual(2, service.Refresh().Count);
        service.ShowHidden = false;
        Assert.AreEqual(1, service.Refresh().Count);
    }

    [TestMethod]
    public void ListDirectories_IncludeHidden_ShowsHiddenFolders()
    {
        using var t = new TempTree();
        t.Dir("plain"); MakeHiddenDir(t, ".vs");
        CollectionAssert.AreEqual(new[] { "plain" }, ExplorerService.ListDirectories(t.Root).Select(i => i.Name).ToList());
        CollectionAssert.AreEqual(new[] { ".vs", "plain" }, ExplorerService.ListDirectories(t.Root, includeHidden: true).Select(i => i.Name).ToList());
    }

    [TestMethod]
    public void Settings_ShowHiddenFiles_DefaultsOff_AndRoundTrips()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pd_hidden_" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.IsFalse(new AppSettings().ShowHiddenFiles);
            var svc = new SettingsService(dir, NullLog.Instance);
            svc.Save(new AppSettings { ShowHiddenFiles = true });
            Assert.IsTrue(new SettingsService(dir, NullLog.Instance).Load().ShowHiddenFiles);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
