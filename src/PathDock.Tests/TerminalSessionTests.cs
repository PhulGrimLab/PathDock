using PathDock.Core.Logging;
using PathDock.Core.Terminal;

namespace PathDock.Tests;

/// <summary>실제 PowerShell/ConPTY를 띄우는 통합 테스트(doc/02 3절의 OSC 통과 검증).</summary>
[TestClass]
public class TerminalSessionTests
{
    private static bool WaitFor(Func<bool> cond, int timeoutMs = 20000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
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

    [TestMethod]
    public void Start_ReportsPromptAndInitialCwd()
    {
        string dir = Path.GetTempPath().TrimEnd('\\');
        using var s = new TerminalSession(100, 30, NullLog.Instance);
        s.Start(dir);
        Assert.IsTrue(WaitFor(() => s.IsIdle), "프롬프트(OSC 133)를 받지 못함:\n" + s.Screen.GetVisibleText());
        Assert.IsNotNull(s.Cwd, "cwd(OSC 9;9)를 받지 못함:\n" + s.Screen.GetVisibleText());
        Assert.AreEqual(Path.GetFullPath(dir).TrimEnd('\\'), s.Cwd!.TrimEnd('\\'), true);
    }

    [TestMethod]
    public void RequestChangeDirectory_WhenIdle_ChangesCwd_AndFlagsAsRequested()
    {
        string root = Path.Combine(Path.GetTempPath(), "pd_t1 '공백'");
        Directory.CreateDirectory(root);
        using var s = new TerminalSession(100, 30, NullLog.Instance);
        var events = new List<(string Path, bool FromRequest)>();
        s.CwdChanged += (p, r) => { lock (events) { events.Add((p, r)); } };
        s.Start(Path.GetTempPath());
        Assert.IsTrue(WaitFor(() => s.IsIdle));

        Assert.IsTrue(s.RequestChangeDirectory(root));
        Assert.IsTrue(WaitFor(() => { lock (events) { return events.Any(e => e.Path.EndsWith("pd_t1 '공백'")); } }),
            "cd 후 cwd 마커 없음:\n" + s.Screen.GetVisibleText());
        lock (events)
        {
            Assert.IsTrue(events.Last().FromRequest);
        }
    }

    [TestMethod]
    public void UserCd_IsReportedAsUserChange_AndBusyPendingAppliedAfterPrompt()
    {
        string a = Path.Combine(Path.GetTempPath(), "pd_a");
        string b = Path.Combine(Path.GetTempPath(), "pd_b");
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);
        using var s = new TerminalSession(100, 30, NullLog.Instance);
        var events = new List<(string Path, bool FromRequest)>();
        s.CwdChanged += (p, r) => { lock (events) { events.Add((p, r)); } };
        s.Start(Path.GetTempPath());
        Assert.IsTrue(WaitFor(() => s.IsIdle));

        // 사용자가 직접 cd -> FromRequest=false
        s.SendInput($"cd '{a}'\r");
        Assert.IsTrue(WaitFor(() => { lock (events) { return events.Any(e => e.Path.EndsWith("pd_a") && !e.FromRequest); } }), s.Screen.GetVisibleText());

        // 실행 중(Start-Sleep) 요청은 보류되고, 끝난 뒤 반영된다.
        s.SendInput("Start-Sleep -Seconds 2\r");
        Assert.IsTrue(WaitFor(() => !s.IsIdle));
        Assert.IsFalse(s.RequestChangeDirectory(b));
        Assert.AreEqual(b, s.PendingPath);
        Assert.IsTrue(WaitFor(() => { lock (events) { return events.Any(e => e.Path.EndsWith("pd_b") && e.FromRequest); } }), s.Screen.GetVisibleText());
        Assert.IsNull(s.PendingPath);
    }

    [TestMethod]
    public void CommandPrompt_ReportsCwd_AndFollowsRequestedAndUserChanges()
    {
        string a = Path.Combine(Path.GetTempPath(), "pd_c 공백");
        string b = Path.Combine(Path.GetTempPath(), "pd_d");
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);
        using var s = new TerminalSession(100, 30, NullLog.Instance);
        var events = new List<(string Path, bool FromRequest)>();
        s.CwdChanged += (p, r) => { lock (events) { events.Add((p, r)); } };
        s.Start(Path.GetTempPath(), TerminalShell.CommandPrompt);
        Assert.IsTrue(WaitFor(() => s.IsIdle), s.Screen.GetVisibleText());
        Assert.IsNotNull(s.Cwd, s.Screen.GetVisibleText());

        Assert.IsTrue(s.RequestChangeDirectory(a));
        Assert.IsTrue(WaitFor(() => { lock (events) { return events.Any(e => e.Path.EndsWith("pd_c 공백") && e.FromRequest); } }), s.Screen.GetVisibleText());

        // 같은 경로에서 명령만 실행해도 프롬프트(대기) 신호가 다시 와야 한다.
        s.SendInput("echo hi\r");
        Assert.IsTrue(WaitFor(() => !s.IsIdle));
        Assert.IsTrue(WaitFor(() => s.IsIdle), s.Screen.GetVisibleText());

        s.SendInput($"cd /d \"{b}\"\r");
        Assert.IsTrue(WaitFor(() => { lock (events) { return events.Any(e => e.Path.EndsWith("pd_d") && !e.FromRequest); } }), s.Screen.GetVisibleText());
    }
}
