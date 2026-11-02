using PathDock.Core.Terminal;

namespace PathDock.Tests;

/// <summary>코드리뷰(2026-10-30)에서 찾은 결함의 재현 테스트. 고치기 전에는 실패하고 고친 뒤 통과한다.</summary>
[TestClass]
public class ReviewFixTests
{
    [TestMethod]
    public void AlternateScreen_ResizeSmaller_ThenLeave_ClampsSavedCursor()
    {
        var b = new ScreenBuffer(80, 50);
        b.MoveTo(0, 40); // 주 화면 커서가 40행
        b.SetAlternate(true);
        b.Resize(80, 30); // 대체 화면 안에서 창을 줄임
        b.SetAlternate(false);
        Assert.IsTrue(b.CursorY < b.Rows, $"CursorY={b.CursorY}, Rows={b.Rows}");
        b.Put('x'); // 예전에는 IndexOutOfRangeException
    }

    [TestMethod]
    public void AlternateScreen_ResizeNarrower_ThenLeave_ClampsSavedColumn()
    {
        var b = new ScreenBuffer(80, 10);
        b.MoveTo(70, 2);
        b.SetAlternate(true);
        b.Resize(40, 10);
        b.SetAlternate(false);
        Assert.IsTrue(b.CursorX < b.Cols, $"CursorX={b.CursorX}, Cols={b.Cols}");
        b.Put('x');
    }

    [TestMethod]
    public void PowerShell_SmartSingleQuotes_AreDoubledInsideQuotedPath()
    {
        // PowerShell은 ‘ ’ ‚ ‛ 도 작은따옴표로 취급한다: 이름에 있으면 문자열이 끊겨 뒤가 명령이 된다.
        string hostile = "C:\\x\\a\u2019;calc;\u2018b.txt";
        string? q = TerminalPathText.Quote(hostile, TerminalShell.PowerShell);
        Assert.AreEqual("'C:\\x\\a\u2019\u2019;calc;\u2018\u2018b.txt'", q);
    }

    [TestMethod]
    public void PowerShell_SmartQuoteOnly_IsStillQuoted()
    {
        string? q = TerminalPathText.Quote("C:\\x\\it\u2019s.txt", TerminalShell.PowerShell);
        Assert.AreEqual("'C:\\x\\it\u2019\u2019s.txt'", q);
    }
}

[TestClass]
public class AsyncFileLoggerRecoveryTests
{
    [TestMethod]
    public void Logger_SurvivesAccessDenied_AndWritesLaterLines()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pd_log_" + Guid.NewGuid().ToString("N"));
        string logs = Path.Combine(dir, "logs");
        string today = Path.Combine(logs, $"pathdock-{DateTime.Now:yyyyMMdd}.log");
        try
        {
            var log = new PathDock.Core.Logging.AsyncFileLogger(dir);
            Directory.CreateDirectory(today); // 로그 파일 자리에 폴더가 있으면 열 때 UnauthorizedAccessException
            log.Info("first");
            Thread.Sleep(400);
            Directory.Delete(today);
            log.Info("second");
            log.Dispose();
            Assert.IsTrue(File.Exists(today), "로그 스레드가 죽어 이후 줄이 기록되지 않았다");
            StringAssert.Contains(File.ReadAllText(today), "second");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}

[TestClass]
public class FileOperationsReviewTests
{
    private static string NewDir()
    {
        string d = Path.Combine(Path.GetTempPath(), "pd_fo_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    [TestMethod]
    public void CopyWithProgress_KeepsZoneIdentifier()
    {
        string src = NewDir(), dst = NewDir();
        try
        {
            string file = Path.Combine(src, "web.txt");
            File.WriteAllText(file, "data");
            File.WriteAllText(file + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
            string copied = PathDock.Core.Explorer.FileOperations.CopyInto(file, dst, new Progress<long>(_ => { }));
            Assert.IsTrue(File.ReadAllText(copied + ":Zone.Identifier").Contains("ZoneId=3"), "웹에서 받은 표식이 복사되지 않았다");
        }
        finally
        {
            Directory.Delete(src, true);
            Directory.Delete(dst, true);
        }
    }

    [TestMethod]
    public void CopyWithProgress_ManySmallFiles_Works()
    {
        string src = NewDir(), dst = NewDir();
        try
        {
            for (int i = 0; i < 50; i++)
            {
                File.WriteAllText(Path.Combine(src, $"f{i}.txt"), new string('x', i));
            }
            string copied = PathDock.Core.Explorer.FileOperations.CopyInto(src, dst, new Progress<long>(_ => { }));
            Assert.AreEqual(50, Directory.GetFiles(copied).Length);
            Assert.AreEqual(49, new FileInfo(Path.Combine(copied, "f49.txt")).Length);
        }
        finally
        {
            Directory.Delete(src, true);
            Directory.Delete(dst, true);
        }
    }
}

[TestClass]
public class CoreRobustnessReviewTests
{
    private static string NewDir()
    {
        string d = Path.Combine(Path.GetTempPath(), "pd_rb_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    [TestMethod]
    public void Settings_Corrupt_IsBackedUpBeforeDefaultsOverwriteIt()
    {
        string dir = NewDir();
        try
        {
            string file = Path.Combine(dir, "settings.json");
            File.WriteAllText(file, "{ 손상된 json");
            var svc = new PathDock.Core.Settings.SettingsService(dir, PathDock.Core.Logging.NullLog.Instance);
            svc.Load();
            Assert.IsTrue(File.Exists(file + ".bad"), "손상된 설정이 백업되지 않았다(다음 저장이 덮어쓰면 즐겨찾기를 잃는다)");
            StringAssert.Contains(File.ReadAllText(file + ".bad"), "손상된");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public void SizeCache_NullEntryInFile_DoesNotBreakLookup()
    {
        string dir = NewDir();
        try
        {
            var cache = new PathDock.Core.Explorer.FolderSizeCache(dir, PathDock.Core.Logging.NullLog.Instance);
            cache.Store(dir, new PathDock.Core.Explorer.FolderStats(1, 1, 0, false));
            cache.Save();
            string file = Directory.GetFiles(dir, "*.json").Single(f => !f.EndsWith("settings.json"));
            File.WriteAllText(file, "{\"C:\\X\":null}");
            var again = new PathDock.Core.Explorer.FolderSizeCache(dir, PathDock.Core.Logging.NullLog.Instance);
            again.Load();
            Assert.AreEqual(PathDock.Core.Explorer.CacheFreshness.Missing, again.Lookup(@"C:\X", out _));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

[TestClass]
public class VtResetReviewTests
{
    private const string Esc = "\u001b";

    private static void Feed(VtParser p, string s)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(s);
        p.Feed(bytes, bytes.Length);
    }

    [TestMethod]
    public void FullReset_LeavesAlternateScreen_AndRestoresRegionAndCursor()
    {
        var b = new ScreenBuffer(20, 6);
        var p = new VtParser(b);
        Feed(p, $"{Esc}[?1049h{Esc}[2;4r{Esc}[?25l"); // TUI가 대체 화면, 스크롤 영역, 커서 숨김을 쓴 상태
        Assert.IsTrue(b.IsAlternate);
        Feed(p, $"{Esc}c"); // reset
        Assert.IsFalse(b.IsAlternate, "TUI가 비정상 종료한 뒤 reset을 해도 대체 화면에 남는다");
        Assert.IsTrue(b.CursorVisible);
        Feed(p, "abc");
        Feed(p, $"{Esc}[6;1H\r\n\r\n"); // 영역이 풀렸다면 마지막 줄을 넘어 스크롤된다
        Assert.IsTrue(b.CursorY <= 5);
    }
}
