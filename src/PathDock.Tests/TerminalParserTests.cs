using System.Text;
using PathDock.Core.Terminal;

namespace PathDock.Tests;

/// <summary>
/// VT 파서(<see cref="VtParser"/>)와 화면 버퍼(<see cref="ScreenBuffer"/>)의 동작 테스트. 이 둘은 코드에서 가장 복잡하고(순환 복잡도 최대 36)
/// 셸 출력(신뢰할 수 없는 입력)을 직접 받는데, 보강 전에는 줄 커버리지가 65%/57%였다.
/// 기대값은 코드가 아니라 터미널 표준 동작(xterm/VT100 계열)을 기준으로 적었다.
/// </summary>
[TestClass]
public class TerminalParserTests
{
    private const string Esc = "\u001b";

    private static (VtParser P, ScreenBuffer B) Make(int cols = 20, int rows = 6)
    {
        var b = new ScreenBuffer(cols, rows);
        return (new VtParser(b), b);
    }

    private static void Feed(VtParser p, string s)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(s);
        p.Feed(bytes, bytes.Length);
    }

    /// <summary>한 줄의 글자(전각 문자의 뒤쪽 칸은 건너뛰고, 빈 칸은 공백, 줄 끝 공백은 제거).</summary>
    private static string Row(ScreenBuffer b, int y)
    {
        lock (b.SyncRoot)
        {
            var sb = new StringBuilder();
            foreach (Cell c in b.GetLine(y))
            {
                if ((c.Attr & CellAttr.WideTrail) != 0)
                {
                    continue;
                }
                sb.Append(c.Ch == 0 ? ' ' : char.ConvertFromUtf32(c.Ch));
            }
            return sb.ToString().TrimEnd();
        }
    }

    private static Cell At(ScreenBuffer b, int x, int y)
    {
        lock (b.SyncRoot)
        {
            return b.GetLine(y)[x];
        }
    }

    // ----- 커서 -----

    [TestMethod]
    public void Cursor_AbsoluteAndRelativeMoves_AreClampedToTheScreen()
    {
        var (p, b) = Make(20, 6);
        Feed(p, $"{Esc}[3;5H");
        Assert.AreEqual((4, 2), (b.CursorX, b.CursorY));
        Feed(p, $"{Esc}[2A");
        Assert.AreEqual(0, b.CursorY);
        Feed(p, $"{Esc}[9A");
        Assert.AreEqual(0, b.CursorY, "위쪽 끝을 넘어가면 안 된다");
        Feed(p, $"{Esc}[100B");
        Assert.AreEqual(5, b.CursorY);
        Feed(p, $"{Esc}[100C");
        Assert.AreEqual(19, b.CursorX);
        Feed(p, $"{Esc}[100D");
        Assert.AreEqual(0, b.CursorX);
        Feed(p, $"{Esc}[7G");
        Assert.AreEqual(6, b.CursorX);
        Feed(p, $"{Esc}[4d");
        Assert.AreEqual(3, b.CursorY);
        Feed(p, $"{Esc}[2E");
        Assert.AreEqual((0, 5), (b.CursorX, b.CursorY), "CNL: 아래로 이동하고 첫 열로");
        Feed(p, $"{Esc}[3F");
        Assert.AreEqual((0, 2), (b.CursorX, b.CursorY), "CPL: 위로 이동하고 첫 열로");
        Feed(p, $"{Esc}[H");
        Assert.AreEqual((0, 0), (b.CursorX, b.CursorY));
        Feed(p, $"{Esc}[;5H");
        Assert.AreEqual((4, 0), (b.CursorX, b.CursorY), "행을 생략하면 1행");
        Feed(p, $"{Esc}[4;3f");
        Assert.AreEqual((2, 3), (b.CursorX, b.CursorY), "f는 H와 같다");
        Feed(p, $"{Esc}[0A");
        Assert.AreEqual(2, b.CursorY, "0은 기본값 1로 취급한다");
    }

    [TestMethod]
    public void Cursor_SaveAndRestore_WorkWithEscAndCsi()
    {
        var (p, b) = Make();
        Feed(p, $"{Esc}[3;4H{Esc}7{Esc}[1;1H");
        Assert.AreEqual((0, 0), (b.CursorX, b.CursorY));
        Feed(p, $"{Esc}8");
        Assert.AreEqual((3, 2), (b.CursorX, b.CursorY));
        Feed(p, $"{Esc}[5;6H{Esc}[s{Esc}[1;1H{Esc}[u");
        Assert.AreEqual((5, 4), (b.CursorX, b.CursorY));
    }

    [TestMethod]
    public void Tab_GoesToNextStop_AndStaysAtLastColumn()
    {
        var (p, b) = Make(20, 3);
        Feed(p, "\t");
        Assert.AreEqual(8, b.CursorX);
        Feed(p, "\t");
        Assert.AreEqual(16, b.CursorX);
        Feed(p, "\t");
        Assert.AreEqual(19, b.CursorX, "마지막 탭 정지점이 화면 밖이면 마지막 열");
        Feed(p, "\t");
        Assert.AreEqual(19, b.CursorX);
    }

    [TestMethod]
    public void Backspace_MovesLeft_ButNotBeforeColumnZero()
    {
        var (p, b) = Make();
        Feed(p, "\b");
        Assert.AreEqual(0, b.CursorX);
        Feed(p, "abc\b");
        Assert.AreEqual(2, b.CursorX);
    }

    [TestMethod]
    public void ControlCharacters_VerticalTabFormFeedAndIndexMoveDown_BellAndDelAreIgnored()
    {
        var (p, b) = Make();
        Feed(p, "a\u000bb");
        Assert.AreEqual("a", Row(b, 0));
        Assert.AreEqual(" b", Row(b, 1), "VT는 줄만 바꾸고 열은 그대로");
        Feed(p, $"{Esc}[1;1H{Esc}[2J");
        Feed(p, $"ab{Esc}Ec");
        Assert.AreEqual("ab", Row(b, 0));
        Assert.AreEqual("c", Row(b, 1), "ESC E(NEL)는 줄바꿈 + 처음으로");
        Feed(p, $"{Esc}[1;1H{Esc}[2J");
        Feed(p, $"ab{Esc}Dc");
        Assert.AreEqual("  c", Row(b, 1), "ESC D(IND)는 줄만 바꾼다");
        Feed(p, $"{Esc}[1;1H{Esc}[2J");
        Feed(p, "x\a\u007fy\u0001z");
        Assert.AreEqual("xyz", Row(b, 0), "BEL/DEL/알 수 없는 제어 문자는 무시");
    }

    [TestMethod]
    public void ReverseIndex_AtTopScrollsDown_ElsewhereMovesUp()
    {
        var (p, b) = Make(10, 3);
        Feed(p, "aaa\r\nbbb\r\nccc");
        Feed(p, $"{Esc}[1;1H{Esc}M");
        Assert.AreEqual(string.Empty, Row(b, 0));
        Assert.AreEqual("aaa", Row(b, 1));
        Assert.AreEqual("bbb", Row(b, 2));
        Feed(p, $"{Esc}[2;1H{Esc}M");
        Assert.AreEqual(0, b.CursorY, "맨 위가 아니면 한 줄 위로만");
        Assert.AreEqual("aaa", Row(b, 1), "스크롤되지 않아야 한다");
    }

    // ----- 지우기 / 삽입 / 삭제 -----

    private static (VtParser P, ScreenBuffer B) Filled()
    {
        var (p, b) = Make(10, 3);
        Feed(p, new string('x', 30)); // 3줄을 가득 채운다(스크롤 없음)
        return (p, b);
    }

    [TestMethod]
    public void EraseDisplay_Modes_ClearTheRightCells()
    {
        var (p, b) = Filled();
        Feed(p, $"{Esc}[2;5H{Esc}[0J");
        Assert.AreEqual("xxxxxxxxxx", Row(b, 0));
        Assert.AreEqual("xxxx", Row(b, 1), "커서부터 끝까지 지움");
        Assert.AreEqual(string.Empty, Row(b, 2));

        (p, b) = Filled();
        Feed(p, $"{Esc}[2;5H{Esc}[1J");
        Assert.AreEqual(string.Empty, Row(b, 0), "처음부터 커서까지 지움");
        Assert.AreEqual("     xxxxx", Row(b, 1), "커서 칸도 지운다");
        Assert.AreEqual("xxxxxxxxxx", Row(b, 2));

        (p, b) = Filled();
        Feed(p, $"{Esc}[2;5H{Esc}[2J");
        Assert.AreEqual(string.Empty, Row(b, 0) + Row(b, 1) + Row(b, 2));
        Assert.AreEqual((4, 1), (b.CursorX, b.CursorY), "ED 2는 커서를 옮기지 않는다");

        (p, b) = Filled();
        Feed(p, $"{Esc}[2;5H{Esc}[J");
        Assert.AreEqual("xxxx", Row(b, 1), "파라미터 없으면 0");
    }

    [TestMethod]
    public void EraseDisplay3_AlsoClearsScrollback()
    {
        var (p, b) = Make(10, 3);
        for (int i = 0; i < 10; i++)
        {
            Feed(p, $"line{i}\r\n");
        }
        Assert.IsTrue(b.ScrollbackCount > 0);
        long scrolledBefore = b.ScrolledLines;
        Feed(p, $"{Esc}[3J");
        Assert.AreEqual(0, b.ScrollbackCount);
        Assert.IsTrue(b.ScrolledLines > scrolledBefore, "지워진 줄 수가 선택 영역 보정을 위해 누적되어야 한다");
    }

    [TestMethod]
    public void EraseLine_Modes_ClearTheRightCells()
    {
        var (p, b) = Make(10, 3);
        Feed(p, $"0123456789{Esc}[1;5H{Esc}[0K");
        Assert.AreEqual("0123", Row(b, 0));
        Feed(p, $"{Esc}[1;1H0123456789{Esc}[1;5H{Esc}[1K");
        Assert.AreEqual("     56789", Row(b, 0));
        Feed(p, $"{Esc}[2K");
        Assert.AreEqual(string.Empty, Row(b, 0));
    }

    [TestMethod]
    public void EraseChars_InsertChars_DeleteChars_ShiftOnlyTheCurrentLine()
    {
        var (p, b) = Make(10, 3);
        Feed(p, $"abcdefghij{Esc}[1;3H{Esc}[3X");
        Assert.AreEqual("ab   fghij", Row(b, 0));

        Feed(p, $"{Esc}[1;1H{Esc}[2Kabcdef{Esc}[1;3H{Esc}[2@");
        Assert.AreEqual("ab  cdef", Row(b, 0), "ICH: 오른쪽으로 밀고 빈 칸");

        Feed(p, $"{Esc}[1;1H{Esc}[2Kabcdef{Esc}[1;3H{Esc}[2P");
        Assert.AreEqual("abef", Row(b, 0), "DCH: 왼쪽으로 당김");

        Feed(p, $"{Esc}[1;1H{Esc}[2Kabcdef{Esc}[1;3H{Esc}[99P");
        Assert.AreEqual("ab", Row(b, 0), "남은 칸보다 많이 지워도 안전해야 한다");

        Feed(p, $"{Esc}[1;1H{Esc}[2Kabcdef{Esc}[1;3H{Esc}[99@");
        Assert.AreEqual("ab", Row(b, 0));
    }

    [TestMethod]
    public void InsertLines_DeleteLines_RespectTheScrollRegion()
    {
        var (p, b) = Make(10, 5);
        Feed(p, "0\r\n1\r\n2\r\n3\r\n4");
        Feed(p, $"{Esc}[2;1H{Esc}[L");
        CollectionAssert.AreEqual(new[] { "0", "", "1", "2", "3" }, Enumerable.Range(0, 5).Select(y => Row(b, y)).ToArray(), "IL: 커서 줄에 빈 줄 삽입, 밑은 밀려난다");
        Feed(p, $"{Esc}[M");
        CollectionAssert.AreEqual(new[] { "0", "1", "2", "3", "" }, Enumerable.Range(0, 5).Select(y => Row(b, y)).ToArray(), "DL: 커서 줄 삭제, 밑은 당겨진다");

        Feed(p, $"{Esc}[2J{Esc}[1;1H0\r\n1\r\n2\r\n3\r\n4");
        Feed(p, $"{Esc}[2;4r{Esc}[2;1H{Esc}[L"); // 스크롤 영역 2~4행
        CollectionAssert.AreEqual(new[] { "0", "", "1", "2", "4" }, Enumerable.Range(0, 5).Select(y => Row(b, y)).ToArray(), "영역 밖(0행, 4행)은 그대로");
        Feed(p, $"{Esc}[1;1H{Esc}[L");
        Assert.AreEqual("0", Row(b, 0), "커서가 영역 밖이면 IL은 무시된다");
    }

    // ----- 스크롤 -----

    [TestMethod]
    public void ScrollRegion_ScrollsOnlyInsideTheRegion_AndKeepsNoScrollback()
    {
        var (p, b) = Make(10, 5);
        Feed(p, "0\r\n1\r\n2\r\n3\r\n4");
        Feed(p, $"{Esc}[2;4r{Esc}[4;1H\n");
        CollectionAssert.AreEqual(new[] { "0", "2", "3", "", "4" }, Enumerable.Range(0, 5).Select(y => Row(b, y)).ToArray());
        Assert.AreEqual(0, b.ScrollbackCount, "전체 화면이 아닌 영역의 스크롤은 스크롤백에 쌓지 않는다");
        Feed(p, $"{Esc}[r{Esc}[1;1H");
        Assert.AreEqual((0, 0), (b.CursorX, b.CursorY), "DECSTBM은 커서를 처음으로");
        Feed(p, $"{Esc}[4;2r");
        Feed(p, $"{Esc}[5;1H\n");
        Assert.AreEqual(1, b.ScrollbackCount, "잘못된 영역(위>아래)은 전체 화면으로 되돌려 스크롤백이 쌓인다");
    }

    [TestMethod]
    public void ScrollUpAndDown_Csi_ShiftLines()
    {
        var (p, b) = Make(10, 3);
        Feed(p, "a\r\nb\r\nc");
        Feed(p, $"{Esc}[1S");
        CollectionAssert.AreEqual(new[] { "b", "c", "" }, Enumerable.Range(0, 3).Select(y => Row(b, y)).ToArray());
        Assert.AreEqual(1, b.ScrollbackCount);
        Feed(p, $"{Esc}[1T");
        CollectionAssert.AreEqual(new[] { "", "b", "c" }, Enumerable.Range(0, 3).Select(y => Row(b, y)).ToArray());
        Feed(p, $"{Esc}[100S");
        Assert.AreEqual(string.Empty, Row(b, 0) + Row(b, 1) + Row(b, 2));
    }

    [TestMethod]
    public void Scrollback_IsCappedAt2000Lines_AndCountsEveryScrolledLine()
    {
        var (p, b) = Make(12, 3);
        const int N = 2500;
        for (int i = 0; i < N; i++)
        {
            Feed(p, $"line{i}\r\n");
        }
        Assert.AreEqual(2000, b.ScrollbackCount);
        Assert.AreEqual(N - 2, b.ScrolledLines, "처음 두 줄은 화면 안에서 줄바꿈되고 나머지는 모두 위로 밀린다");
        string newest = Row(b, -1);
        Assert.IsTrue(newest.StartsWith("line") && int.Parse(newest[4..]) >= N - 6, "가장 최근 스크롤백 줄이어야 한다: " + newest);
    }

    [TestMethod]
    public void AlternateScreen_SavesAndRestoresMainScreen_AndNeverFillsScrollback()
    {
        var (p, b) = Make(10, 3);
        Feed(p, "main1\r\nmain2");
        int sb = b.ScrollbackCount;
        Feed(p, $"{Esc}[?1049h");
        Assert.IsTrue(b.IsAlternate);
        Assert.AreEqual(string.Empty, Row(b, 0) + Row(b, 1) + Row(b, 2), "대체 화면은 비어 있다");
        for (int i = 0; i < 20; i++)
        {
            Feed(p, $"alt{i}\r\n");
        }
        Assert.AreEqual(sb, b.ScrollbackCount, "대체 화면(vim 등)의 출력은 스크롤백에 남기지 않는다");
        Feed(p, $"{Esc}[?1049h");
        Assert.IsTrue(b.IsAlternate, "이미 대체 화면이면 다시 켜도 변화 없음");
        Feed(p, $"{Esc}[?1049l");
        Assert.IsFalse(b.IsAlternate);
        Assert.AreEqual("main1", Row(b, 0));
        Assert.AreEqual("main2", Row(b, 1));
        Assert.AreEqual((5, 1), (b.CursorX, b.CursorY), "커서 위치도 복원");
        Feed(p, $"{Esc}[?47h");
        Assert.IsTrue(b.IsAlternate, "모드 47");
        Feed(p, $"{Esc}[?47l{Esc}[?1047h");
        Assert.IsTrue(b.IsAlternate, "모드 1047");
        Feed(p, $"{Esc}[?1047l");
        Assert.IsFalse(b.IsAlternate);
    }

    // ----- 크기 변경 -----

    [TestMethod]
    public void Resize_ShrinkingRows_MovesTopLinesToScrollbackAndKeepsCursorOnScreen()
    {
        var (p, b) = Make(20, 6);
        Feed(p, "r0\r\nr1\r\nr2\r\nr3\r\nr4\r\nr5");
        Assert.AreEqual(5, b.CursorY);
        lock (b.SyncRoot) { b.Resize(20, 3); }
        Assert.AreEqual((3, 20), (b.Rows, b.Cols));
        Assert.AreEqual("r3", Row(b, 0));
        Assert.AreEqual("r4", Row(b, 1));
        Assert.AreEqual("r5", Row(b, 2));
        Assert.AreEqual(2, b.CursorY, "커서는 같은 줄(r5)에 머물러야 한다");
        Assert.AreEqual(3, b.ScrollbackCount, "밀려난 윗줄 r0~r2");
        Assert.AreEqual("r2", Row(b, -1));
        Assert.AreEqual("r0", Row(b, -3));
    }

    [TestMethod]
    public void Resize_Growing_KeepsContent_AndAddsBlankArea()
    {
        var (p, b) = Make(10, 3);
        Feed(p, "abc\r\ndef");
        lock (b.SyncRoot) { b.Resize(30, 8); }
        Assert.AreEqual((8, 30), (b.Rows, b.Cols));
        Assert.AreEqual("abc", Row(b, 0));
        Assert.AreEqual("def", Row(b, 1));
        Assert.AreEqual(string.Empty, Row(b, 7));
        Assert.AreEqual((3, 1), (b.CursorX, b.CursorY));
        Feed(p, new string('z', 25));
        Assert.AreEqual("defzzzzzzzzzzzzzzzzzzzzzzzzz", Row(b, 1), "넓어진 폭을 쓸 수 있어야 한다");
    }

    [TestMethod]
    public void Resize_NarrowingColumns_TruncatesAndClampsCursor()
    {
        var (p, b) = Make(20, 3);
        Feed(p, "0123456789ABCDEFGHIJ");
        lock (b.SyncRoot) { b.Resize(8, 3); }
        Assert.AreEqual("01234567", Row(b, 0));
        Assert.IsTrue(b.CursorX <= 7, $"커서가 새 폭 밖: {b.CursorX}");
    }

    [TestMethod]
    public void Resize_WithZeroOrSameSize_IsSafe_AndAlternateScreenSurvives()
    {
        var (p, b) = Make(10, 3);
        long v = b.Version;
        lock (b.SyncRoot) { b.Resize(10, 3); }
        Assert.AreEqual(v, b.Version, "같은 크기는 아무것도 하지 않는다");
        lock (b.SyncRoot) { b.Resize(0, 0); }
        Assert.AreEqual((1, 1), (b.Rows, b.Cols), "0 이하는 1로 맞춘다");
        lock (b.SyncRoot) { b.Resize(10, 3); }

        Feed(p, "main");
        Feed(p, $"{Esc}[?1049h");
        lock (b.SyncRoot) { b.Resize(15, 5); }
        Assert.IsTrue(b.IsAlternate);
        Feed(p, $"{Esc}[?1049l");
        Assert.AreEqual((5, 15), (b.Rows, b.Cols), "대체 화면 중 바뀐 크기가 주 화면에도 적용되어야 한다");
        Assert.AreEqual("main", Row(b, 0));
        for (int y = 0; y < b.Rows; y++)
        {
            _ = Row(b, y); // 모든 줄이 접근 가능해야 한다(줄 길이가 새 폭과 맞는지)
        }
        Feed(p, new string('q', 40));
    }

    // ----- 글자 -----

    [TestMethod]
    public void CharWidth_HandlesCombiningNarrowAndWideRanges()
    {
        foreach ((int cp, int w) in new[]
        {
            (0x41, 1), (0x300, 0), (0x200B, 0), (0xFE0F, 0), (0x1100, 2), (0x115F, 2), (0x1160, 1), (0xAC00, 2), (0xD7A3, 2),
            (0xD7A4, 1), (0x3000, 2), (0x1F600, 2), (0x1F650, 1), (0x20000, 2), (0x3FFFD, 2), (0x3FFFE, 1),
        })
        {
            Assert.AreEqual(w, ScreenBuffer.CharWidth(cp), $"U+{cp:X}");
        }
    }

    [TestMethod]
    public void Text_CombiningMarksAreDropped_EmojiTakesTwoCells()
    {
        var (p, b) = Make(10, 3);
        Feed(p, "e\u0301x");
        Assert.AreEqual("ex", Row(b, 0));
        Assert.AreEqual(2, b.CursorX, "결합 문자는 칸을 차지하지 않는다");
        Feed(p, $"{Esc}[2J{Esc}[1;1H😀a");
        Assert.AreEqual(0x1F600, At(b, 0, 0).Ch);
        Assert.IsTrue((At(b, 1, 0).Attr & CellAttr.WideTrail) != 0);
        Assert.AreEqual('a', (char)At(b, 2, 0).Ch);
        Assert.AreEqual(3, b.CursorX);
    }

    [TestMethod]
    public void FullReset_EscC_ClearsScreenCursorAndAttributes()
    {
        var (p, b) = Make(10, 3);
        Feed(p, $"{Esc}[31;1mabc{Esc}[2;3H{Esc}c");
        Assert.AreEqual(string.Empty, Row(b, 0));
        Assert.AreEqual((0, 0), (b.CursorX, b.CursorY));
        Feed(p, "z");
        Cell c = At(b, 0, 0);
        Assert.AreEqual(0u, c.Fg);
        Assert.AreEqual(CellAttr.None, c.Attr);
    }

    // ----- SGR(색/속성) -----

    [TestMethod]
    public void Sgr_BasicColorsAndAttributes_ApplyAndReset()
    {
        var (p, b) = Make();
        Feed(p, $"{Esc}[31;1;4mA{Esc}[0mB");
        Cell a = At(b, 0, 0), plain = At(b, 1, 0);
        Assert.AreEqual(Palette.Get(1), a.Fg);
        Assert.IsTrue(a.Attr.HasFlag(CellAttr.Bold) && a.Attr.HasFlag(CellAttr.Underline));
        Assert.AreEqual(0u, plain.Fg);
        Assert.AreEqual(CellAttr.None, plain.Attr);

        Feed(p, $"{Esc}[1;1H{Esc}[44;93mC{Esc}[39;49mD{Esc}[7mE{Esc}[27mF{Esc}[1;3mG{Esc}[22;23mH{Esc}[mI");
        Assert.AreEqual(Palette.Get(4), At(b, 0, 0).Bg);
        Assert.AreEqual(Palette.Get(11), At(b, 0, 0).Fg, "93은 밝은 노랑");
        Assert.AreEqual((0u, 0u), (At(b, 1, 0).Fg, At(b, 1, 0).Bg), "39/49는 기본색");
        Assert.IsTrue(At(b, 2, 0).Attr.HasFlag(CellAttr.Inverse));
        Assert.IsFalse(At(b, 3, 0).Attr.HasFlag(CellAttr.Inverse));
        Assert.IsTrue(At(b, 4, 0).Attr.HasFlag(CellAttr.Bold) && At(b, 4, 0).Attr.HasFlag(CellAttr.Italic));
        Assert.AreEqual(CellAttr.None, At(b, 5, 0).Attr & (CellAttr.Bold | CellAttr.Italic), "22/23으로 굵게/기울임 해제");
        Assert.AreEqual(CellAttr.None, At(b, 6, 0).Attr);
    }

    [TestMethod]
    public void Sgr_256AndTrueColor_BrightBackground_ColonSeparators()
    {
        var (p, b) = Make();
        Feed(p, $"{Esc}[38;5;196mA{Esc}[48;2;10;20;30mB{Esc}[101mC{Esc}[38:5:21mD");
        Assert.AreEqual(Palette.Get(196), At(b, 0, 0).Fg);
        Assert.AreEqual(0xFF0A141Eu, At(b, 1, 0).Bg);
        Assert.AreEqual(Palette.Get(9), At(b, 2, 0).Bg, "101은 밝은 빨강 배경");
        Assert.AreEqual(Palette.Get(21), At(b, 3, 0).Fg, ": 구분자도 ; 처럼 처리");
    }

    [TestMethod]
    public void Sgr_MalformedExtendedColors_AreIgnoredWithoutAffectingText()
    {
        var (p, b) = Make();
        Feed(p, $"{Esc}[38;2;1mX{Esc}[38mY{Esc}[48;5mZ");
        Assert.AreEqual("XYZ", Row(b, 0));
        Assert.AreEqual(0u, At(b, 0, 0).Fg, "인자가 모자란 38;2는 무시");
        Assert.AreEqual(Palette.Get(0), Palette.Get(-1) == 0 ? Palette.Get(0) : 0u);
        Assert.AreEqual(0u, Palette.Get(256), "범위 밖 팔레트 번호는 0");
    }

    // ----- 응답(장치 질의) -----

    [TestMethod]
    public void DeviceQueries_ReplyInTheExpectedFormat()
    {
        var (p, b) = Make(20, 6);
        var replies = new List<string>();
        p.Reply = replies.Add;
        Feed(p, $"{Esc}[3;5H{Esc}[6n");
        Assert.AreEqual("\u001b[3;5R", replies[^1], "CPR은 1부터 센 행;열");
        Feed(p, $"{Esc}[5n");
        Assert.AreEqual("\u001b[0n", replies[^1]);
        Feed(p, $"{Esc}[c");
        Assert.AreEqual("\u001b[?1;0c", replies[^1]);
        Feed(p, $"{Esc}[0c");
        Assert.AreEqual("\u001b[?1;0c", replies[^1]);
        Feed(p, $"{Esc}[>c");
        Assert.AreEqual("\u001b[>0;0;0c", replies[^1]);
        int count = replies.Count;
        Feed(p, $"{Esc}[7n{Esc}[?6n{Esc}[?2004h");
        Assert.AreEqual(count, replies.Count, "지원하지 않는 질의에는 응답하지 않는다");
    }

    [TestMethod]
    public void DeviceStatusReport_AtPendingWrapColumn_ReportsLastColumn()
    {
        var (p, b) = Make(5, 3);
        var replies = new List<string>();
        p.Reply = replies.Add;
        Feed(p, "abcde"); // 마지막 칸을 쓴 직후(줄바꿈 대기)
        Feed(p, $"{Esc}[6n");
        Assert.AreEqual("\u001b[1;5R", replies[^1], "열이 화면 폭을 넘지 않게 보고한다");
    }

    // ----- 모드 / 시퀀스 처리 -----

    [TestMethod]
    public void PrivateModes_CursorVisibility_AndUnknownModesAreIgnored()
    {
        var (p, b) = Make();
        Feed(p, $"{Esc}[?25l");
        Assert.IsFalse(b.CursorVisible);
        Feed(p, $"{Esc}[?25h");
        Assert.IsTrue(b.CursorVisible);
        Feed(p, $"{Esc}[?25;1049l{Esc}[?2004h{Esc}[?1000h{Esc}[4h{Esc}[20lok");
        Assert.IsFalse(b.CursorVisible, "여러 모드를 한 번에 지정");
        Assert.AreEqual("ok", Row(b, 0), "알 수 없는 모드는 무시하고 이어서 출력");
    }

    [TestMethod]
    public void EscapeSequences_WithIntermediatesAndStrings_AreConsumed()
    {
        var (p, b) = Make();
        Feed(p, $"{Esc}(Bhi");
        Assert.AreEqual("hi", Row(b, 0), "문자 집합 지정은 한 글자만 소비");
        Feed(p, $"{Esc}[1;1H{Esc}[2J{Esc}#8x");
        Assert.AreEqual("x", Row(b, 0));
        Feed(p, $"{Esc}[1;1H{Esc}[2J{Esc}Pqdata{Esc}\\ok");
        Assert.AreEqual("ok", Row(b, 0), "DCS는 ST까지 버린다");
        Feed(p, $"{Esc}[1;1H{Esc}[2J{Esc}_apc\aok2");
        Assert.AreEqual("ok2", Row(b, 0), "APC는 BEL까지 버린다");
    }

    [TestMethod]
    public void Csi_RestartedByEscape_AndHugeNumbersAreClamped()
    {
        var (p, b) = Make(20, 6);
        Feed(p, $"{Esc}[1{Esc}[3C");
        Assert.AreEqual(3, b.CursorX, "미완성 CSI 뒤에 새 ESC가 오면 새 시퀀스로");
        Feed(p, $"{Esc}[99999999999C");
        Assert.AreEqual(19, b.CursorX);
        Feed(p, $"{Esc}[99999999999;99999999999H");
        Assert.AreEqual((19, 5), (b.CursorX, b.CursorY));
    }

    [TestMethod]
    public void Osc_TitleIsReported_UnknownCommandsAreIgnored_LongOnesAreTruncated()
    {
        var (p, b) = Make();
        var titles = new List<string>();
        p.TitleChanged += titles.Add;
        Feed(p, $"{Esc}]0;My Title\a{Esc}]2;Other{Esc}\\");
        CollectionAssert.AreEqual(new[] { "My Title", "Other" }, titles);

        titles.Clear();
        Feed(p, $"{Esc}]52;c;SGVsbG8=\a{Esc}]7;file:///c:/x\a{Esc}]8;;http://x\a");
        Assert.AreEqual(0, titles.Count, "OSC 52(클립보드 쓰기) 등 지원하지 않는 명령은 무시해야 한다");
        Assert.AreEqual(string.Empty, Row(b, 0));

        Feed(p, $"{Esc}]0;{new string('a', 10000)}\a");
        Assert.AreEqual(1, titles.Count);
        Assert.IsTrue(titles[0].Length <= 4096, $"OSC 길이 상한을 넘음: {titles[0].Length}");
    }

    [TestMethod]
    public void ShellMarker_CmdStampIsStripped_EmptyPathOnlySignalsPrompt_BadFormatIsATitle()
    {
        var (p, b) = Make();
        p.MarkerToken = "t";
        string? cwd = null;
        int prompts = 0;
        var titles = new List<string>();
        p.CwdReported += c => cwd = c;
        p.PromptShown += () => prompts++;
        p.TitleChanged += titles.Add;

        Feed(p, $"{Esc}]0;PD:t:0:C:\\Dir X?12345{Esc}\\");
        Assert.AreEqual("C:\\Dir X", cwd, "cmd가 붙이는 ?시각은 버린다");
        Assert.AreEqual(1, prompts);
        Assert.AreEqual(0, titles.Count, "마커는 제목으로 보여주지 않는다");

        cwd = null;
        Feed(p, $"{Esc}]0;PD:t:5:\a");
        Assert.IsNull(cwd);
        Assert.AreEqual(2, prompts, "경로가 없어도 프롬프트 신호는 보낸다");

        Feed(p, $"{Esc}]0;PD:t:\a");
        Assert.AreEqual(2, prompts);
        CollectionAssert.AreEqual(new[] { "PD:t:" }, titles, "형식이 맞지 않으면 일반 제목");
    }

    [TestMethod]
    public void Feeding_ByteByByte_GivesTheSameResultAsFeedingAtOnce()
    {
        string input = $"{Esc}[31mA{Esc}[1;4;7mB한😀{Esc}[2;3Hq{Esc}]0;t\a{Esc}[48;2;1;2;3mZ\r\nlast";
        var (p1, b1) = Make(20, 5);
        Feed(p1, input);
        var (p2, b2) = Make(20, 5);
        foreach (byte x in Encoding.UTF8.GetBytes(input))
        {
            p2.Feed(new[] { x }, 1);
        }
        Assert.AreEqual(b1.GetVisibleText(), b2.GetVisibleText());
        for (int y = 0; y < 5; y++)
        {
            for (int x = 0; x < 20; x++)
            {
                Cell c1 = At(b1, x, y), c2 = At(b2, x, y);
                Assert.AreEqual((c1.Ch, c1.Fg, c1.Bg, c1.Attr), (c2.Ch, c2.Fg, c2.Bg, c2.Attr), $"셀 ({x},{y})");
            }
        }
    }

    // ----- 조회 -----

    [TestMethod]
    public void GetLine_ClampsIndexes_AndTextHelpersTrimTrailingBlanks()
    {
        var (p, b) = Make(10, 3);
        Feed(p, "a  \r\n b");
        lock (b.SyncRoot)
        {
            Assert.AreSame(b.GetLine(2), b.GetLine(100), "화면 아래로 넘어간 줄 번호는 마지막 줄");
            Assert.AreSame(b.GetLine(0), b.GetLine(-99999), "스크롤백이 없으면 첫 줄");
        }
        Assert.AreEqual("a\n b", b.GetVisibleText(), "줄 끝 공백과 끝의 빈 줄은 제거");
        for (int i = 0; i < 6; i++)
        {
            Feed(p, $"\r\nn{i}");
        }
        string all = b.GetAllText();
        Assert.IsTrue(all.StartsWith("a\n b"), "스크롤백이 앞에 와야 한다: " + all);
        Assert.IsTrue(all.EndsWith("n5"));
    }

    // ----- 퍼징(회귀) -----

    private static readonly string[] FuzzPieces =
    {
        "\u001b[", "\u001b]", "\u001bP", "\u001b(", "\u001b7", "\u001b8", "\u001bM", "\u001bc", "\u0007", "\u001b\\",
        "0", "1", "2", "3", "9", "65535", "99999999999", "-1", ";", ":", "?", ">", "<", "=", " ", "!",
        "A", "B", "C", "D", "E", "F", "G", "H", "J", "K", "L", "M", "P", "S", "T", "X", "@", "d", "f", "h", "l", "m", "n", "r", "s", "u", "c",
        "\r", "\n", "\b", "\t", "\u007f", "한", "글", "😀", "e\u0301", "x", "abc",
    };

    [TestMethod]
    public void Fuzz_RandomEscapeSequencesAndResizes_NeverThrowAndKeepTheCursorInBounds()
    {
        var failures = new List<string>();
        for (int seed = 0; seed < 300 && failures.Count < 5; seed++)
        {
            var rnd = new Random(seed);
            var b = new ScreenBuffer(rnd.Next(1, 90), rnd.Next(1, 30));
            var p = new VtParser(b) { Reply = _ => { } };
            if (seed % 3 == 0)
            {
                p.MarkerToken = "tok";
            }
            try
            {
                for (int step = 0; step < 400; step++)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, rnd.Next(1, 12)).Select(_ => FuzzPieces[rnd.Next(FuzzPieces.Length)])));
                    int cut = rnd.Next(0, bytes.Length + 1); // 청크 경계에서 잘라 넣는다.
                    p.Feed(bytes[..cut], cut);
                    p.Feed(bytes[cut..], bytes.Length - cut);
                    if (rnd.Next(25) == 0)
                    {
                        lock (b.SyncRoot) { b.Resize(rnd.Next(1, 90), rnd.Next(1, 30)); }
                    }
                    lock (b.SyncRoot)
                    {
                        Assert.IsTrue(b.CursorY >= 0 && b.CursorY < b.Rows, $"CursorY {b.CursorY} (rows {b.Rows})");
                        Assert.IsTrue(b.CursorX >= 0 && (b.Cols < 2 || b.CursorX <= b.Cols), $"CursorX {b.CursorX} (cols {b.Cols})");
                    }
                    _ = b.GetVisibleText();
                }
                _ = b.GetAllText();
            }
            catch (Exception ex)
            {
                failures.Add($"seed {seed}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        Assert.AreEqual(0, failures.Count, string.Join("\n", failures));
    }
}
