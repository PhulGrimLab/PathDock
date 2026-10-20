using PathDock.Core.Settings;

namespace PathDock.Tests;

[TestClass]
public class SingleInstanceNameTests
{
    [TestMethod]
    public void SameFolder_IgnoringCaseAndTrailingSeparator_GivesSameName()
    {
        string a = SingleInstanceName.For(@"C:\Users\Test\AppData\Roaming\PathDock");
        Assert.AreEqual(a, SingleInstanceName.For(@"c:\users\test\appdata\roaming\pathdock\"));
        Assert.AreEqual(a, SingleInstanceName.For(@"C:\Users\Test\AppData\Roaming\PathDock\"));
    }

    [TestMethod]
    public void DifferentFolders_GiveDifferentNames()
    {
        Assert.AreNotEqual(SingleInstanceName.For(@"C:\data\one"), SingleInstanceName.For(@"C:\data\two"));
    }

    [TestMethod]
    public void RelativePath_IsResolvedToTheSameFullPath()
    {
        string relative = Path.Combine("sub", "dir");
        Assert.AreEqual(SingleInstanceName.For(relative), SingleInstanceName.For(Path.GetFullPath(relative)));
    }

    [TestMethod]
    public void Name_IsHexOfFixedLength_SafeForKernelObjectNames()
    {
        string name = SingleInstanceName.For(@"C:\한글 폴더\a b");
        Assert.AreEqual(16, name.Length);
        Assert.IsTrue(name.All(Uri.IsHexDigit), name);
    }
}
