using System.Windows.Automation;
using System.Text.Json;
using PathDock.UiAutomation.Tests;

// PathDock UI Automation 하네스(doc/03). 사용법: PathDock.UiAutomation.Tests.exe [테스트이름...]
// 실행 중에는 SendKeys를 쓰므로 마우스/키보드를 건드리지 않는다.

Console.OutputEncoding = System.Text.Encoding.UTF8;

string root = Path.Combine("C:\\", "pd_ui_tests", Guid.NewGuid().ToString("N")[..6]);
Directory.CreateDirectory(root);

string MakeDir(string name)
{
    string p = Path.Combine(root, name);
    Directory.CreateDirectory(p);
    return p;
}

string NewDataDir(string[]? favorites = null, string mode = "Bidirectional", string? lastPath = null)
{
    string d = MakeDir("data_" + Guid.NewGuid().ToString("N")[..4]);
    var s = new Dictionary<string, object?>
    {
        ["SyncMode"] = mode,
        ["LastPath"] = lastPath ?? root,
        ["Favorites"] = (favorites ?? Array.Empty<string>()).Select(f => new { Name = Path.GetFileName(f), Path = f }).ToArray(),
    };
    File.WriteAllText(Path.Combine(d, "settings.json"), JsonSerializer.Serialize(s));
    return d;
}

var tests = new List<(string Name, Action Body)>
{
    ("Favorites_Click_NavigatesExplorer", () =>
    {
        string a = MakeDir("pdA_fav"), b = MakeDir("pdB_fav");
        using var app = AppDriver.Launch(NewDataDir(new[] { a, b }, "Off"));
        app.Click("Favorite_1");
        app.WaitUntil(() => app.PathBarText() == a, $"즐겨찾기 1 이동 실패: '{app.PathBarText()}'");
        app.Click("Favorite_2");
        app.WaitUntil(() => app.PathBarText() == b, $"즐겨찾기 2 이동 실패: '{app.PathBarText()}'");
    }),

    ("Favorites_EmptySlotClick_RegistersCurrentFolder", () =>
    {
        string a = MakeDir("pdA_reg");
        using var app = AppDriver.Launch(NewDataDir(null, "Off", a));
        app.WaitUntil(() => app.PathBarText() == a, "시작 경로가 마지막 경로가 아님");
        app.Click("Favorite_3");
        app.WaitUntil(() => app.Find("Favorite_3").Current.Name.Contains("pdA_reg"), $"빈 칸에 현재 폴더가 등록되지 않음: name='{app.Find("Favorite_3").Current.Name}' path='{app.PathBarText()}'");
    }),

    ("Drives_ShowSystemDriveUsage_AndClickNavigatesToRoot", () =>
    {
        string a = MakeDir("pdA_drv");
        using var app = AppDriver.Launch(NewDataDir(null, "Off", a));
        string sysLetter = Path.GetPathRoot(Environment.SystemDirectory)!.Substring(0, 1);
        string name = app.Find("Drive_" + sysLetter).Current.Name;
        if (!name.StartsWith(sysLetter + ":"))
        {
            throw new TestFailedException($"드라이브 이름이 이상함: '{name}'");
        }
        app.Click("Drive_" + sysLetter);
        string root = sysLetter + @":\";
        app.WaitUntil(() => app.PathBarText() == root, $"드라이브 클릭 후 이동 실패: '{app.PathBarText()}'");
    }),

    ("Explorer_OpenFolderWithEnter_UpdatesPath", () =>
    {
        string a = MakeDir("pdA_open");
        MakeDir("pdA_open\\child_dir");
        using var app = AppDriver.Launch(NewDataDir(null, "Off", a));
        app.OpenListItem("child_dir");
        string expected = Path.Combine(a, "child_dir");
        app.WaitUntil(() => app.PathBarText() == expected, $"하위 폴더 진입 실패: '{app.PathBarText()}'");
    }),

    ("Explorer_OpenFile_LaunchesAssociatedProgram_InItsFolder", () =>
    {
        string a = MakeDir("pdA_launch");
        // 열리면 자기 폴더(작업 폴더)에 marker.txt를 만드는 스크립트: 실행 + 작업 폴더 지정을 함께 검증한다.
        File.WriteAllText(Path.Combine(a, "run_me.cmd"), "@echo off\r\necho ok> marker.txt\r\n");
        using var app = AppDriver.Launch(NewDataDir(null, "Off", a));
        app.OpenListItem("run_me.cmd");
        string marker = Path.Combine(a, "marker.txt");
        app.WaitUntil(() => File.Exists(marker), "파일을 열었는데 실행되지 않음(또는 작업 폴더가 다름)");
    }),

    ("Explorer_ShowsAncestorColumns_AndFolderSizes", () =>
    {
        string a = MakeDir("pdA_cols");
        string mid = Path.Combine(a, "mid"), cur = Path.Combine(mid, "cur");
        Directory.CreateDirectory(Path.Combine(cur, "sub_big"));
        Directory.CreateDirectory(Path.Combine(a, "mid_sibling"));
        File.WriteAllBytes(Path.Combine(cur, "sub_big", "data.bin"), new byte[3 * 1024]);
        File.WriteAllBytes(Path.Combine(cur, "top.bin"), new byte[10]);
        using var app = AppDriver.Launch(NewDataDir(null, "Off", cur));
        app.WaitUntil(() => app.PathBarText() == cur, "시작 경로 불일치");
        // 부모 열: mid의 하위 폴더 = cur / 조부모 열: a의 하위 폴더 = mid, mid_sibling
        app.WaitUntil(() => app.ColumnContains("AncestorColumn_1", "cur"), "부모 열에 현재 폴더가 없음");
        app.WaitUntil(() => app.ColumnContains("AncestorColumn_2", "mid") && app.ColumnContains("AncestorColumn_2", "mid_sibling"), "조부모 열 내용이 이상함");
        // 폴더 크기: sub_big = 3 KB, 요약에 폴더/파일 개수
        app.WaitUntil(() => app.ListCellTextExists("3 KB"), "하위 폴더 크기(3 KB)가 표시되지 않음");
        app.WaitUntil(() => app.Find("FolderSummary").Current.Name.Contains("폴더 1개") && app.Find("FolderSummary").Current.Name.Contains("파일 1개"), $"요약 이상: '{app.Find("FolderSummary").Current.Name}'");
        // 부모 열에서 조부모 열의 다른 폴더를 눌러 이동
        app.ClickColumnItem("AncestorColumn_2", "mid_sibling");
        string sib = Path.Combine(a, "mid_sibling");
        app.WaitUntil(() => app.PathBarText() == sib, $"상위 열 클릭 이동 실패: '{app.PathBarText()}'");
    }),

    ("StatusBar_ShowsCpuGpuMemoryMeters", () =>
    {
        string a = MakeDir("pdA_usage");
        using var app = AppDriver.Launch(NewDataDir(null, "Off", a));
        // 첫 측정은 약 1초 뒤 도착한다. 각 미터의 이름: "CPU 12%", "GPU 3%"(또는 N/A), "메모리 8 GB / 16 GB".
        app.WaitUntil(() =>
            System.Text.RegularExpressions.Regex.IsMatch(app.Find("Meter_Cpu").Current.Name, @"^CPU \d+%$")
            && System.Text.RegularExpressions.Regex.IsMatch(app.Find("Meter_Gpu").Current.Name, @"^GPU (\d+%|N/A)$")
            && System.Text.RegularExpressions.Regex.IsMatch(app.Find("Meter_Memory").Current.Name, @"^메모리 .+ / .+$"),
            $"미터가 갱신되지 않음: '{app.Find("Meter_Cpu").Current.Name}' '{app.Find("Meter_Gpu").Current.Name}' '{app.Find("Meter_Memory").Current.Name}'");
    }),

    ("FolderSizeCache_IsPersistedOnClose", () =>
    {
        string a = MakeDir("pdA_cache");
        Directory.CreateDirectory(Path.Combine(a, "sub_cached"));
        File.WriteAllBytes(Path.Combine(a, "sub_cached", "d.bin"), new byte[2048]);
        string data = NewDataDir(null, "Off", a);
        using (var app = AppDriver.Launch(data))
        {
            app.WaitUntil(() => app.ListCellTextExists("2 KB"), "폴더 크기(2 KB)가 계산되지 않음");
            app.CloseGracefully();
        }
        string cacheFile = Path.Combine(data, "folder-size-cache.json");
        if (!File.Exists(cacheFile) || !File.ReadAllText(cacheFile).Contains("SUB_CACHED", StringComparison.Ordinal))
        {
            throw new TestFailedException("캐시 파일이 저장되지 않았거나 항목이 없음");
        }
        // 재시작 후에도 같은 크기가 표시된다(캐시 또는 재계산 어느 쪽이든 결과는 동일해야 함).
        using var app2 = AppDriver.Launch(data);
        app2.WaitUntil(() => app2.ListCellTextExists("2 KB"), "재시작 후 폴더 크기가 표시되지 않음");
    }),

    ("Explorer_SortByHeader_Size_TogglesDirection", () =>
    {
        string a = MakeDir("pdA_sort");
        File.WriteAllBytes(Path.Combine(a, "f_small"), new byte[10]);
        File.WriteAllBytes(Path.Combine(a, "f_big"), new byte[5000]);
        File.WriteAllBytes(Path.Combine(a, "f_mid"), new byte[300]);
        using var app = AppDriver.Launch(NewDataDir(null, "Off", a));
        app.WaitUntil(() => app.ListOrder().Count == 3, "목록 항목 3개가 아님");
        app.ClickHeader("크기");
        app.WaitUntil(() => string.Join(",", app.ListOrder()) == "f_small,f_mid,f_big", $"크기 오름차순 아님: {string.Join(",", app.ListOrder())}");
        app.ClickHeader("크기");
        app.WaitUntil(() => string.Join(",", app.ListOrder()) == "f_big,f_mid,f_small", $"크기 내림차순 아님: {string.Join(",", app.ListOrder())}");
        app.ClickHeader("이름");
        app.WaitUntil(() => string.Join(",", app.ListOrder()) == "f_big,f_mid,f_small", $"이름 오름차순 아님: {string.Join(",", app.ListOrder())}");
    }),

    ("Explorer_Rename_F2_RenamesAndSelects", () =>
    {
        string a = MakeDir("pdA_ren");
        File.WriteAllText(Path.Combine(a, "old_name.txt"), "x");
        using var app = AppDriver.Launch(NewDataDir(null, "Off", a));
        app.SelectListItem("old_name.txt");
        app.PressKeys("{F2}");
        app.AnswerInputDialog("이름 변경", "new_name.txt");
        app.WaitUntil(() => File.Exists(Path.Combine(a, "new_name.txt")) && !File.Exists(Path.Combine(a, "old_name.txt")), "디스크에서 이름이 바뀌지 않음");
        app.WaitUntil(() => app.ListContains("new_name.txt"), "목록에 새 이름이 보이지 않음");
    }),

    ("Explorer_NewFolder_CtrlShiftN_CreatesFolder", () =>
    {
        string a = MakeDir("pdA_new");
        using var app = AppDriver.Launch(NewDataDir(null, "Off", a));
        app.Activate();
        app.Find("FileList").SetFocus(); // 시작 직후 포커스는 터미널에 있으므로 목록에 먼저 준다.
        app.PressKeys("^+n");
        app.AnswerInputDialog("새 폴더", "made_by_test");
        app.WaitUntil(() => Directory.Exists(Path.Combine(a, "made_by_test")), "폴더가 만들어지지 않음");
        app.WaitUntil(() => app.ListContains("made_by_test"), "목록에 새 폴더가 보이지 않음");
    }),

    ("Explorer_Delete_AsksConfirm_NoKeeps_YesRecycles", () =>
    {
        string a = MakeDir("pdA_del");
        string f = Path.Combine(a, "victim_pd.txt");
        File.WriteAllText(f, "x");
        using var app = AppDriver.Launch(NewDataDir(null, "Off", a));
        app.SelectListItem("victim_pd.txt");
        app.PressKeys("{DELETE}");
        app.AnswerConfirm("삭제 확인", yes: false);
        Thread.Sleep(800);
        if (!File.Exists(f))
        {
            throw new TestFailedException("아니오를 눌렀는데 파일이 삭제됨");
        }
        app.SelectListItem("victim_pd.txt");
        app.PressKeys("{DELETE}");
        app.AnswerConfirm("삭제 확인", yes: true);
        app.WaitUntil(() => !File.Exists(f), "예를 눌렀는데 파일이 삭제되지 않음");
        app.WaitUntil(() => !app.ListContains("victim_pd.txt"), "삭제 후 목록에서 사라지지 않음");
    }),

    ("About_LogoClick_ShowsInfoWindow_AndCloses", () =>
    {
        string a = MakeDir("pdA_about");
        using var app = AppDriver.Launch(NewDataDir(null, "Off", a));
        app.Click("AboutButton");
        AutomationElement dlg = app.FindDialog("PathDock 정보");
        string version = dlg.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "AboutVersionTextBlock")).Current.Name;
        if (!version.StartsWith("버전 ", StringComparison.Ordinal))
        {
            throw new TestFailedException($"버전 문구가 이상함: '{version}'");
        }
        string env = dlg.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "AboutEnvironmentTextBlock")).Current.Name;
        if (!env.Contains(".NET", StringComparison.Ordinal))
        {
            throw new TestFailedException($"실행 환경 문구가 이상함: '{env}'");
        }
        AutomationElement close = dlg.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "AboutCloseButton"));
        ((InvokePattern)close.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        app.WaitUntil(() => app.Window.FindFirst(TreeScope.Children, new AndCondition(
            new PropertyCondition(AutomationElement.NameProperty, "PathDock 정보"),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window))) is null, "정보 창이 닫히지 않음");
    }),

    ("Theme_Toggle_SwitchesAndPersists", () =>
    {
        string a = MakeDir("pdA_theme");
        string data = NewDataDir(null, "Off", a);
        using (var app = AppDriver.Launch(data))
        {
            app.Click("ThemeButton");
            Thread.Sleep(800);
            app.CloseGracefully();
        }
        string json = File.ReadAllText(Path.Combine(data, "settings.json"));
        if (!json.Contains("\"Theme\": \"Light\"", StringComparison.Ordinal))
        {
            throw new TestFailedException("라이트 테마가 설정에 저장되지 않음: " + json);
        }
    }),

    ("PathBar_InvalidPath_ShowsInfoBar_AndKeepsPath", () =>
    {
        string a = MakeDir("pdA_bad");
        using var app = AppDriver.Launch(NewDataDir(null, "Off", a));
        string bad = Path.Combine(a, "no_such_folder");
        app.TypePath(bad);
        // 4553801 이후: 오류 시 주소창은 복원하지 않고 입력한 경로를 그대로 둔다(고쳐 쓸 수 있도록).
        Thread.Sleep(1500);
        if (app.PathBarText() != bad) throw new TestFailedException($"잘못된 경로 입력 후 주소창이 입력값을 유지하지 않음: '{app.PathBarText()}'");
    }),

    ("SyncMode_ExplorerToTerminal_ChangesTerminalPrompt", () =>
    {
        string a = MakeDir("pdA_e2t"), b = MakeDir("pdB_e2t");
        using var app = AppDriver.Launch(NewDataDir(new[] { a, b }, "Bidirectional", a));
        app.Click("Favorite_2");
        app.WaitUntil(() => app.TerminalText().Contains("pdB_e2t>"), $"터미널이 따라가지 않음:\n{app.TerminalText()}", 25000);
    }),

    ("SyncMode_TerminalToExplorer_UpdatesPathBar", () =>
    {
        string a = MakeDir("pdA_t2e"), b = MakeDir("pdB_t2e");
        using var app = AppDriver.Launch(NewDataDir(null, "Bidirectional", a));
        app.TypeInTerminal("cd " + b + "{ENTER}");
        app.WaitUntil(() => app.PathBarText() == b, $"탐색기가 따라가지 않음: '{app.PathBarText()}'\n{app.TerminalText()}", 25000);
    }),

    ("SyncMode_Off_DoesNotPropagate", () =>
    {
        string a = MakeDir("pdA_off"), b = MakeDir("pdB_off");
        using var app = AppDriver.Launch(NewDataDir(new[] { a, b }, "Bidirectional", a));
        app.SelectSyncMode("동기화 끄기");
        app.Click("Favorite_2");
        app.WaitUntil(() => app.PathBarText() == b, "탐색기 이동 실패");
        Thread.Sleep(3000);
        if (app.TerminalText().Contains("pdB_off>"))
        {
            throw new TestFailedException("동기화 끄기인데 터미널이 이동함:\n" + app.TerminalText());
        }
    }),

    ("Settings_SyncMode_PersistsAcrossRestart", () =>
    {
        string a = MakeDir("pdA_persist");
        string data = NewDataDir(null, "Bidirectional", a);
        using (var app = AppDriver.Launch(data))
        {
            app.SelectSyncMode("터미널 → 탐색기");
            app.CloseGracefully();
        }
        using var app2 = AppDriver.Launch(data);
        app2.WaitUntil(() => app2.SelectedSyncModeName() == "터미널 → 탐색기", $"모드 복원 실패: '{app2.SelectedSyncModeName()}'");
    }),
};

if (args.Length > 0 && args[0] == "--dump")
{
    Dump.Run(NewDataDir(null, "Off", MakeDir("pdA_dump")), args.Length > 1 ? args[1] : null);
    return 0;
}

if (args.Length > 0 && args[0] == "--probe-f2")
{
    Probe.RunF2(NewDataDir(null, "Off", MakeDir("pdA_probe")), MakeDir("pdA_probe"), args.Length > 1 ? args[1] : "{F2}");
    return 0;
}

if (args.Length > 1 && args[0] == "--shot-about")
{
    Probe.ShotAbout(NewDataDir(null, "Off", MakeDir("pdA_shot")), args[1]);
    return 0;
}

string[] wanted = args;
int failed = 0, ran = 0;
foreach (var (name, body) in tests)
{
    if (wanted.Length > 0 && !wanted.Contains(name))
    {
        continue;
    }
    ran++;
    var sw = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        body();
        Console.WriteLine($"PASS  {name} ({sw.Elapsed.TotalSeconds:0.0}s)");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"FAIL  {name} ({sw.Elapsed.TotalSeconds:0.0}s)\n      {ex.GetType().Name}: {ex.Message.Replace("\n", "\n      ")}");
    }
}

try { Directory.Delete(root, true); } catch (IOException) { }
Console.WriteLine($"\n{ran - failed}/{ran} 통과");
return failed == 0 ? 0 : 1;
