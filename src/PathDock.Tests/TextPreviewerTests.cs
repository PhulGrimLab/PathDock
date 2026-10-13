using System.Text;
using PathDock.Core.Explorer;

namespace PathDock.Tests;

[TestClass]
public class TextPreviewerTests
{
    private static string TempFile(byte[] content, string ext = ".txt")
    {
        string path = Path.Combine(Path.GetTempPath(), "pd_txt_" + Guid.NewGuid().ToString("N") + ext);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static TextPreview? ReadText(string content, Encoding? encoding = null)
    {
        Encoding enc = encoding ?? new UTF8Encoding(false);
        string path = TempFile(enc.GetPreamble().Concat(enc.GetBytes(content)).ToArray()); // GetBytes는 BOM을 넣지 않으므로 직접 붙인다.
        try
        {
            return TextPreviewer.Read(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void IsPreviewable_AcceptsTextAndSourceExtensions_RejectsOthers()
    {
        foreach (string name in new[] { "a.txt", "README.MD", "b.log", "c.json", "d.cs", "e.ps1", "f.yml", "my notes.Txt" })
        {
            Assert.IsTrue(TextPreviewer.IsPreviewable(name), name);
        }
        foreach (string? name in new[] { "a.png", "b.exe", "LICENSE", "txt", "", null })
        {
            Assert.IsFalse(TextPreviewer.IsPreviewable(name), name ?? "(null)");
        }
    }

    [TestMethod]
    public void Read_ShortFile_ReturnsAllLinesAndCount()
    {
        TextPreview? p = ReadText("첫째 줄\r\n둘째 줄\r\n셋째 줄\r\n");
        Assert.IsNotNull(p);
        Assert.AreEqual("첫째 줄\n둘째 줄\n셋째 줄", p.Text);
        Assert.AreEqual(3, p.TotalLines);
    }

    [TestMethod]
    public void Read_MoreThanMaxLines_ShowsOnlyFirstLinesButCountsAll()
    {
        TextPreview? p = ReadText(string.Join("\n", Enumerable.Range(1, 45).Select(i => "line" + i)));
        Assert.IsNotNull(p);
        string[] shown = p.Text.Split('\n');
        Assert.AreEqual(TextPreviewer.MaxLines, shown.Length);
        Assert.AreEqual("line1", shown[0]);
        Assert.AreEqual(45, p.TotalLines);
    }

    [TestMethod]
    public void Read_LongLineIsTruncated_AndTabsExpanded()
    {
        TextPreview? p = ReadText(new string('x', 500) + "\n\tindented");
        Assert.IsNotNull(p);
        string[] lines = p.Text.Split('\n');
        Assert.AreEqual(TextPreviewer.MaxLineChars + 1, lines[0].Length);
        Assert.IsTrue(lines[0].EndsWith('…'));
        Assert.AreEqual("    indented", lines[1]);
    }

    [TestMethod]
    public void Read_Utf8BomAndUtf16Bom_AreDecoded()
    {
        Assert.AreEqual("hello", ReadText("hello", new UTF8Encoding(true))?.Text);
        Assert.AreEqual("안녕", ReadText("안녕", new UnicodeEncoding(false, true))?.Text);
        Assert.AreEqual("안녕", ReadText("안녕", new UnicodeEncoding(true, true))?.Text);
    }

    [TestMethod]
    public void Read_BinaryContent_ReturnsNull()
    {
        string path = TempFile(new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 });
        try
        {
            Assert.IsNull(TextPreviewer.Read(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Read_EmptyFile_ReturnsEmptyTextWithZeroLines()
    {
        TextPreview? p = ReadText(string.Empty);
        Assert.IsNotNull(p);
        Assert.AreEqual(string.Empty, p.Text);
        Assert.AreEqual(0, p.TotalLines);
    }

    [TestMethod]
    public void Read_FileLargerThanReadWindow_HasUnknownLineCount_AndNoBrokenTrailingChar()
    {
        // 한글(3바이트)로 읽기 한도를 넘겨, 한도 경계에서 글자가 잘려도 깨진 문자가 남지 않아야 한다.
        string path = TempFile(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("가나다라마바사", 20000))));
        try
        {
            TextPreview? p = TextPreviewer.Read(path);
            Assert.IsNotNull(p);
            Assert.IsNull(p.TotalLines);
            Assert.IsFalse(p.Text.Contains('�'));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Read_MissingFile_ReturnsNull()
    {
        Assert.IsNull(TextPreviewer.Read(Path.Combine(Path.GetTempPath(), "pd_missing_" + Guid.NewGuid().ToString("N") + ".txt")));
    }
}
