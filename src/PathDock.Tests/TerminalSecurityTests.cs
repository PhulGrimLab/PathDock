using System.Diagnostics;
using System.Reflection;
using System.Text;
using PathDock.Core.Logging;
using PathDock.Core.Terminal;

namespace PathDock.Tests;

/// <summary>
/// 보안 리뷰(2026-10-16)에서 찾아 고친 문제의 회귀 테스트: 터미널 출력으로 신호를 위조(토큰 우회), 파서 자원 소모,
/// 붙여넣기 확인 우회, 관리자 권한 프로세스의 스크립트 파일/현재 폴더 실행 파일 문제.
/// </summary>
[TestClass]
public class TerminalSecurityTests
{
    private static void Feed(VtParser p, string s)
    {
        byte[] b = Encoding.UTF8.GetBytes(s);
        p.Feed(b, b.Length);
    }

    private static bool WaitFor(Func<bool> cond, int timeoutMs = 20000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (cond())
            {
                return true;
            }
            Thread.Sleep(50);
        }
        return cond();
    }

    // ----- H1: OSC 9;9 / 133은 토큰으로 인증되지 않으므로 토큰 세션에서는 무시한다 -----

    [TestMethod]
    public void OscCwdAndPrompt_AreIgnored_WhenMarkerTokenIsSet()
    {
        var p = new VtParser(new ScreenBuffer(80, 24)) { MarkerToken = "secret" };
        string? cwd = null;
        int prompts = 0;
        p.CwdReported += c => cwd = c;
        p.PromptShown += () => prompts++;

        Feed(p, "\u001b]9;9;\\\\evil.example\\share\u001b\\");
        Feed(p, "\u001b]133;A\u0007");

        Assert.IsNull(cwd, "토큰 세션이 출력만으로 위조된 경로(UNC)를 받아들임");
        Assert.AreEqual(0, prompts, "토큰 세션이 출력만으로 위조된 프롬프트 신호를 받아들임");
    }

    [TestMethod]
    public void OscCwdAndPrompt_StillWork_WithoutMarkerToken()
    {
        // 토큰을 쓰지 않는 파서(예: 다른 셸 통합)의 기존 동작은 유지한다.
        var p = new VtParser(new ScreenBuffer(80, 24));
        string? cwd = null;
        int prompts = 0;
        p.CwdReported += c => cwd = c;
        p.PromptShown += () => prompts++;

        Feed(p, "\u001b]9;9;C:\\Work\u0007");
        Feed(p, "\u001b]133;A\u0007");

        Assert.AreEqual("C:\\Work", cwd);
        Assert.AreEqual(1, prompts);
    }

    [TestMethod]
    public void RealSession_IgnoresForgedOscFromOutput_ButFollowsRealPromptMarker()
    {
        string dir = Path.GetTempPath().TrimEnd('\\');
        using var s = new TerminalSession(100, 30, NullLog.Instance);
        var reported = new List<string>();
        s.CwdChanged += (path, _) => { lock (reported) { reported.Add(path); } };
        s.Start(dir);
        Assert.IsTrue(WaitFor(() => s.IsIdle), s.Screen.GetVisibleText());

        // 셸이 OSC 9;9를 출력하게 한다(파일/원격 출력으로 들어오는 위조와 같은 바이트).
        s.SendInput("[Console]::Out.Write([char]27 + ']9;9;\\\\evil.example\\share' + [char]27 + '\\')\r");
        Assert.IsTrue(WaitFor(() => !s.IsIdle), "명령이 시작되지 않음");
        Assert.IsTrue(WaitFor(() => s.IsIdle), s.Screen.GetVisibleText());

        lock (reported)
        {
            Assert.IsFalse(reported.Any(p => p.Contains("evil.example")), "위조된 UNC 경로가 보고됨: " + string.Join(", ", reported));
        }
        Assert.IsFalse(s.Cwd?.Contains("evil.example") ?? false);
    }

    // ----- M4/M5: 짧은 입력으로 파서 락/메모리를 오래 쓰지 못하게 한다 -----

    [TestMethod]
    public void ScrollUp_WithHugeCount_IsBounded()
    {
        var p = new VtParser(new ScreenBuffer(120, 30));
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 200; i++)
        {
            Feed(p, "\u001b[65535S");
            Feed(p, "\u001b[65535T");
        }
        // 수정 전에는 한 번에 약 50ms(락 점유) - 400번이면 20초 이상. 이제는 영역 높이만큼만 민다.
        Assert.IsTrue(sw.ElapsedMilliseconds < 2000, $"큰 스크롤 횟수가 너무 오래 걸림: {sw.ElapsedMilliseconds}ms");
    }

    [TestMethod]
    public void ScrollUp_ByMoreThanRegion_BehavesLikeScrollingEveryLine()
    {
        var b = new ScreenBuffer(10, 3);
        var p = new VtParser(b);
        Feed(p, "aaa\r\nbbb\r\nccc");
        Feed(p, "\u001b[100S");
        Assert.AreEqual(string.Empty, b.GetVisibleText().Replace("\n", string.Empty).Trim(), "화면이 모두 밀려나 비어야 함");
    }

    [TestMethod]
    public void CsiParams_AreCapped()
    {
        var p = new VtParser(new ScreenBuffer(120, 30));
        Feed(p, "\u001b[" + new string(';', 200_000) + "m");
        var field = typeof(VtParser).GetField("_params", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var list = (List<int>)field.GetValue(p)!;
        Assert.IsTrue(list.Count <= 32, $"파라미터가 제한 없이 쌓임: {list.Count}");
        // 파서는 계속 정상 동작해야 한다.
        Feed(p, "ok");
    }

    // ----- M3: 붙여넣기 -----

    [TestMethod]
    public void PasteGuard_Normalize_UnifiesNewlines_KeepsTab_StripsControlChars()
    {
        Assert.AreEqual("a\rb\rc", PasteGuard.Normalize("a\r\nb\nc"));
        Assert.AreEqual("a\tb", PasteGuard.Normalize("a\tb"));
        Assert.AreEqual("ab", PasteGuard.Normalize("a\u001bb"));
        Assert.AreEqual("echo hi", PasteGuard.Normalize("echo\u0003 hi".Replace("\u0003", string.Empty)));
        Assert.AreEqual("ab", PasteGuard.Normalize("a\u0003\u007fb"));
        Assert.AreEqual(string.Empty, PasteGuard.Normalize(null));
    }

    [TestMethod]
    public void PasteGuard_SingleLineWithTrailingNewline_RunsCommandsAndNeedsConfirmation()
    {
        // 웹에서 복사한 명령 끝에 숨은 줄바꿈이 붙은 경우: 한 줄이지만 붙여넣는 즉시 실행된다.
        string text = PasteGuard.Normalize("Remove-Item C:\\x -Recurse\n");
        Assert.IsTrue(PasteGuard.RunsCommands(text));
        Assert.AreEqual(1, PasteGuard.LineCount(text));
    }

    [TestMethod]
    public void PasteGuard_PlainSingleLine_DoesNotRunCommands()
    {
        string text = PasteGuard.Normalize("echo hello");
        Assert.IsFalse(PasteGuard.RunsCommands(text));
        Assert.AreEqual(1, PasteGuard.LineCount(text));
        Assert.AreEqual(0, PasteGuard.LineCount(string.Empty));
    }

    [TestMethod]
    public void PasteGuard_Preview_ShowsFirstLinesTruncated_AndCountsHidden()
    {
        string text = PasteGuard.Normalize(string.Join("\n", Enumerable.Range(1, 8).Select(i => $"line{i}")) + "\n");
        string preview = PasteGuard.Preview(text, 5, 80, out int hidden);
        Assert.AreEqual("line1\nline2\nline3\nline4\nline5", preview);
        Assert.AreEqual(3, hidden);
        string longLine = PasteGuard.Preview(new string('x', 200), 5, 10, out int none);
        Assert.AreEqual(new string('x', 10) + "…", longLine);
        Assert.AreEqual(0, none);
    }

    // ----- M1: 스크립트 파일을 만들지 않는다 -----

    [TestMethod]
    public void ShellIntegrationCommand_IsSingleLineAsciiWithoutDoubleQuotes_AndCarriesToken()
    {
        string cmd = TerminalSession.BuildShellIntegrationCommand("abc123");
        Assert.IsTrue(cmd.Contains("abc123"));
        Assert.IsFalse(cmd.Contains('"'), "큰따옴표가 있으면 명령줄 인용이 깨진다");
        Assert.IsFalse(cmd.Contains('\n') || cmd.Contains('\r'));
        Assert.IsTrue(cmd.All(c => c < 128), "ASCII만 사용해야 한다");
    }

    [TestMethod]
    public void PowerShellSession_DoesNotWriteScriptFilesToTemp()
    {
        string dir = Path.Combine(Path.GetTempPath(), "PathDock");
        int Count() => Directory.Exists(dir) ? Directory.GetFiles(dir, "*.ps1").Length : 0;
        int before = Count();

        using var s = new TerminalSession(100, 30, NullLog.Instance);
        s.Start(Path.GetTempPath());
        Assert.IsTrue(WaitFor(() => s.IsIdle), s.Screen.GetVisibleText());
        Assert.IsNotNull(s.Cwd, "스크립트 파일 없이도 경로 보고가 와야 한다:\n" + s.Screen.GetVisibleText());

        Assert.AreEqual(before, Count(), "관리자 권한 앱이 사용자 쓰기 가능한 임시 폴더에 스크립트를 만들었다");
        Assert.IsFalse(s.Screen.GetVisibleText().Contains("CLIXML"), "화면에 CLIXML이 출력됨:\n" + s.Screen.GetVisibleText());
    }

    // ----- M6: cmd는 현재 폴더의 실행 파일을 먼저 실행하지 않는다 -----

    [TestMethod]
    public void CommandPrompt_DoesNotRunPlantedExecutableFromCurrentDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pd_plant_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        string marker = Path.Combine(dir, "marker.txt");
        File.WriteAllText(Path.Combine(dir, "pdplanted.bat"), $"@echo hijacked> \"{marker}\"\r\n");
        // 이 변수가 테스트를 실행한 환경에 이미 설정돼 있으면(일부 도구가 설정한다) 앱의 보호 없이도 통과해 버리므로, 이 테스트 동안은 지운다.
        const string Var = "NoDefaultCurrentDirectoryInExePath";
        string? inherited = Environment.GetEnvironmentVariable(Var);
        Environment.SetEnvironmentVariable(Var, null);
        try
        {
            using var s = new TerminalSession(100, 30, NullLog.Instance);
            s.Start(dir, TerminalShell.CommandPrompt);
            Assert.IsTrue(WaitFor(() => s.IsIdle), s.Screen.GetVisibleText());

            s.SendInput("pdplanted\r");
            Assert.IsTrue(WaitFor(() => !s.IsIdle));
            Assert.IsTrue(WaitFor(() => s.IsIdle), s.Screen.GetVisibleText());

            Assert.IsFalse(File.Exists(marker), "현재 폴더의 pdplanted.bat이 실행되었다:\n" + s.Screen.GetVisibleText());
            // 명시적으로 현재 폴더를 지정하면(.\) 여전히 실행된다 - 의도한 사용은 막지 않는다.
            s.SendInput(@".\pdplanted" + "\r");
            Assert.IsTrue(WaitFor(() => File.Exists(marker)), ".\\ 로 명시한 실행이 동작하지 않음:\n" + s.Screen.GetVisibleText());
        }
        finally
        {
            Environment.SetEnvironmentVariable(Var, inherited);
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}
