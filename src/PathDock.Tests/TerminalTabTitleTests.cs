using PathDock.Core.Terminal;

namespace PathDock.Tests;

[TestClass]
public class TerminalTabTitleTests
{
    [TestMethod]
    public void Build_UnknownCwd_ShowsShellNameOnly()
    {
        Assert.AreEqual("PowerShell", TerminalTabTitle.Build(TerminalShell.PowerShell, null));
        Assert.AreEqual("cmd", TerminalTabTitle.Build(TerminalShell.CommandPrompt, ""));
        Assert.AreEqual("cmd", TerminalTabTitle.Build(TerminalShell.CommandPrompt, "   "));
    }

    [TestMethod]
    public void Build_UsesLastFolderName_WithOrWithoutTrailingSlash()
    {
        Assert.AreEqual("PowerShell · PathDock", TerminalTabTitle.Build(TerminalShell.PowerShell, @"C:\Works\PathDock"));
        Assert.AreEqual("PowerShell · PathDock", TerminalTabTitle.Build(TerminalShell.PowerShell, @"C:\Works\PathDock\"));
        Assert.AreEqual("cmd · 내 프로젝트", TerminalTabTitle.Build(TerminalShell.CommandPrompt, @"D:\작업 폴더\내 프로젝트"));
    }

    [TestMethod]
    public void Build_DriveRoot_ShowsDriveLetter()
    {
        Assert.AreEqual("PowerShell · C:", TerminalTabTitle.Build(TerminalShell.PowerShell, @"C:\"));
    }

    [TestMethod]
    public void Build_UncPath_ShowsShareOrFolderName()
    {
        Assert.AreEqual("cmd · docs", TerminalTabTitle.Build(TerminalShell.CommandPrompt, @"\\server\share\docs"));
        Assert.AreEqual("cmd · share", TerminalTabTitle.Build(TerminalShell.CommandPrompt, @"\\server\share"));
    }
}
