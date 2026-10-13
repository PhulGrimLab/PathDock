using PathDock.Core.Explorer;
using PathDock.Core.Logging;
using PathDock.Core.Monitoring;
using PathDock.Core.Paths;
using PathDock.Core.Settings;
using PathDock.Core.Sync;
using PathDock.Core.Terminal;

namespace PathDock.Tests;


[TestClass]
public class PathNormalizerTests
{
    [TestMethod]
    public void Normalize_TrimsTrailingSeparator_AndUppercasesDrive()
    {
        Assert.AreEqual(@"C:\Works", PathNormalizer.Normalize(@"c:\Works\"));
        Assert.AreEqual(@"C:\", PathNormalizer.Normalize(@"c:\"));
    }

    [TestMethod]
    public void Normalize_ResolvesDotDot_AndQuotes()
    {
        Assert.AreEqual(@"C:\a\c", PathNormalizer.Normalize("\"C:\\a\\b\\..\\c\""));
    }

    [TestMethod]
    public void Normalize_KeepsUnc_AndKorean()
    {
        Assert.AreEqual(@"\\server\share\폴더", PathNormalizer.Normalize(@"\\server\share\폴더\"));
    }

    [TestMethod]
    public void Normalize_InvalidOrEmpty_ReturnsNull()
    {
        Assert.IsNull(PathNormalizer.Normalize(null));
        Assert.IsNull(PathNormalizer.Normalize("  "));
        Assert.IsNull(PathNormalizer.Normalize("C:\\a|b"));
    }

    [TestMethod]
    public void AreSame_IgnoresCaseAndTrailingSlash()
    {
        Assert.IsTrue(PathNormalizer.AreSame(@"c:\Works\", @"C:\WORKS"));
        Assert.IsFalse(PathNormalizer.AreSame(@"C:\a", @"C:\b"));
        Assert.IsFalse(PathNormalizer.AreSame(null, @"C:\b"));
    }
}

/// <summary>임시 폴더 트리를 만들고 지우는 테스트 헬퍼.</summary>
public sealed class TempTree : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "pd_test_" + Guid.NewGuid().ToString("N"));
    public TempTree() { Directory.CreateDirectory(Root); }
    public string Dir(string name) { string p = Path.Combine(Root, name); Directory.CreateDirectory(p); return p; }
    public string File(string rel, string content = "x") { string p = Path.Combine(Root, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)!); System.IO.File.WriteAllText(p, content); return p; }
    public void Dispose() { try { Directory.Delete(Root, true); } catch (IOException) { } }
}

[TestClass]
public class ExplorerServiceTests
{
    [TestMethod]
    public void List_PutsFoldersFirst_ThenSortsByName()
    {
        using var t = new TempTree();
        t.File("b.txt"); t.File("a.txt"); t.Dir("zdir"); t.Dir("Adir");
        var names = ExplorerService.List(t.Root).Select(i => i.Name).ToList();
        CollectionAssert.AreEqual(new[] { "Adir", "zdir", "a.txt", "b.txt" }, names);
    }

    [TestMethod]
    public void List_MissingFolder_Throws()
    {
        Assert.ThrowsExactly<DirectoryNotFoundException>(() => ExplorerService.List(@"C:\no_such_dir_pathdock"));
    }

    [TestMethod]
    public void NavigateTo_FailureKeepsCurrent_AndHistoryWorks()
    {
        using var t = new TempTree();
        string a = t.Dir("a"), b = t.Dir("b");
        var svc = new ExplorerService();
        svc.NavigateTo(a);
        svc.NavigateTo(b);
        Assert.ThrowsExactly<DirectoryNotFoundException>(() => svc.NavigateTo(Path.Combine(t.Root, "nope")));
        Assert.IsTrue(PathNormalizer.AreSame(b, svc.Current));

        Assert.IsTrue(PathNormalizer.AreSame(a, svc.GoBack()));
        Assert.IsTrue(svc.CanGoForward);
        Assert.IsTrue(PathNormalizer.AreSame(b, svc.GoForward()));
        Assert.IsFalse(svc.CanGoForward);
    }

    [TestMethod]
    public void NavigateTo_SamePath_DoesNotGrowHistory()
    {
        using var t = new TempTree();
        var svc = new ExplorerService();
        svc.NavigateTo(t.Root);
        svc.NavigateTo(t.Root + "\\");
        Assert.IsFalse(svc.CanGoBack);
    }

    [TestMethod]
    public void NavigateTo_AfterBack_DiscardsForwardHistory()
    {
        using var t = new TempTree();
        string a = t.Dir("a"), b = t.Dir("b"), c = t.Dir("c");
        var svc = new ExplorerService();
        svc.NavigateTo(a); svc.NavigateTo(b);
        svc.GoBack();
        svc.NavigateTo(c);
        Assert.IsFalse(svc.CanGoForward);
    }
}

[TestClass]
public class SettingsServiceTests
{
    [TestMethod]
    public void SaveThenLoad_RoundTrips()
    {
        using var t = new TempTree();
        var svc = new SettingsService(t.Root, NullLog.Instance);
        var s = new AppSettings { SyncMode = SyncMode.TerminalToExplorer, LastPath = @"C:\x", ExplorerRatio = 0.3 };
        s.Favorites.Add(new FavoriteEntry { Name = "A", Path = @"C:\a" });
        svc.Save(s);

        AppSettings loaded = new SettingsService(t.Root, NullLog.Instance).Load();
        Assert.AreEqual(SyncMode.TerminalToExplorer, loaded.SyncMode);
        Assert.AreEqual(@"C:\a", loaded.Favorites[0].Path);
        Assert.AreEqual(0.3, loaded.ExplorerRatio);
    }

    [TestMethod]
    public void Load_CorruptJson_ReturnsDefaults()
    {
        using var t = new TempTree();
        System.IO.File.WriteAllText(Path.Combine(t.Root, "settings.json"), "{ not json");
        AppSettings s = new SettingsService(t.Root, NullLog.Instance).Load();
        Assert.AreEqual(SyncMode.Bidirectional, s.SyncMode);
    }

    [TestMethod]
    public void Load_MoreThanMaxFavorites_IsTruncated()
    {
        using var t = new TempTree();
        var svc = new SettingsService(t.Root, NullLog.Instance);
        var s = new AppSettings();
        for (int i = 0; i < 15; i++) { s.Favorites.Add(new FavoriteEntry { Name = "f" + i, Path = @"C:\" }); }
        svc.Save(s);
        Assert.AreEqual(AppSettings.MaxFavorites, svc.Load().Favorites.Count);
    }
}

[TestClass]
public class PathSyncServiceTests
{
    private sealed class FakeTarget : IPathSyncTarget
    {
        public string? CurrentPath { get; set; }
        public bool Accepts { get; set; } = true;
        public List<string> Requests { get; } = new();
        public bool RequestChange(string path) { Requests.Add(path); return Accepts; }
    }

    private static (PathSyncService Svc, FakeTarget Explorer, FakeTarget Terminal) Create(SyncMode mode)
    {
        var e = new FakeTarget(); var t = new FakeTarget();
        return (new PathSyncService(e, t, NullLog.Instance) { Mode = mode }, e, t);
    }

    [TestMethod]
    [DataRow(SyncMode.Bidirectional, true, true)]
    [DataRow(SyncMode.ExplorerToTerminal, true, false)]
    [DataRow(SyncMode.TerminalToExplorer, false, true)]
    [DataRow(SyncMode.Off, false, false)]
    public void ModeMatrix_PropagatesOnlyAllowedDirections(SyncMode mode, bool toTerminal, bool toExplorer)
    {
        using var tree = new TempTree();
        var (svc, e, t) = Create(mode);
        svc.OnExplorerNavigated(tree.Root, ChangeOrigin.User);
        svc.OnTerminalCwdChanged(tree.Root, ChangeOrigin.User);
        Assert.AreEqual(toTerminal ? 1 : 0, t.Requests.Count);
        Assert.AreEqual(toExplorer ? 1 : 0, e.Requests.Count);
    }

    [TestMethod]
    public void SyncOrigin_IsNeverPropagated()
    {
        using var tree = new TempTree();
        var (svc, e, t) = Create(SyncMode.Bidirectional);
        svc.OnExplorerNavigated(tree.Root, ChangeOrigin.Sync);
        svc.OnTerminalCwdChanged(tree.Root, ChangeOrigin.Sync);
        Assert.AreEqual(0, e.Requests.Count + t.Requests.Count);
    }

    [TestMethod]
    public void SamePathAsTarget_IsIgnored()
    {
        using var tree = new TempTree();
        var (svc, _, t) = Create(SyncMode.Bidirectional);
        t.CurrentPath = tree.Root.ToUpperInvariant() + "\\";
        svc.OnExplorerNavigated(tree.Root, ChangeOrigin.User);
        Assert.AreEqual(0, t.Requests.Count);
    }

    [TestMethod]
    public void MissingFolder_RaisesWarning_AndDoesNotPropagate()
    {
        var (svc, _, t) = Create(SyncMode.Bidirectional);
        SyncWarning? w = null;
        svc.Warning += x => w = x;
        svc.OnExplorerNavigated(@"C:\no_such_dir_pathdock", ChangeOrigin.User);
        Assert.IsNotNull(w);
        Assert.AreEqual(0, t.Requests.Count);
    }

    [TestMethod]
    public void RequestChange_ReceivesNormalizedPath()
    {
        using var tree = new TempTree();
        var (svc, _, t) = Create(SyncMode.Bidirectional);
        svc.OnExplorerNavigated(tree.Root + "\\", ChangeOrigin.User);
        Assert.AreEqual(tree.Root, t.Requests.Single());
    }
}

[TestClass]
public class VtParserTests
{
    private static (ScreenBuffer B, VtParser P) Make(int cols = 20, int rows = 5)
    {
        var b = new ScreenBuffer(cols, rows);
        return (b, new VtParser(b));
    }

    private static void Feed(VtParser p, string s)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(s);
        p.Feed(bytes, bytes.Length);
    }

    [TestMethod]
    public void PlainText_AndNewline()
    {
        var (b, p) = Make();
        Feed(p, "abc\r\ndef");
        Assert.AreEqual("abc\ndef", b.GetVisibleText());
    }

    [TestMethod]
    public void CursorMove_AndErase()
    {
        var (b, p) = Make();
        Feed(p, "hello\x1b[1;2H" + "X" + "\x1b[K");
        Assert.AreEqual("hX", b.GetVisibleText());
    }

    [TestMethod]
    public void Sgr_SetsColorAndReset()
    {
        var (b, p) = Make();
        Feed(p, "\x1b[31mR\x1b[0mN\x1b[38;2;1;2;3mT");
        Assert.AreEqual(Palette.Get(1), b.GetLine(0)[0].Fg);
        Assert.AreEqual(0u, b.GetLine(0)[1].Fg);
        Assert.AreEqual(0xFF010203u, b.GetLine(0)[2].Fg);
    }

    [TestMethod]
    public void SplitUtf8AndEscape_AcrossChunks()
    {
        var (b, p) = Make();
        byte[] korean = System.Text.Encoding.UTF8.GetBytes("가나\x1b[1;1H다");
        p.Feed(korean, 2);                          // '가'의 일부만
        p.Feed(korean[2..], korean.Length - 2);
        Assert.AreEqual("다나", b.GetVisibleText().TrimEnd());
    }

    [TestMethod]
    public void WideChars_TakeTwoCells_AndWrapEarly()
    {
        var (b, p) = Make(cols: 5);
        Feed(p, "ab가나");   // a b [가 가] [나 나] -> 5칸: a,b,가(2)=4칸, 나는 다음 줄
        Assert.AreEqual("ab가\n나", b.GetVisibleText());
    }

    [TestMethod]
    public void ScrollsIntoScrollback_WhenFull()
    {
        var (b, p) = Make(rows: 2);
        Feed(p, "1\r\n2\r\n3");
        Assert.AreEqual("2\n3", b.GetVisibleText());
        Assert.AreEqual(1, b.ScrollbackCount);
    }

    [TestMethod]
    public void ShellMarkerInTitle_RaisesCwdThenPrompt_AndIsNotTitle()
    {
        var (_, p) = Make();
        var log = new List<string>();
        p.CwdReported += x => log.Add("cwd:" + x);
        p.PromptShown += () => log.Add("prompt");
        p.TitleChanged += x => log.Add("title:" + x);
        Feed(p, "\x1b]0;PD:3:C:\\a b\a\x1b]0;other\a");
        CollectionAssert.AreEqual(new[] { "cwd:C:\\a b", "prompt", "title:other" }, log);
    }

    [TestMethod]
    public void CursorPositionRequest_IsAnswered()
    {
        var (_, p) = Make();
        string? reply = null;
        p.Reply = r => reply = r;
        Feed(p, "ab\x1b[6n");
        Assert.AreEqual("\x1b[1;3R", reply);
    }

    [TestMethod]
    public void AlternateScreen_RestoresMainContent()
    {
        var (b, p) = Make();
        Feed(p, "main\x1b[?1049hALT\x1b[?1049l");
        Assert.AreEqual("main", b.GetVisibleText());
    }
}

[TestClass]
public class DriveServiceTests
{
    [TestMethod]
    public void List_ContainsSystemDrive_WithConsistentCapacity()
    {
        string sysRoot = Path.GetPathRoot(Environment.SystemDirectory)!;
        DriveEntry? c = DriveService.List().FirstOrDefault(d => string.Equals(d.Root, sysRoot, StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(c, "시스템 드라이브가 목록에 없음");
        Assert.IsTrue(c.TotalSize > 0);
        Assert.IsTrue(c.FreeSpace >= 0 && c.FreeSpace <= c.TotalSize);
        Assert.AreEqual(c.TotalSize - c.FreeSpace, c.UsedSize);
        Assert.IsTrue(c.UsedRatio is >= 0 and <= 1);
    }

    [TestMethod]
    public void UsedRatio_UnknownCapacity_IsZero()
    {
        Assert.AreEqual(0, new DriveEntry(@"D:\", "", DriveType.Removable, 0, 0).UsedRatio);
        Assert.AreEqual(0.25, new DriveEntry(@"C:\", "", DriveType.Fixed, 100, 75).UsedRatio);
    }

    [TestMethod]
    public void RootOf_ReturnsDriveOrUncRoot()
    {
        Assert.AreEqual(@"C:\", DriveService.RootOf(@"C:\a\b"));
        Assert.AreEqual(@"\\srv\share", DriveService.RootOf(@"\\srv\share\x"));
        Assert.IsNull(DriveService.RootOf(null));
    }
}

[TestClass]
public class FolderSizeTests
{
    [TestMethod]
    public void Calculate_SumsRecursively_AndCountsFilesAndFolders()
    {
        using var t = new TempTree();
        t.File("a.bin", new string('x', 100));
        t.File("sub/b.bin", new string('x', 50));
        t.File("sub/deep/c.bin", new string('x', 25));
        FolderStats s = FolderSizeCalculator.Calculate(t.Root);
        Assert.AreEqual(175, s.TotalSize);
        Assert.AreEqual(3, s.FileCount);
        Assert.AreEqual(2, s.FolderCount);
        Assert.IsFalse(s.Incomplete);
    }

    [TestMethod]
    public void Calculate_EmptyFolder_IsZero()
    {
        using var t = new TempTree();
        FolderStats s = FolderSizeCalculator.Calculate(t.Root);
        Assert.AreEqual(new FolderStats(0, 0, 0, false), s);
    }

    [TestMethod]
    public void Calculate_MissingFolder_IsIncomplete()
    {
        FolderStats s = FolderSizeCalculator.Calculate(Path.Combine(Path.GetTempPath(), "pd_missing_" + Guid.NewGuid().ToString("N")));
        Assert.IsTrue(s.Incomplete);
        Assert.AreEqual(0, s.TotalSize);
    }

    [TestMethod]
    public void Calculate_Cancelled_Throws()
    {
        using var t = new TempTree();
        t.File("a.txt");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => FolderSizeCalculator.Calculate(t.Root, cts.Token));
    }

    [TestMethod]
    public void ListDirectories_ReturnsOnlyFolders_Sorted_AndEmptyOnMissing()
    {
        using var t = new TempTree();
        t.Dir("b"); t.Dir("A"); t.File("f.txt");
        CollectionAssert.AreEqual(new[] { "A", "b" }, ExplorerService.ListDirectories(t.Root).Select(i => i.Name).ToList());
        Assert.AreEqual(0, ExplorerService.ListDirectories(Path.Combine(t.Root, "nope")).Count);
    }

    [TestMethod]
    [DataRow(0L, "0 B")]
    [DataRow(1023L, "1023 B")]
    [DataRow(1024L, "1 KB")]
    [DataRow(1536L, "1.5 KB")]
    [DataRow(5L * 1024 * 1024 * 1024, "5 GB")]
    public void SizeFormatter_FormatsUnits(long bytes, string expected)
    {
        Assert.AreEqual(expected, SizeFormatter.Format(bytes));
    }
}

[TestClass]
public class FolderSizeCacheTests
{
    private sealed class FakeTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [TestMethod]
    public void Lookup_MissingThenFreshThenStaleByTtl()
    {
        using var t = new TempTree();
        var time = new FakeTime();
        var cache = new FolderSizeCache(null, NullLog.Instance, TimeSpan.FromMinutes(10), time);
        Assert.AreEqual(CacheFreshness.Missing, cache.Lookup(t.Root, out _));

        cache.Store(t.Root, new FolderStats(100, 2, 1, false));
        Assert.AreEqual(CacheFreshness.Fresh, cache.Lookup(t.Root, out FolderStats? s));
        Assert.AreEqual(100, s!.TotalSize);

        time.Now += TimeSpan.FromMinutes(11);
        Assert.AreEqual(CacheFreshness.Stale, cache.Lookup(t.Root, out FolderStats? s2));
        Assert.AreEqual(100, s2!.TotalSize, "오래된 값도 우선 보여줄 수 있어야 한다");
    }

    [TestMethod]
    public void Lookup_FolderChanged_IsStale()
    {
        using var t = new TempTree();
        var cache = new FolderSizeCache(null, NullLog.Instance);
        cache.Store(t.Root, new FolderStats(1, 1, 0, false));
        Directory.SetLastWriteTimeUtc(t.Root, DateTime.UtcNow.AddHours(-5));
        Assert.AreEqual(CacheFreshness.Stale, cache.Lookup(t.Root, out _));
    }

    [TestMethod]
    public void Keys_AreCaseAndTrailingSlashInsensitive()
    {
        using var t = new TempTree();
        var cache = new FolderSizeCache(null, NullLog.Instance);
        cache.Store(t.Root.ToLowerInvariant() + "\\", new FolderStats(5, 1, 0, false));
        Assert.AreEqual(CacheFreshness.Fresh, cache.Lookup(t.Root.ToUpperInvariant(), out _));
    }

    [TestMethod]
    public void Invalidate_RemovesFolderAndDescendantsOnly()
    {
        using var t = new TempTree();
        string a = t.Dir("a"), ab = t.Dir(Path.Combine("a", "b")), ax = t.Dir("ax");
        var cache = new FolderSizeCache(null, NullLog.Instance);
        foreach (string p in new[] { a, ab, ax }) { cache.Store(p, new FolderStats(1, 0, 0, false)); }
        cache.Invalidate(a);
        Assert.AreEqual(CacheFreshness.Missing, cache.Lookup(a, out _));
        Assert.AreEqual(CacheFreshness.Missing, cache.Lookup(ab, out _));
        Assert.AreEqual(CacheFreshness.Fresh, cache.Lookup(ax, out _), "이름이 비슷한 다른 폴더는 남아야 한다");
    }

    [TestMethod]
    public void SaveThenLoad_RoundTrips_AndCorruptFileStartsEmpty()
    {
        using var t = new TempTree();
        string data = t.Dir("data"), folder = t.Dir("f");
        var c1 = new FolderSizeCache(data, NullLog.Instance);
        c1.Store(folder, new FolderStats(42, 3, 2, true));
        c1.Save();

        var c2 = new FolderSizeCache(data, NullLog.Instance);
        c2.Load();
        Assert.AreEqual(CacheFreshness.Fresh, c2.Lookup(folder, out FolderStats? s));
        Assert.AreEqual(new FolderStats(42, 3, 2, true), s);

        System.IO.File.WriteAllText(Path.Combine(data, "folder-size-cache.json"), "{ broken");
        var c3 = new FolderSizeCache(data, NullLog.Instance);
        c3.Load();
        Assert.AreEqual(0, c3.Count);
    }
}

[TestClass]
public class SystemMonitorTests
{
    [TestMethod]
    public void Sample_ReturnsValuesInRange()
    {
        using var m = new SystemMonitor(NullLog.Instance, 0);
        Thread.Sleep(300);
        SystemUsage u = m.Sample();
        Assert.IsTrue(u.CpuPercent is >= 0 and <= 100, $"cpu={u.CpuPercent}");
        Assert.IsTrue(u.MemoryPercent is > 0 and <= 100, $"mem={u.MemoryPercent}");
        Assert.IsTrue(u.MemoryTotalBytes > 0 && u.MemoryUsedBytes > 0 && u.MemoryUsedBytes <= u.MemoryTotalBytes);
        if (u.GpuPercent is { } g)
        {
            Assert.IsTrue(g is >= 0 and <= 100, $"gpu={g}");
        }
    }

    [TestMethod]
    public void Timer_RaisesUpdated()
    {
        using var m = new SystemMonitor(NullLog.Instance, 200);
        using var got = new ManualResetEventSlim();
        m.Updated += _ => got.Set();
        Assert.IsTrue(got.Wait(5000), "주기 측정 이벤트가 오지 않음");
    }

    [TestMethod]
    [DataRow("pid_12_luid_0x0_0x1_phys_0_eng_0_engtype_3D", "3D")]
    [DataRow("pid_9_luid_0x0_0x2_phys_0_eng_3_engtype_VideoDecode", "VideoDecode")]
    [DataRow("weird", "unknown")]
    public void EngineType_ParsesInstanceName(string name, string expected)
    {
        Assert.AreEqual(expected, SystemMonitor.EngineType(name));
    }
}

[TestClass]
public class SortAndFileOperationTests
{
    private sealed record Row(string Name, bool Dir, DateTime Mod, long? Size);

    private static List<Row> Sort(IEnumerable<Row> rows, SortKey k, bool asc) =>
        FileSorter.Sort(rows, k, asc, r => r.Dir, r => r.Name, r => r.Mod, r => r.Size);

    [TestMethod]
    public void Sort_FoldersFirst_NaturalNameOrder()
    {
        var rows = new[] { new Row("file10", false, default, 1), new Row("file2", false, default, 1), new Row("Zdir", true, default, null), new Row("adir", true, default, null) };
        CollectionAssert.AreEqual(new[] { "adir", "Zdir", "file2", "file10" }, Sort(rows, SortKey.Name, true).Select(r => r.Name).ToList());
        CollectionAssert.AreEqual(new[] { "Zdir", "adir", "file10", "file2" }, Sort(rows, SortKey.Name, false).Select(r => r.Name).ToList());
    }

    [TestMethod]
    public void Sort_BySize_DescendingWithinGroups_AndUnknownSizeLast()
    {
        var rows = new[] { new Row("a", false, default, 5), new Row("b", false, default, 50), new Row("d1", true, default, 7), new Row("d2", true, default, null), new Row("d3", true, default, 70) };
        CollectionAssert.AreEqual(new[] { "d3", "d1", "d2", "b", "a" }, Sort(rows, SortKey.Size, false).Select(r => r.Name).ToList());
        CollectionAssert.AreEqual(new[] { "d2", "d1", "d3", "a", "b" }, Sort(rows, SortKey.Size, true).Select(r => r.Name).ToList());
    }

    [TestMethod]
    public void Sort_ByModified_TiesFallBackToName()
    {
        var t = new DateTime(2026, 1, 1);
        var rows = new[] { new Row("b", false, t, 1), new Row("a", false, t, 1), new Row("c", false, t.AddDays(1), 1) };
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, Sort(rows, SortKey.Modified, true).Select(r => r.Name).ToList());
    }

    [TestMethod]
    public void ValidateName_RejectsBadNames()
    {
        Assert.IsNull(FileOperations.ValidateName("정상 이름.txt"));
        foreach (string bad in new[] { "", "  ", "..", "a/b", "a:b", "a?", "end.", "end " })
        {
            Assert.IsNotNull(FileOperations.ValidateName(bad), bad);
        }
    }

    [TestMethod]
    public void Rename_File_AndFolder_AndConflict()
    {
        using var t = new TempTree();
        string f = t.File("a.txt"), d = t.Dir("dir"); t.File("exists.txt");
        string f2 = FileOperations.Rename(f, "b.txt");
        Assert.IsTrue(System.IO.File.Exists(f2) && !System.IO.File.Exists(f));
        string d2 = FileOperations.Rename(d, "dir2");
        Assert.IsTrue(Directory.Exists(d2));
        Assert.ThrowsExactly<IOException>(() => FileOperations.Rename(f2, "exists.txt"));
        Assert.ThrowsExactly<ArgumentException>(() => FileOperations.Rename(f2, "bad|name"));
    }

    [TestMethod]
    public void Rename_CaseOnly_Works_ForFileAndFolder()
    {
        using var t = new TempTree();
        string f = t.File("abc.txt"), d = t.Dir("folder");
        string f2 = FileOperations.Rename(f, "ABC.txt");
        string d2 = FileOperations.Rename(d, "Folder");
        CollectionAssert.AreEquivalent(new[] { "ABC.txt", "Folder" }, new DirectoryInfo(t.Root).GetFileSystemInfos().Select(i => i.Name).ToList());
        Assert.AreEqual("ABC.txt", Path.GetFileName(f2));
        Assert.AreEqual("Folder", Path.GetFileName(d2));
    }

    [TestMethod]
    public void CreateFolder_AndSuggestName()
    {
        using var t = new TempTree();
        Assert.AreEqual("새 폴더", FileOperations.SuggestNewFolderName(t.Root));
        string made = FileOperations.CreateFolder(t.Root, "새 폴더");
        Assert.IsTrue(Directory.Exists(made));
        Assert.AreEqual("새 폴더 (2)", FileOperations.SuggestNewFolderName(t.Root));
        Assert.ThrowsExactly<IOException>(() => FileOperations.CreateFolder(t.Root, "새 폴더"));
    }

    [TestMethod]
    public void DeleteToRecycleBin_RemovesFileAndFolder()
    {
        using var t = new TempTree();
        string f = t.File("del_me_pd.txt"), d = t.Dir("del_dir_pd");
        t.File("del_dir_pd/x.txt");
        FileOperations.DeleteToRecycleBin(f);
        FileOperations.DeleteToRecycleBin(d);
        Assert.IsFalse(System.IO.File.Exists(f));
        Assert.IsFalse(Directory.Exists(d));
        Assert.ThrowsExactly<FileNotFoundException>(() => FileOperations.DeleteToRecycleBin(f));
    }

    [TestMethod]
    public void Cache_InvalidateWithAncestors_ClearsParentsToo()
    {
        using var t = new TempTree();
        string a = t.Dir("a"), ab = t.Dir(Path.Combine("a", "b")), sib = t.Dir("sib");
        var cache = new FolderSizeCache(null, NullLog.Instance);
        foreach (string p in new[] { t.Root, a, ab, sib }) { cache.Store(p, new FolderStats(1, 0, 0, false)); }
        cache.InvalidateWithAncestors(ab);
        Assert.AreEqual(CacheFreshness.Missing, cache.Lookup(ab, out _));
        Assert.AreEqual(CacheFreshness.Missing, cache.Lookup(a, out _));
        Assert.AreEqual(CacheFreshness.Missing, cache.Lookup(t.Root, out _));
        Assert.AreEqual(CacheFreshness.Fresh, cache.Lookup(sib, out _), "형제 폴더 캐시는 유지");
    }
}

[TestClass]
public class FileCategorizerTests
{
    [TestMethod]
    [DataRow("main.cs", FileCategory.Code)]
    [DataRow("README.MD", FileCategory.Document)]
    [DataRow("data.JSON", FileCategory.Data)]
    [DataRow("photo.JPG", FileCategory.Image)]
    [DataRow("song.mp3", FileCategory.Audio)]
    [DataRow("clip.mkv", FileCategory.Video)]
    [DataRow("pack.zip", FileCategory.Archive)]
    [DataRow("setup.exe", FileCategory.Executable)]
    [DataRow("PathDock.Core.dll", FileCategory.Library)]
    [DataRow("noext", FileCategory.Other)]
    [DataRow("weird.zzz", FileCategory.Other)]
    public void Categorize_ByExtension_CaseInsensitive(string name, FileCategory expected)
    {
        Assert.AreEqual(expected, FileCategorizer.Categorize(name, false));
    }

    [TestMethod]
    public void Categorize_Directory_IsFolder_RegardlessOfName()
    {
        Assert.AreEqual(FileCategory.Folder, FileCategorizer.Categorize("looks_like_file.cs", true));
    }

    [TestMethod]
    public void Settings_Theme_RoundTrips()
    {
        using var t = new TempTree();
        var svc = new SettingsService(t.Root, NullLog.Instance);
        svc.Save(new AppSettings { Theme = "Light" });
        Assert.AreEqual("Light", new SettingsService(t.Root, NullLog.Instance).Load().Theme);
        Assert.AreEqual("Dark", new AppSettings().Theme);
    }
}

[TestClass]
public class SecurityFixTests
{
    [TestMethod]
    [DataRow("C:\\a'b", "C:\\a''b")]
    [DataRow("C:\\Bob\u2019s", "C:\\Bob\u2019\u2019s")]
    [DataRow("C:\\x\u2019;calc;\u2019y", "C:\\x\u2019\u2019;calc;\u2019\u2019y")]
    [DataRow("C:\\plain", "C:\\plain")]
    public void EscapeSingleQuoted_DoublesEveryQuoteChar(string input, string expected)
    {
        Assert.AreEqual(expected, TerminalSession.EscapeSingleQuoted(input));
    }

    [TestMethod]
    public void VtParser_WithToken_IgnoresForgedMarker()
    {
        var b = new ScreenBuffer(80, 5);
        var p = new VtParser(b) { MarkerToken = "abc" };
        int prompts = 0;
        string? cwd = null;
        p.PromptShown += () => prompts++;
        p.CwdReported += c => cwd = c;
        p.Feed(System.Text.Encoding.UTF8.GetBytes("\x1b]0;PD:9:C:\\evil\a"), 0 + System.Text.Encoding.UTF8.GetByteCount("\x1b]0;PD:9:C:\\evil\a"));
        Assert.AreEqual(0, prompts);
        Assert.IsNull(cwd);
        string real = "\x1b]0;PD:abc:3:C:\\ok\a";
        p.Feed(System.Text.Encoding.UTF8.GetBytes(real), System.Text.Encoding.UTF8.GetByteCount(real));
        Assert.AreEqual(1, prompts);
        Assert.AreEqual("C:\\ok", cwd);
    }

    [TestMethod]
    public void Logger_Sanitize_RemovesControlChars()
    {
        Assert.AreEqual("a b c", AsyncFileLogger.Sanitize("a\nb\rc"));
        Assert.AreEqual("plain", AsyncFileLogger.Sanitize("plain"));
    }

    [TestMethod]
    public void UsesRecycleBin_FalseForUnc()
    {
        Assert.IsFalse(FileOperations.UsesRecycleBin(@"\\server\share\x"));
    }
}