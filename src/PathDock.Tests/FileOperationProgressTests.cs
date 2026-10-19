using PathDock.Core.Explorer;

namespace PathDock.Tests;

/// <summary>복사·이동의 진행 보고와 취소: 바이트 수 보고, 취소 시 반쯤 쓴 파일/폴더 정리, 진행 보고 없는 기존 경로 유지.</summary>
[TestClass]
public class FileOperationProgressTests
{
    /// <summary>호출한 스레드에서 바로 실행되는 진행 보고기(Progress&lt;T&gt;는 비동기로 전달돼 테스트가 불안정하다).</summary>
    private sealed class SyncProgress(Action<long> onReport) : IProgress<long>
    {
        public void Report(long value) => onReport(value);
    }

    private static string BigFile(TempTree t, string name, int megabytes)
    {
        string p = Path.Combine(t.Root, name);
        using var f = File.Create(p);
        f.SetLength((long)megabytes * 1024 * 1024); // 0으로 채워진 파일
        return p;
    }

    [TestMethod]
    public void CopyInto_ReportsAllBytes_AndKeepsContentAndTimestamp()
    {
        using var t = new TempTree();
        string src = BigFile(t, "big.bin", 5);
        File.SetLastWriteTimeUtc(src, new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        string dest = t.Dir("dest");
        long reported = 0;
        string copied = FileOperations.CopyInto(src, dest, new SyncProgress(d => reported += d), CancellationToken.None);
        Assert.AreEqual(5L * 1024 * 1024, reported);
        Assert.AreEqual(new FileInfo(src).Length, new FileInfo(copied).Length);
        Assert.AreEqual(File.GetLastWriteTimeUtc(src), File.GetLastWriteTimeUtc(copied));
    }

    [TestMethod]
    public void CopyInto_Cancelled_DeletesPartialFile_AndKeepsSource()
    {
        using var t = new TempTree();
        string src = BigFile(t, "big.bin", 8);
        string dest = t.Dir("dest");
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress(_ => cts.Cancel()); // 첫 조각을 쓴 직후 취소
        Assert.ThrowsExactly<OperationCanceledException>(() => FileOperations.CopyInto(src, dest, progress, cts.Token));
        Assert.AreEqual(0, Directory.GetFileSystemEntries(dest).Length, "취소한 복사의 반쪽 파일이 남음");
        Assert.IsTrue(File.Exists(src));
    }

    [TestMethod]
    public void CopyInto_Folder_Cancelled_RemovesPartialFolder()
    {
        using var t = new TempTree();
        string src = t.Dir("srcdir");
        for (int i = 0; i < 4; i++)
        {
            using var f = File.Create(Path.Combine(src, $"f{i}.bin"));
            f.SetLength(3L * 1024 * 1024);
        }
        string dest = t.Dir("dest");
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress(_ => cts.Cancel());
        Assert.ThrowsExactly<OperationCanceledException>(() => FileOperations.CopyInto(src, dest, progress, cts.Token));
        Assert.AreEqual(0, Directory.GetFileSystemEntries(dest).Length, "취소한 폴더 복사의 결과가 남음");
        Assert.AreEqual(4, Directory.GetFiles(src).Length);
    }

    [TestMethod]
    public void CopyInto_WithoutProgress_BehavesAsBefore()
    {
        using var t = new TempTree();
        string src = t.File("a.txt", "hello");
        string dest = t.Dir("dest");
        string copied = FileOperations.CopyInto(src, dest);
        Assert.AreEqual("hello", File.ReadAllText(copied));
    }

    [TestMethod]
    public void MeasureBytes_SumsFilesRecursively_AndZeroForMissing()
    {
        using var t = new TempTree();
        t.File("d/a.txt", "12345");
        t.File("d/sub/b.txt", "123");
        Assert.AreEqual(8L, FileOperations.MeasureBytes(Path.Combine(t.Root, "d")));
        Assert.AreEqual(5L, FileOperations.MeasureBytes(Path.Combine(t.Root, "d", "a.txt")));
        Assert.AreEqual(0L, FileOperations.MeasureBytes(Path.Combine(t.Root, "nope")));
    }

    [TestMethod]
    public void MoveInto_SameVolume_Works_WithProgressParameters()
    {
        using var t = new TempTree();
        string src = t.File("m.txt", "moveme");
        string dest = t.Dir("dest");
        string moved = FileOperations.MoveInto(src, dest, new SyncProgress(_ => { }), CancellationToken.None);
        Assert.IsFalse(File.Exists(src));
        Assert.AreEqual("moveme", File.ReadAllText(moved));
    }
}
