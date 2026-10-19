using PathDock.Core.Explorer;

namespace PathDock.Tests;

[TestClass]
public class DropRulesTests
{
    [TestMethod]
    public void SameVolume_DefaultsToMove()
    {
        using var t = new TempTree();
        string f = t.File("a.txt");
        string dest = t.Dir("dest");
        Assert.AreEqual(DropEffect.Move, DropRules.Decide(new[] { f }, dest, ctrlHeld: false, shiftHeld: false));
    }

    [TestMethod]
    public void CtrlForcesCopy_ShiftForcesMove()
    {
        using var t = new TempTree();
        string f = t.File("a.txt");
        string dest = t.Dir("dest");
        Assert.AreEqual(DropEffect.Copy, DropRules.Decide(new[] { f }, dest, ctrlHeld: true, shiftHeld: false));
        Assert.AreEqual(DropEffect.Move, DropRules.Decide(new[] { f }, dest, ctrlHeld: false, shiftHeld: true));
    }

    [TestMethod]
    public void MoveIntoOwnParent_IsNothingToDo_ButCopyIsAllowed()
    {
        using var t = new TempTree();
        string f = t.File("a.txt");
        Assert.AreEqual(DropEffect.None, DropRules.Decide(new[] { f }, t.Root, false, false));
        Assert.AreEqual(DropEffect.Copy, DropRules.Decide(new[] { f }, t.Root, ctrlHeld: true, shiftHeld: false));
    }

    [TestMethod]
    public void FolderIntoItselfOrChild_IsRejected()
    {
        using var t = new TempTree();
        string dir = t.Dir("d");
        string child = t.Dir(Path.Combine("d", "child"));
        Assert.AreEqual(DropEffect.None, DropRules.Decide(new[] { dir }, dir, false, false));
        Assert.AreEqual(DropEffect.None, DropRules.Decide(new[] { dir }, child, true, false));
    }

    [TestMethod]
    public void MixedSelection_OnlyNoOpWhenAllAlreadyThere()
    {
        using var t = new TempTree();
        string inRoot = t.File("a.txt");
        string sub = t.Dir("sub");
        string inSub = t.File(Path.Combine("sub", "b.txt"));
        // a.txt는 이미 Root에 있고 b.txt는 아니므로 Root로의 이동은 의미가 있다.
        Assert.AreEqual(DropEffect.Move, DropRules.Decide(new[] { inRoot, inSub }, t.Root, false, false));
        Assert.AreEqual(DropEffect.Move, DropRules.Decide(new[] { inRoot }, sub, false, false));
    }

    [TestMethod]
    public void InvalidDestinations_AreRejected()
    {
        using var t = new TempTree();
        string f = t.File("a.txt");
        Assert.AreEqual(DropEffect.None, DropRules.Decide(new[] { f }, Path.Combine(t.Root, "nope"), false, false));
        Assert.AreEqual(DropEffect.None, DropRules.Decide(new[] { f }, string.Empty, false, false));
        Assert.AreEqual(DropEffect.None, DropRules.Decide(Array.Empty<string>(), t.Root, false, false));
        Assert.AreEqual(DropEffect.None, DropRules.Decide(new[] { t.File("x.txt") }, f, false, false), "파일 위에는 놓을 수 없다");
    }

    [TestMethod]
    public void DifferentVolume_DefaultsToCopy()
    {
        // 다른 드라이브가 있는 PC에서만 의미가 있다.
        DriveInfo? other = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.DriveType == DriveType.Fixed
            && !string.Equals(d.Name, Path.GetPathRoot(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase));
        if (other is null)
        {
            Assert.Inconclusive("다른 고정 드라이브가 없다");
            return;
        }
        using var t = new TempTree();
        string f = t.File("a.txt");
        Assert.AreEqual(DropEffect.Copy, DropRules.Decide(new[] { f }, other.RootDirectory.FullName, false, false));
    }
}
