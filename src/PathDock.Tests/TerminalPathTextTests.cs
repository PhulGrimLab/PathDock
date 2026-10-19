using PathDock.Core.Terminal;

namespace PathDock.Tests;

[TestClass]
public class TerminalPathTextTests
{
    [TestMethod]
    public void PowerShell_PlainPath_IsNotQuoted()
    {
        Assert.AreEqual(@"C:\Work\a.txt", TerminalPathText.Quote(@"C:\Work\a.txt", TerminalShell.PowerShell));
    }

    [TestMethod]
    public void PowerShell_SpacesAndSpecials_UseSingleQuotes_AndDoubleInnerQuote()
    {
        Assert.AreEqual(@"'C:\My Docs\a.txt'", TerminalPathText.Quote(@"C:\My Docs\a.txt", TerminalShell.PowerShell));
        Assert.AreEqual(@"'C:\it''s\a.txt'", TerminalPathText.Quote(@"C:\it's\a.txt", TerminalShell.PowerShell));
        Assert.AreEqual(@"'C:\a$b\c.txt'", TerminalPathText.Quote(@"C:\a$b\c.txt", TerminalShell.PowerShell));
        Assert.AreEqual(@"'C:\a&b\c.txt'", TerminalPathText.Quote(@"C:\a&b\c.txt", TerminalShell.PowerShell));
    }

    [TestMethod]
    public void PowerShell_Korean_IsNotQuotedUnlessSpaced()
    {
        Assert.AreEqual(@"C:\문서\보고서.txt", TerminalPathText.Quote(@"C:\문서\보고서.txt", TerminalShell.PowerShell));
        Assert.AreEqual(@"'C:\내 문서\a.txt'", TerminalPathText.Quote(@"C:\내 문서\a.txt", TerminalShell.PowerShell));
    }

    [TestMethod]
    public void Cmd_QuotesWithDoubleQuotes_WhenNeeded()
    {
        Assert.AreEqual(@"C:\Work\a.txt", TerminalPathText.Quote(@"C:\Work\a.txt", TerminalShell.CommandPrompt));
        Assert.AreEqual(@"""C:\My Docs\a.txt""", TerminalPathText.Quote(@"C:\My Docs\a.txt", TerminalShell.CommandPrompt));
        Assert.AreEqual(@"""C:\a&b\c.txt""", TerminalPathText.Quote(@"C:\a&b\c.txt", TerminalShell.CommandPrompt));
    }

    [TestMethod]
    public void ControlCharacters_AreRejected_SoNoCommandCanRun()
    {
        Assert.IsNull(TerminalPathText.Quote(@"C:\a" + "\r\nb.txt", TerminalShell.PowerShell));
        Assert.IsNull(TerminalPathText.Quote(@"C:\a" + "\u001bb.txt", TerminalShell.CommandPrompt));
        Assert.IsNull(TerminalPathText.Quote(string.Empty, TerminalShell.PowerShell));
    }

    [TestMethod]
    public void Join_SeparatesBySpace_AddsTrailingSpace_AndSkipsUnusable()
    {
        string bad = @"C:\bad" + "\npath";
        string text = TerminalPathText.Join(new[] { @"C:\a.txt", bad, @"C:\My Docs\b.txt" }, TerminalShell.PowerShell);
        Assert.AreEqual(@"C:\a.txt 'C:\My Docs\b.txt' ", text);
        Assert.AreEqual(string.Empty, TerminalPathText.Join(new[] { bad }, TerminalShell.PowerShell));
        Assert.IsFalse(text.Contains('\r') || text.Contains('\n'), "Enter가 들어가면 입력 즉시 실행된다");
    }
}
