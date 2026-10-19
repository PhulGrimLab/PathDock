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


    public const string MainWindowTitle = "PathDock";
    public const string SplashWindowTitle = "PathDock Loading";

    /// <summary>앱 실행 파일 위치. 하네스 출력 폴더에서 위로 올라가며 src 아래의 PathDock.App 빌드 결과(플랫폼/대상 프레임워크 폴더 이름이 바뀌어도 찾도록 후보를 확인)를 찾는다.</summary>
    public static string AppExePath
    {
        get
        {
            string[] candidates =
            {
                @"bin\x64\Debug\net10.0-windows10.0.19041.0", @"bin\Debug\net10.0-windows10.0.19041.0",
                @"bin\x64\Debug\net10.0-windows", @"bin\Debug\net10.0-windows",
            };
            for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                foreach (string candidate in candidates)
                {
                    string exe = Path.Combine(dir.FullName, "PathDock.App", candidate, "PathDock.App.exe");
                    if (File.Exists(exe))
                    {
                        return exe;
                    }
                }
            }
            throw new FileNotFoundException("PathDock.App.exe(Debug 빌드)를 찾을 수 없습니다. 먼저 솔루션을 빌드하세요.");
        }
    }

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
                // 메인 창 제목은 "PathDock". 시작 중 로딩 팝업("PathDock Loading")이 먼저 뜨므로 Process.MainWindowHandle 대신 제목으로 찾는다.
                IntPtr hwnd = Native.FindWindowByTitle(p.Id, MainWindowTitle);
                if (hwnd == IntPtr.Zero)
                {
                    return false;
                }
                window = AutomationElement.FromHandle(hwnd);
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

    /// <summary>요소가 지금 UIA 트리에 있는지(기다리지 않는다). 숨겨진(Collapsed) 요소는 트리에 없다.</summary>
    public bool Exists(string automationId) =>
        Window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, automationId)) is not null;

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

    /// <summary>클립보드의 글자(PowerShell Get-Clipboard로 읽는다. 하네스는 STA가 아니므로 Clipboard 클래스를 쓰지 않는다).</summary>
    public static string ClipboardText() => RunPowerShell("Get-Clipboard -Raw").TrimEnd('\r', '\n');

    public static void SetClipboardText(string text) => RunPowerShell($"Set-Clipboard -Value '{text.Replace("'", "''")}'");

    private static string RunPowerShell(string command)
    {
        var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -Command \"{command}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        using Process p = Process.Start(psi) ?? throw new InvalidOperationException("powershell을 시작하지 못함");
        string output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(15000);
        return output;
    }

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

    /// <summary>콤보박스(AutomationId)의 index번째 항목을 선택한다(항목 이름이 언어/설정에 따라 바뀌는 콤보에 쓴다).</summary>
    public void SelectComboItemByIndex(string comboId, int index)
    {
        AutomationElement combo = Find(comboId);
        var expand = (ExpandCollapsePattern)combo.GetCurrentPattern(ExpandCollapsePattern.Pattern);
        expand.Expand();
        AutomationElement? item = null;
        WaitUntil(() =>
        {
            AutomationElementCollection items = combo.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
            item = items.Count > index ? items[index] : null;
            return item is not null;
        }, DefaultTimeoutMs, $"콤보박스 항목 없음: {comboId}[{index}]");
        ((SelectionItemPattern)item!.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        try { expand.Collapse(); } catch (InvalidOperationException) { }
    }

    /// <summary>슬라이더(AutomationId)의 값을 설정한다.</summary>
    public void SetSlider(string sliderId, double value)
    {
        ((RangeValuePattern)Find(sliderId).GetCurrentPattern(RangeValuePattern.Pattern)).SetValue(value);
    }

    /// <summary>입력칸(AutomationId)에 글자를 넣는다.</summary>
    public void SetText(string textBoxId, string value)
    {
        ((ValuePattern)Find(textBoxId).GetCurrentPattern(ValuePattern.Pattern)).SetValue(value);
    }

    /// <summary>화면의 어떤 글자 요소에 <paramref name="fragment"/>가 들어 있는지(알림 막대처럼 AutomationId로 못 찾는 문구 확인용).</summary>
    public bool AnyTextContains(string fragment)
    {
        foreach (AutomationElement e in Window.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text)))
        {
            if (e.Current.Name.Contains(fragment, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>체크박스(AutomationId)의 현재 선택 상태.</summary>
    public bool IsChecked(string checkBoxId) =>
        ((TogglePattern)Find(checkBoxId).GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState == ToggleState.On;

    public void Toggle(string checkBoxId)
    {
        ((TogglePattern)Find(checkBoxId).GetCurrentPattern(TogglePattern.Pattern)).Toggle();
    }

    /// <summary>제목 표시줄의 설정 버튼으로 설정 창을 연다(모달).</summary>
    public void OpenSettings() => Click("SettingsButton");

    /// <summary>설정 창을 닫는다.</summary>
    public void CloseSettings() => Click("SettingsCloseButton");

    /// <summary>앱이 종료된 뒤 데이터 폴더의 settings.json 내용.</summary>
    public string ReadSettingsJson() => File.ReadAllText(Path.Combine(DataDir, "settings.json"));

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

    /// <summary>목록 항목(이름)을 끌어 다른 요소(AutomationId) 한가운데에 놓는다. 앱 창이 앞에 없으면 실패로 본다(다른 창 보호).</summary>
    public void DragListItemTo(string itemName, string targetAutomationId)
    {
        AutomationElement item = FindListItem(itemName);
        AutomationElement target = Find(targetAutomationId);
        DragBetween(item, target);
    }

    /// <summary>목록 항목(이름)을 끌어 목록의 다른 항목(이름) 위에 놓는다.</summary>
    public void DragListItemToItem(string itemName, string targetItemName)
    {
        DragBetween(FindListItem(itemName), FindListItem(targetItemName));
    }

    /// <summary>목록 항목(이름)을 끌어 상위 열(AutomationId)의 항목(이름) 위에 놓는다.</summary>
    public void DragListItemToColumnItem(string itemName, string columnId, string columnItemName)
    {
        AutomationElement col = Find(columnId);
        AutomationElement? target = null;
        WaitUntil(() =>
        {
            target = col.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
                new PropertyCondition(AutomationElement.NameProperty, columnItemName)));
            return target is not null;
        }, DefaultTimeoutMs, $"열 항목 없음: {columnItemName}");
        DragBetween(FindListItem(itemName), target!);
    }

    private AutomationElement FindListItem(string name)
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
        return item!;
    }

    private void DragBetween(AutomationElement from, AutomationElement to)
    {
        Activate();
        System.Windows.Rect a = from.Current.BoundingRectangle, b = to.Current.BoundingRectangle;
        // 항목은 이름 칸(왼쪽)을 눌러야 하므로 가로는 왼쪽에서 60px, 세로는 가운데로 잡는다. 대상이 항목이면 같은 규칙, 큰 요소면 한가운데.
        int fromX = (int)(a.X + Math.Min(60, a.Width / 2)), fromY = (int)(a.Y + a.Height / 2);
        int toX = (int)(b.Height < 60 ? b.X + Math.Min(60, b.Width / 2) : b.X + b.Width / 2), toY = (int)(b.Y + b.Height / 2);
        if (!Native.Drag(new IntPtr(Window.Current.NativeWindowHandle), fromX, fromY, toX, toY))
        {
            throw new TestFailedException("끌어 놓는 동안 앱 창이 앞에 있지 않아 중단함(다른 창 보호)");
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

    /// <summary>메인 창에 닫기를 요청만 한다(종료를 기다리지 않는다). 종료 확인 창이 뜨는 경우를 시험할 때 쓴다.</summary>
    public void RequestClose()
    {
        IntPtr main = Native.FindWindowByTitle(_process.Id, MainWindowTitle);
        if (main == IntPtr.Zero || !Native.RequestClose(main))
        {
            _process.CloseMainWindow();
        }
    }

    public bool HasExited => _process.HasExited;

    public bool WaitForExit(int timeoutMs) => _process.WaitForExit(timeoutMs);

    /// <summary>창을 정상 종료(설정 저장 경로를 타도록 CloseMainWindow)하고 종료를 기다린다.</summary>
    public void CloseGracefully()
    {
        // 시작 직후에는 로딩 팝업이 떠 있을 수 있어 Process.CloseMainWindow 대신 메인 창을 제목으로 찾아 닫는다.
        IntPtr main = Native.FindWindowByTitle(_process.Id, MainWindowTitle);
        if (main == IntPtr.Zero || !Native.RequestClose(main))
        {
            _process.CloseMainWindow();
        }
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
