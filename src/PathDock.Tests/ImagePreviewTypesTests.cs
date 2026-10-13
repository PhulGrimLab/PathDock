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
}