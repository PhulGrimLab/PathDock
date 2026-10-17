using PathDock.Core.Explorer;

namespace PathDock.Tests;

[TestClass]
public class ImagePreviewTypesTests
{
    [TestMethod]
    public void IsPreviewable_AcceptsCommonImageExtensions_CaseInsensitive()
    {
        foreach (string name in new[] { "a.png", "b.JPG", "c.jpeg", "d.gif", "e.bmp", "f.ico", "g.tif", "h.TIFF", "i.webp", "my photo.Png" })
        {
            Assert.IsTrue(ImagePreviewTypes.IsPreviewable(name), name);
        }
    }

    [TestMethod]
    public void IsPreviewable_RejectsOtherFilesAndEmptyNames()
    {
        foreach (string? name in new[] { "a.txt", "b.svg", "c.heic", "noext", "png", ".gitignore", "", null })
        {
            Assert.IsFalse(ImagePreviewTypes.IsPreviewable(name), name ?? "(null)");
        }
    }

    [TestMethod]
    public void IsPdf_MatchesPdfExtensionOnly_CaseInsensitive()
    {
        Assert.IsTrue(ImagePreviewTypes.IsPdf("a.pdf"));
        Assert.IsTrue(ImagePreviewTypes.IsPdf("보고서 (최종).PDF"));
        Assert.IsFalse(ImagePreviewTypes.IsPdf("a.pdf.txt"));
        Assert.IsFalse(ImagePreviewTypes.IsPdf("pdf"));
        Assert.IsFalse(ImagePreviewTypes.IsPdf(null));
        Assert.IsFalse(ImagePreviewTypes.IsPreviewable("a.pdf")); // PDF는 이미지 형식이 아니라 별도 경로로 그린다
    }

    [TestMethod]
    public void LaunchPolicy_OpensDocumentsAsNormalUser_ButKeepsExecutablesInAppContext()
    {
        foreach (string name in new[] { "a.pdf", "b.DOCX", "c.png", "d.txt", "my file (1).hwp" })
        {
            Assert.IsTrue(LaunchPolicy.OpensAsNormalUser(name), name);
        }
        foreach (string name in new[] { "setup.exe", "run.BAT", "x.cmd", "y.ps1", "z.msi", "link.lnk", "noextension", "" })
        {
            Assert.IsFalse(LaunchPolicy.OpensAsNormalUser(name), name);
        }
    }
}