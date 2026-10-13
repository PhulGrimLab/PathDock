using PathDock.Core.Terminal;

namespace PathDock.Tests;

[TestClass]
public class TerminalThemeTests
{
    [TestMethod]
    public void TryParseColor_AcceptsHexWithOrWithoutHash_RejectsOthers()
    {
        Assert.AreEqual(0xFF112233u, TerminalThemes.TryParseColor("#112233"));
        Assert.AreEqual(0xFFAABBCCu, TerminalThemes.TryParseColor(" aabbcc "));
        Assert.IsNull(TerminalThemes.TryParseColor("#12345"));
        Assert.IsNull(TerminalThemes.TryParseColor("red"));
        Assert.IsNull(TerminalThemes.TryParseColor(""));
        Assert.IsNull(TerminalThemes.TryParseColor(null));
    }

    [TestMethod]
    public void Resolve_OverridesOnlyValidColors_AndUnknownThemeFallsBackToDefault()
    {
        TerminalColorScheme t = TerminalThemes.Resolve("Dracula", "#FF0000", "zzz");
        Assert.AreEqual(0xFFFF0000u, t.Foreground);
        Assert.AreEqual(0xFF282A36u, t.Background);
        Assert.AreEqual(TerminalColorScheme.DefaultKey, TerminalThemes.Resolve("nope", null, null).Key);
    }

    [TestMethod]
    public void AllThemes_Have16AnsiColors_AndDefaultMatchesBasePalette()
    {
        foreach (TerminalColorScheme t in TerminalThemes.All)
        {
            Assert.AreEqual(16, t.Ansi.Length, t.Key);
        }
        Assert.AreEqual(Palette.Get(1), TerminalThemes.All[0].Ansi[1]);
    }
}
