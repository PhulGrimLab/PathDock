using System.Diagnostics;
using System.Reflection;
using PathDock.Core.Explorer;
using PathDock.Core.Logging;
using PathDock.Core.Paths;
using PathDock.Core.Settings;
using PathDock.Core.Terminal;

namespace PathDock.Tests;

/// <summary>
/// 보안 리뷰(2026-10-16)의 낮음 항목 수정 회귀 테스트: 링크(정션/심볼릭 링크)를 따라가는 관리자 권한 쓰기(L1), 느린 네트워크 경로(L4),
/// 링크가 있는 폴더의 이동/복사(L6), 예약 장치 이름(L7), 이전 버전 임시 파일 정리(L9).
/// </summary>
[TestClass]
public class HardeningTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "pd_hard_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        // 정션은 링크만 지워야 한다(대상 내용은 지우지 않는다). 재귀 삭제는 .NET이 링크를 따라가지 않고 링크 자체를 제거한다.
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>정션(디렉터리 링크)을 만든다. 관리자 권한이 필요 없다.</summary>
    private static void MakeJunction(string link, string target)
    {
        using var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        p.WaitForExit();
        Assert.AreEqual(0, p.ExitCode, "정션을 만들지 못함");
    }

    // ----- L1: 링크를 따라가지 않는 쓰기 -----

    [TestMethod]
    public void WriteAllTextAtomic_WritesFile_AndLeavesNoTempFiles()
    {
        string file = Path.Combine(_root, "a.json");
        SafeIo.WriteAllTextAtomic(file, "{\"x\":1}");
        SafeIo.WriteAllTextAtomic(file, "{\"x\":2}"); // 덮어쓰기
        Assert.AreEqual("{\"x\":2}", File.ReadAllText(file));
        Assert.AreEqual(1, Directory.GetFiles(_root).Length, "임시 파일이 남음: " + string.Join(", ", Directory.GetFiles(_root)));
    }

    [TestMethod]
    public void WriteAllTextAtomic_RefusesFolderThatIsALink()
    {
        string target = Path.Combine(_root, "target");
        string link = Path.Combine(_root, "link");
        Directory.CreateDirectory(target);
        MakeJunction(link, target);

        Assert.Throws<IOException>(() => SafeIo.WriteAllTextAtomic(Path.Combine(link, "settings.json"), "{}"));
        Assert.AreEqual(0, Directory.GetFileSystemEntries(target).Length, "링크를 따라 대상 폴더에 파일이 만들어짐");
    }

    [TestMethod]
    public void WriteAllTextAtomic_ReplacesASymlinkFileInsteadOfWritingThroughIt()
    {
        string victim = Path.Combine(_root, "victim.txt");
        File.WriteAllText(victim, "original");
        string file = Path.Combine(_root, "settings.json");
        try
        {
            File.CreateSymbolicLink(file, victim);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            Assert.Inconclusive("심볼릭 링크를 만들 권한이 없어 확인하지 못함(관리자 또는 개발자 모드 필요).");
            return;
        }

        SafeIo.WriteAllTextAtomic(file, "{\"new\":true}");

        Assert.AreEqual("original", File.ReadAllText(victim), "링크를 따라 대상 파일을 덮어씀");
        Assert.IsFalse(SafeIo.IsReparsePoint(file), "링크가 일반 파일로 교체되어야 함");
        Assert.AreEqual("{\"new\":true}", File.ReadAllText(file));
    }

    [TestMethod]
    public void SettingsAndCacheSave_DoNotWriteThroughLinkedDataDir()
    {
        string target = Path.Combine(_root, "target");
        string link = Path.Combine(_root, "data");
        Directory.CreateDirectory(target);
        MakeJunction(link, target);

        new SettingsService(link, NullLog.Instance).Save(new AppSettings());
        var cache = new FolderSizeCache(link, NullLog.Instance);
        cache.Store(_root, new FolderStats(1, 1, 0, false));
        cache.Save();

        Assert.AreEqual(0, Directory.GetFileSystemEntries(target).Length, "링크로 바뀐 데이터 폴더를 따라 대상에 저장됨");
    }

    [TestMethod]
    public void Logger_DoesNotWriteThroughLinkedLogsDir()
    {
        string dataDir = Path.Combine(_root, "data");
        string target = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(target);
        MakeJunction(Path.Combine(dataDir, "logs"), target);

        using (var log = new AsyncFileLogger(dataDir))
        {
            log.Info("should not be written");
            log.Warn("neither this");
        }

        Assert.AreEqual(0, Directory.GetFileSystemEntries(target).Length, "로그가 링크를 따라 다른 폴더에 기록됨");
    }

    [TestMethod]
    public void Logger_StillWritesNormally()
    {
        string dataDir = Path.Combine(_root, "data");
        using (var log = new AsyncFileLogger(dataDir))
        {
            log.Info("hello");
        }
        string[] logs = Directory.GetFiles(Path.Combine(dataDir, "logs"), "pathdock-*.log");
        Assert.AreEqual(1, logs.Length);
        StringAssert.Contains(File.ReadAllText(logs[0]), "hello");
    }

    // ----- L4: 느린 네트워크 경로 -----

    [TestMethod]
    public void DirectoryExistsWithTimeout_ReturnsPromptly_ForExistingAndMissing()
    {
        Assert.IsTrue(SafeIo.DirectoryExistsWithTimeout(_root, TimeSpan.FromSeconds(2)));
        Assert.IsFalse(SafeIo.DirectoryExistsWithTimeout(Path.Combine(_root, "none"), TimeSpan.FromSeconds(2)));
    }

    [TestMethod]
    public void DirectoryExistsWithTimeout_GivesUpOnUnreachableShare_WithinTheTimeout()
    {
        // 192.0.2.1은 문서용(TEST-NET-1) 주소라 응답이 없다. OS는 연결 시도를 수십 초 기다릴 수 있지만 호출은 시간 제한 안에 돌아와야 한다.
        var sw = Stopwatch.StartNew();
        bool exists = SafeIo.DirectoryExistsWithTimeout(@"\\192.0.2.1\pdshare", TimeSpan.FromSeconds(1));
        sw.Stop();
        Assert.IsFalse(exists);
        Assert.IsTrue(sw.ElapsedMilliseconds < 4000, $"시간 제한을 넘겨 기다림: {sw.ElapsedMilliseconds}ms");
    }

    [TestMethod]
    public void IsUnc_RecognizesNetworkPaths()
    {
        Assert.IsTrue(SafeIo.IsUnc(@"\\server\share"));
        Assert.IsTrue(SafeIo.IsUnc(@"\\?\UNC\server\share"));
        Assert.IsFalse(SafeIo.IsUnc(@"C:\Users"));
        Assert.IsFalse(SafeIo.IsUnc(@"\\?\C:\Users"));
    }

    // ----- L6: 링크가 있는 폴더 -----

    [TestMethod]
    public void RealPath_ResolvesJunctions()
    {
        string target = Path.Combine(_root, "real");
        Directory.CreateDirectory(target);
        string link = Path.Combine(_root, "alias");
        MakeJunction(link, target);
        Assert.AreEqual(Path.GetFullPath(target).TrimEnd('\\'), SafeIo.RealPath(link).TrimEnd('\\'), true);
    }

    [TestMethod]
    public void CopyInto_RejectsDestinationThatIsALinkBackIntoTheSource()
    {
        string src = Path.Combine(_root, "src");
        string child = Path.Combine(src, "child");
        string other = Path.Combine(_root, "other");
        Directory.CreateDirectory(child);
        File.WriteAllText(Path.Combine(src, "f.txt"), "x");
        Directory.CreateDirectory(other);
        string link = Path.Combine(other, "into");
        MakeJunction(link, child); // other\into -> src\child (원본 안)

        var sw = Stopwatch.StartNew();
        Assert.Throws<IOException>(() => FileOperations.CopyInto(src, link));
        Assert.IsTrue(sw.ElapsedMilliseconds < 5000, "자기 자신 안 복사를 즉시 거부해야 한다");
    }

    [TestMethod]
    public void CopyDirectory_ReportsSkippedLinks()
    {
        string src = Path.Combine(_root, "src2");
        string real = Path.Combine(_root, "real2");
        Directory.CreateDirectory(src);
        Directory.CreateDirectory(real);
        File.WriteAllText(Path.Combine(src, "f.txt"), "x");
        MakeJunction(Path.Combine(src, "lnk"), real);
        string dest = Path.Combine(_root, "dest2");
        Directory.CreateDirectory(dest);

        MethodInfo m = typeof(FileOperations).GetMethod("CopyIntoCore", BindingFlags.NonPublic | BindingFlags.Static)!;
        var args = new object?[] { src, dest, false };
        m.Invoke(null, args);

        Assert.IsTrue((bool)args[2]!, "링크를 건너뛰었는데 알리지 않음(원본 삭제로 링크가 사라질 수 있다)");
        Assert.IsTrue(File.Exists(Path.Combine(dest, "src2", "f.txt")));
        Assert.IsFalse(Directory.Exists(Path.Combine(dest, "src2", "lnk")), "링크가 복사본에 따라 들어옴");
    }

    // ----- L7: 예약 장치 이름 -----

    [TestMethod]
    [DataRow("CON")]
    [DataRow("con")]
    [DataRow("NUL")]
    [DataRow("AUX")]
    [DataRow("PRN")]
    [DataRow("COM1")]
    [DataRow("lpt9")]
    [DataRow("con.txt")]
    [DataRow("NUL.tar.gz")]
    [DataRow("CONIN$")]
    public void ValidateName_RejectsReservedDeviceNames(string name)
    {
        Assert.IsNotNull(FileOperations.ValidateName(name), name);
    }

    [TestMethod]
    [DataRow("console")]
    [DataRow("conx.txt")]
    [DataRow("aux1")]
    [DataRow("COM10")]
    [DataRow("my.con")]
    [DataRow("report.txt")]
    public void ValidateName_AllowsSimilarButOrdinaryNames(string name)
    {
        Assert.IsNull(FileOperations.ValidateName(name), name);
    }

    [TestMethod]
    public void Rename_And_CreateFolder_RefuseReservedNames()
    {
        string f = Path.Combine(_root, "a.txt");
        File.WriteAllText(f, "x");
        Assert.Throws<ArgumentException>(() => FileOperations.Rename(f, "NUL"));
        Assert.Throws<ArgumentException>(() => FileOperations.CreateFolder(_root, "con"));
    }

    // ----- L9: 이전 버전이 남긴 임시 스크립트 정리 -----

    [TestMethod]
    public void LegacyTempCleanup_DeletesOnlyOldShellIntegrationScripts()
    {
        string dir = Path.Combine(_root, "PathDock");
        Directory.CreateDirectory(dir);
        string oldScript = Path.Combine(dir, "shell-integration-1-aaaa.ps1");
        string freshScript = Path.Combine(dir, "shell-integration-2-bbbb.ps1");
        string other = Path.Combine(dir, "notes.txt");
        foreach (string f in new[] { oldScript, freshScript, other })
        {
            File.WriteAllText(f, "x");
        }
        File.SetLastWriteTimeUtc(oldScript, DateTime.UtcNow.AddDays(-3));
        File.SetLastWriteTimeUtc(other, DateTime.UtcNow.AddDays(-3));

        int deleted = LegacyTempCleanup.Run(dir);

        Assert.AreEqual(1, deleted);
        Assert.IsFalse(File.Exists(oldScript));
        Assert.IsTrue(File.Exists(freshScript), "다른 실행 중인 앱이 쓸 수 있는 최근 파일은 남겨야 한다");
        Assert.IsTrue(File.Exists(other), "스크립트가 아닌 파일은 지우면 안 된다");
    }

    [TestMethod]
    public void LegacyTempCleanup_RemovesEmptyFolder_AndIgnoresMissingFolder()
    {
        string dir = Path.Combine(_root, "PathDock2");
        Directory.CreateDirectory(dir);
        string script = Path.Combine(dir, "shell-integration-9-cccc.ps1");
        File.WriteAllText(script, "x");
        File.SetLastWriteTimeUtc(script, DateTime.UtcNow.AddDays(-2));

        Assert.AreEqual(1, LegacyTempCleanup.Run(dir));
        Assert.IsFalse(Directory.Exists(dir), "비게 된 폴더는 지워야 한다");
        Assert.AreEqual(0, LegacyTempCleanup.Run(Path.Combine(_root, "missing")));
    }
}
