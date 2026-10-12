using System.Diagnostics;
using System.Windows.Automation;
using System.Windows.Forms;

namespace PathDock.UiAutomation.Tests;

/// <summary>
/// PathDock.App.exe를 실제 프로세스로 띄워 UI Automation으로 조작하는 드라이버(doc/03).
/// 데이터 폴더는 PATHDOCK_DATA_DIR로 임시 폴더에 격리한다. Lifetime: 테스트 하나. Dispose에서 프로세스를 종료한다.
/// CodeAtlas 교훈: Invoke는 스퓨리어스 예외가 날 수 있어 삼키고 호출 측이 상태 변화로 판정한다.
/// </summary>
public sealed class AppDriver : IDisposable
{
    private const int DefaultTimeoutMs = 20000;
    private readonly Process _process;

    public string DataDir { get; }
    public int ProcessId => _process.Id;
    public AutomationElement Window { get; }

    private AppDriver(Process process, string dataDir, AutomationElement window)
    {
        _process = process;
        DataDir = dataDir;
        Window = window;
    }

    /// <summary>앱 실행 파일 위치(하네스 출력 폴더 기준 상대 경로).</summary>
    public static string AppExePath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "PathDock.App", "bin", "Debug", "net10.0-windows", "PathDock.App.exe"));

    /// <summary>앱을 시작하고 메인 창과 첫 프롬프트(터미널)가 뜰 때까지 기다린다.</summary>
    public static AppDriver Launch(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        var psi = new ProcessStartInfo(AppExePath) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(AppExePath)! };
        psi.Environment["PATHDOCK_DATA_DIR"] = dataDir;
        Process p = Process.Start(psi) ?? throw new InvalidOperationException("앱을 시작하지 못함");

        // 창/프롬프트 대기 중 어떤 예외가 나도 이미 시작한 앱 프로세스를 남기지 않는다(호출 측 using이 아직 소유하기 전이므로 여기서 정리).
        try
        {
            AutomationElement? window = null;
            // 데스크톱 전체를 UIA로 훑으면 최상위 창이 많을 때 매우 느리고 불안정하므로, 프로세스의 메인 창 핸들에서 바로 요소를 얻는다.
            WaitUntil(() =>
            {
                p.Refresh();
                if (p.MainWindowHandle == IntPtr.Zero)
                {
                    return false;
                }
                window = AutomationElement.FromHandle(p.MainWindowHandle);
                return true;
            }, DefaultTimeoutMs, p.HasExited ? $"앱이 시작 직후 종료됨(코드 {p.ExitCode})" : "메인 창이 뜨지 않음");
            var driver = new AppDriver(p, dataDir, window!);
            driver.WaitUntil(() => driver.TerminalText().Contains("PS ", StringComparison.Ordinal), "터미널 프롬프트가 나오지 않음");
            return driver;
        }
        catch
        {
            try { p.Kill(true); } catch (InvalidOperationException) { }
            p.Dispose();
            throw;
        }
    }

    public static void WaitUntil(Func<bool> cond, int timeoutMs, string failMessage)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                if (cond())
                {
                    return;
                }
            }
            catch (ElementNotAvailableException)
            {
                // UI가 갱신되는 중: 재시도.
            }
            Thread.Sleep(100);
        }
        throw new TestFailedException(failMessage);
    }

    public void WaitUntil(Func<bool> cond, string failMessage, int timeoutMs = DefaultTimeoutMs) => WaitUntil(cond, timeoutMs, failMessage);

    /// <summary>앱 창을 포그라운드로 만든다(SendKeys가 엉뚱한 창으로 가는 것을 방지).</summary>
    public void Activate()
    {
        var hwnd = new IntPtr(Window.Current.NativeWindowHandle);
        WaitUntil(() => Native.BringToFront(hwnd), 5000, "앱 창을 포그라운드로 만들지 못함");
    }

    public AutomationElement Find(string automationId)
    {
        AutomationElement? e = null;
        WaitUntil(() =>
        {
            e = Window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
            return e is not null;
        }, DefaultTimeoutMs, $"요소를 찾지 못함: {automationId}");
        return e!;
    }

    public void Click(string automationId)
    {
        AutomationElement e = Find(automationId);
        try
        {
            ((InvokePattern)e.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        }
        catch (InvalidOperationException)
        {
            // 핸들러가 이미 처리한 뒤의 스퓨리어스 예외(doc/03).
        }
    }

    public string PathBarText() => ((ValuePattern)Find("PathBar").GetCurrentPattern(ValuePattern.Pattern)).Current.Value;

    public string TerminalText() => ((ValuePattern)Find("Terminal").GetCurrentPattern(ValuePattern.Pattern)).Current.Value;

    /// <summary>경로 표시줄에 값을 넣고 Enter로 이동한다.</summary>
    public void TypePath(string path)
    {
        Activate();
        AutomationElement bar = Find("PathBar");
        bar.SetFocus();
        ((ValuePattern)bar.GetCurrentPattern(ValuePattern.Pattern)).SetValue(path);
        SendKeys.SendWait("{ENTER}");
    }

    /// <summary>터미널에 포커스를 주고 텍스트를 입력한다(SendKeys 특수문자는 호출 측이 피한다).</summary>
    public void TypeInTerminal(string text)
    {
        Activate();
        Find("Terminal").SetFocus();
        Thread.Sleep(200);
        SendKeys.SendWait(text);
    }

    public void SelectSyncMode(string label)
    {
        AutomationElement combo = Find("SyncModeCombo");
        var expand = (ExpandCollapsePattern)combo.GetCurrentPattern(ExpandCollapsePattern.Pattern);
        expand.Expand();
        AutomationElement? item = null;
        WaitUntil(() =>
        {
            item = combo.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
                new PropertyCondition(AutomationElement.NameProperty, label)));
            return item is not null;
        }, DefaultTimeoutMs, $"동기화 모드 항목 없음: {label}");
        ((SelectionItemPattern)item!.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        try { expand.Collapse(); } catch (InvalidOperationException) { }
    }

    public string SelectedSyncModeName()
    {
        var sel = (SelectionPattern)Find("SyncModeCombo").GetCurrentPattern(SelectionPattern.Pattern);
        AutomationElement[] items = sel.Current.GetSelection();
        return items.Length > 0 ? items[0].Current.Name : string.Empty;
    }

    /// <summary>파일 목록에서 이름이 일치하는 항목을 선택하고 Enter를 눌러 연다.</summary>
    public void OpenListItem(string name)
    {
        Activate();
        AutomationElement list = Find("FileList");
        AutomationElement? item = null;
        WaitUntil(() =>
        {
            item = list.FindFirst(TreeScope.Descendants, new AndCondition(
                new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem)),
                new PropertyCondition(AutomationElement.NameProperty, name)));
            return item is not null;
        }, DefaultTimeoutMs, $"목록 항목 없음: {name}");
        ((SelectionItemPattern)item!.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        item.SetFocus();
        Thread.Sleep(150);
        SendKeys.SendWait("{ENTER}");
    }

    /// <summary>지정한 상위 열(AutomationId)에 이름이 일치하는 항목이 있는지.</summary>
    public bool ColumnContains(string columnId, string name)
    {
        AutomationElement col = Find(columnId);
        return col.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, name)) is not null;
    }

    /// <summary>상위 열의 항목을 선택(클릭과 동일하게 SelectionChanged 발생)한다.</summary>
    public void ClickColumnItem(string columnId, string name)
    {
        AutomationElement col = Find(columnId);
        AutomationElement? item = null;
        WaitUntil(() =>
        {
            item = col.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
                new PropertyCondition(AutomationElement.NameProperty, name)));
            return item is not null;
        }, DefaultTimeoutMs, $"열 항목 없음: {name}");
        ((SelectionItemPattern)item!.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
    }

    /// <summary>파일 목록 안에 해당 텍스트(크기 셀 등)가 표시되는지.</summary>
    public bool ListCellTextExists(string text)
    {
        AutomationElement list = Find("FileList");
        return list.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, text)) is not null;
    }

    /// <summary>파일 목록 항목 이름을 화면 순서대로 돌려준다.</summary>
    public List<string> ListOrder()
    {
        AutomationElement list = Find("FileList");
        var names = new List<string>();
        foreach (AutomationElement e in list.FindAll(TreeScope.Children, new OrCondition(
                     new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem),
                     new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem))))
        {
            names.Add(e.Current.Name);
        }
        return names;
    }

    /// <summary>이름이 접두사로 시작하는 열 머리글을 누른다(정렬 표시 ▲▼ 때문에 접두사로 찾는다).</summary>
    public void ClickHeader(string namePrefix)
    {
        AutomationElement list = Find("FileList");
        AutomationElement? header = null;
        WaitUntil(() =>
        {
            foreach (AutomationElement h in list.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.HeaderItem)))
            {
                if (h.Current.Name.StartsWith(namePrefix, StringComparison.Ordinal))
                {
                    header = h;
                    return true;
                }
            }
            return false;
        }, DefaultTimeoutMs, $"열 머리글 없음: {namePrefix}");
        try
        {
            ((InvokePattern)header!.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        }
        catch (InvalidOperationException)
        {
            // 클릭 처리 후 발생하는 스퓨리어스 예외.
        }
    }

    public void SelectListItem(string name)
    {
        AutomationElement list = Find("FileList");
        AutomationElement? item = null;
        WaitUntil(() =>
        {
            item = list.FindFirst(TreeScope.Descendants, new AndCondition(
                new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem)),
                new PropertyCondition(AutomationElement.NameProperty, name)));
            return item is not null;
        }, DefaultTimeoutMs, $"목록 항목 없음: {name}");
        ((SelectionItemPattern)item!.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        Activate();
        item.SetFocus();
        Thread.Sleep(150);
    }

    /// <summary>키 입력을 보낸다(예: "{F2}", "{DELETE}", "^+n").</summary>
    public void PressKeys(string keys)
    {
        Activate();
        SendKeys.SendWait(keys);
    }

    /// <summary>앱 프로세스의 최상위 창(입력창/메시지 상자)을 제목으로 찾는다.</summary>
    public AutomationElement FindDialog(string title)
    {
        AutomationElement? dlg = null;
        WaitUntil(() =>
        {
            // 소유 창(모달 대화상자/메시지 상자)은 UIA에서 메인 창의 자식으로 노출된다(실측). 데스크톱 직속도 함께 확인한다.
            var nameCond = new PropertyCondition(AutomationElement.NameProperty, title);
            dlg = Window.FindFirst(TreeScope.Children, new AndCondition(nameCond, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window)))
                  ?? AutomationElement.RootElement.FindFirst(TreeScope.Children, new AndCondition(new PropertyCondition(AutomationElement.ProcessIdProperty, _process.Id), nameCond));
            return dlg is not null;
        }, DefaultTimeoutMs, $"대화상자가 뜨지 않음: {title}");
        return dlg!;
    }

    /// <summary>입력창에 값을 넣고 확인을 누른다.</summary>
    public void AnswerInputDialog(string title, string value)
    {
        AutomationElement dlg = FindDialog(title);
        AutomationElement box = dlg.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "InputBox"));
        ((ValuePattern)box.GetCurrentPattern(ValuePattern.Pattern)).SetValue(value);
        AutomationElement ok = dlg.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "InputOk"));
        ((InvokePattern)ok.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
    }

    /// <summary>삭제 확인 메시지 상자에서 예/아니오를 누른다.</summary>
    public void AnswerConfirm(string title, bool yes)
    {
        AutomationElement dlg = FindDialog(title);
        AutomationElement? button = null;
        foreach (AutomationElement b in dlg.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)))
        {
            string n = b.Current.Name;
            bool isYes = n.StartsWith("예", StringComparison.Ordinal) || n.StartsWith("Yes", StringComparison.OrdinalIgnoreCase);
            bool isNo = n.StartsWith("아니", StringComparison.Ordinal) || n.StartsWith("No", StringComparison.OrdinalIgnoreCase);
            if ((yes && isYes) || (!yes && isNo))
            {
                button = b;
            }
        }
        if (button is null)
        {
            throw new TestFailedException("확인 창에서 예/아니오 버튼을 찾지 못함");
        }
        try
        {
            ((InvokePattern)button.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        }
        catch (InvalidOperationException)
        {
            // 대화상자가 닫히며 발생하는 예외.
        }
    }

    public bool ListContains(string name)
    {
        AutomationElement list = Find("FileList");
        return list.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, name)) is not null;
    }

    /// <summary>창을 정상 종료(설정 저장 경로를 타도록 CloseMainWindow)하고 종료를 기다린다.</summary>
    public void CloseGracefully()
    {
        _process.CloseMainWindow();
        if (!_process.WaitForExit(15000))
        {
            throw new TestFailedException("앱이 정상 종료하지 않음");
        }
    }

    public void Dispose()
    {
        if (!_process.HasExited)
        {
            try { _process.Kill(true); } catch (InvalidOperationException) { }
        }
        _process.Dispose();
    }
}

/// <summary>하네스 테스트 실패(메시지만 표시).</summary>
public sealed class TestFailedException : Exception
{
    public TestFailedException(string message) : base(message) { }
}
