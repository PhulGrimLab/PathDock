using System.Text;
using PathDock.Core.Terminal;

namespace PathDock.Tests;

/// <summary>터미널 마우스 선택 로직(<see cref="TerminalSelection"/>). 컨트롤에서 분리하기 전에는 UI 없이는 시험할 수 없었다.</summary>
[TestClass]
public class TerminalSelectionTests
{
    private static ScreenBuffer Buffer(string text, int cols = 20, int rows = 5)
    {
        var b = new ScreenBuffer(cols, rows);
        var p = new VtParser(b);
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        p.Feed(bytes, bytes.Length);
        return b;
    }

    [TestMethod]
    public void Begin_ClearsPreviousSelection_UntilTheMouseMoves()
    {
        var s = new TerminalSelection();
        s.Begin((0, 2), 0);
        s.Extend((0, 6));
        Assert.IsTrue(s.HasSelection);
        s.Begin((1, 1), 0);
        Assert.IsFalse(s.HasSelection, "새로 누르면 이전 선택은 풀린다");
        Assert.IsTrue(s.IsDragging);
        s.Extend((1, 1));
        Assert.IsFalse(s.HasSelection, "같은 칸이면 선택이 아니다");
        s.Extend((1, 4));
        Assert.IsTrue(s.HasSelection);
        s.EndDrag();
        Assert.IsFalse(s.IsDragging);
        Assert.IsTrue(s.HasSelection, "버튼을 떼도 선택은 유지");
        s.Clear();
        Assert.IsFalse(s.HasSelection);
    }

    [TestMethod]
    public void Ordered_IsTheSame_WhateverTheDragDirection()
    {
        var forward = new TerminalSelection();
        forward.Begin((1, 3), 0);
        forward.Extend((3, 5));
        var backward = new TerminalSelection();
        backward.Begin((3, 5), 0);
        backward.Extend((1, 3));
        Assert.AreEqual(forward.Ordered(), backward.Ordered());
        Assert.AreEqual(((1, 3), (3, 5)), forward.Ordered());

        var sameLine = new TerminalSelection();
        sameLine.Begin((2, 9), 0);
        sameLine.Extend((2, 4));
        Assert.AreEqual(((2, 4), (2, 9)), sameLine.Ordered(), "같은 줄은 열 순서");
    }

    [TestMethod]
    public void TryGetRowRange_FirstMiddleAndLastLines()
    {
        var s = new TerminalSelection();
        Assert.IsFalse(s.TryGetRowRange(0, 20, out _, out _), "선택이 없으면 어떤 줄도 아니다");
        s.Begin((1, 5), 0);
        s.Extend((3, 7));
        Assert.IsFalse(s.TryGetRowRange(0, 20, out _, out _));
        Assert.IsTrue(s.TryGetRowRange(1, 20, out int from, out int to));
        Assert.AreEqual((5, 19), (from, to), "첫 줄: 시작 열부터 줄 끝까지");
        Assert.IsTrue(s.TryGetRowRange(2, 20, out from, out to));
        Assert.AreEqual((0, 19), (from, to), "가운데 줄: 전체");
        Assert.IsTrue(s.TryGetRowRange(3, 20, out from, out to));
        Assert.AreEqual((0, 7), (from, to), "마지막 줄: 처음부터 끝 열까지");
        Assert.IsFalse(s.TryGetRowRange(4, 20, out _, out _));

        var single = new TerminalSelection();
        single.Begin((2, 3), 0);
        single.Extend((2, 8));
        Assert.IsTrue(single.TryGetRowRange(2, 20, out from, out to));
        Assert.AreEqual((3, 8), (from, to), "한 줄 안의 선택");
    }

    [TestMethod]
    public void ExtractText_JoinsLinesWithCrLf_TrimsTrailingBlanks_AndHandlesPartialFirstAndLastLines()
    {
        ScreenBuffer b = Buffer("hello world\r\nsecond line   \r\nthird");
        var s = new TerminalSelection();
        s.Begin((0, 6), 0);
        s.Extend((2, 2));
        lock (b.SyncRoot)
        {
            Assert.AreEqual("world\r\nsecond line\r\nthi", s.ExtractText(b));
        }
        var oneLine = new TerminalSelection();
        oneLine.Begin((0, 4), 0);
        oneLine.Extend((0, 0));
        lock (b.SyncRoot)
        {
            Assert.AreEqual("hello", oneLine.ExtractText(b), "거꾸로 끌어도 같은 글자");
        }
    }

    [TestMethod]
    public void ExtractText_SkipsWideCharacterTrailCells()
    {
        ScreenBuffer b = Buffer("a한글b");
        var s = new TerminalSelection();
        s.Begin((0, 0), 0);
        s.Extend((0, 5));
        lock (b.SyncRoot)
        {
            Assert.AreEqual("a한글b", s.ExtractText(b), "전각 문자의 뒤쪽 칸이 공백으로 끼어들면 안 된다");
        }
    }

    [TestMethod]
    public void SelectWord_SelectsTheNonBlankRun_AndIgnoresBlanksAndOutOfRange()
    {
        ScreenBuffer b = Buffer("foo  bar-baz qux");
        var s = new TerminalSelection();
        Assert.IsTrue(s.SelectWord(b, (0, 7)));
        Assert.IsTrue(s.HasSelection);
        lock (b.SyncRoot)
        {
            Assert.AreEqual("bar-baz", s.ExtractText(b), "공백이 아닌 연속 글자가 한 단어");
        }
        Assert.IsTrue(s.SelectWord(b, (0, 0)));
        lock (b.SyncRoot)
        {
            Assert.AreEqual("foo", s.ExtractText(b), "줄 처음의 단어");
        }
        Assert.IsTrue(s.SelectWord(b, (0, 15)));
        lock (b.SyncRoot)
        {
            Assert.AreEqual("qux", s.ExtractText(b), "줄 끝의 단어");
        }

        s.Clear();
        Assert.IsFalse(s.SelectWord(b, (0, 4)), "공백 위에서는 선택하지 않는다");
        Assert.IsFalse(s.HasSelection);
        Assert.IsFalse(s.SelectWord(b, (0, 99)), "줄 밖");
        Assert.IsFalse(s.SelectWord(b, (0, -1)));
    }

    [TestMethod]
    public void FollowScroll_ShiftsTheSelectionWithNewOutput_AndClampsToScrollback()
    {
        var b = new ScreenBuffer(10, 3);
        var p = new VtParser(b);
        void Feed(string t) { byte[] x = Encoding.UTF8.GetBytes(t); p.Feed(x, x.Length); }
        Feed("a\r\nb\r\nc\r\n"); // 이미 한 줄 스크롤됨
        var s = new TerminalSelection();
        long scrolled;
        lock (b.SyncRoot) { scrolled = b.ScrolledLines; }
        s.Begin((1, 0), scrolled);
        s.Extend((1, 3));
        Feed("d\r\n");
        lock (b.SyncRoot) { s.FollowScroll(b); }
        Assert.IsTrue(s.HasSelection);
        Assert.AreEqual(((0, 0), (0, 3)), s.Ordered(), "한 줄 스크롤되면 선택도 한 줄 위로");

        Feed("e\r\nf\r\n");
        lock (b.SyncRoot) { s.FollowScroll(b); }
        Assert.IsTrue(s.HasSelection);
        Assert.IsTrue(s.Ordered().Start.Line < 0, "화면 밖(스크롤백)으로 올라가도 같은 글자를 가리킨다");
    }

    [TestMethod]
    public void FollowScroll_ClearsTheSelection_WhenItScrollsOutOfTheScrollbackAndDragStops()
    {
        var b = new ScreenBuffer(10, 3);
        var p = new VtParser(b);
        var s = new TerminalSelection();
        s.Begin((0, 0), 0);
        s.Extend((0, 4));
        for (int i = 0; i < 2300; i++) // 스크롤백 상한(2000)을 넘겨 밀어낸다
        {
            byte[] x = Encoding.UTF8.GetBytes($"line{i}\r\n");
            p.Feed(x, x.Length);
        }
        lock (b.SyncRoot) { s.FollowScroll(b); }
        Assert.IsFalse(s.HasSelection, "스크롤백 밖으로 밀려난 선택은 풀린다");
        Assert.IsFalse(s.IsDragging);
    }

    [TestMethod]
    public void FollowScroll_DoesNothing_WithoutSelectionOrScrolling()
    {
        var b = new ScreenBuffer(10, 3);
        var s = new TerminalSelection();
        lock (b.SyncRoot) { s.FollowScroll(b); }
        Assert.IsFalse(s.HasSelection);
        s.Begin((1, 1), 0);
        s.Extend((1, 5));
        lock (b.SyncRoot) { s.FollowScroll(b); }
        Assert.AreEqual(((1, 1), (1, 5)), s.Ordered(), "스크롤이 없으면 그대로");
    }
}
