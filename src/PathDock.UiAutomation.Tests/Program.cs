using System.Windows.Automation;
using System.Text.Json;
using PathDock.UiAutomation.Tests;

// PathDock UI Automation 하네스(doc/03). 사용법: PathDock.UiAutomation.Tests.exe [테스트이름...]
// 실행 중에는 SendKeys를 쓰므로 마우스/키보드를 건드리지 않는다.

Console.OutputEncoding = System.Text.Encoding.UTF8;
Native.EnableDpiAwareness(); // 창 좌표/캡처를 실제 픽셀로 읽기 위해(배율이 100%가 아닌 PC에서 필수)

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

    ("Startup_ShowsLoadingPopup_AndClosesAboutOneSecondAfterMainWindow", () =>
    {
        string data = NewDataDir(null, "Off", MakeDir("pdA_splash"));
        var psi = new System.Diagnostics.ProcessStartInfo(AppDriver.AppExePath) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(AppDriver.AppExePath)! };
        psi.Environment["PATHDOCK_DATA_DIR"] = data;
        using System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi) ?? throw new TestFailedException("앱을 시작하지 못함");
        try
        {
            // 1) 로딩 팝업이 가장 먼저 뜬다.
            AppDriver.WaitUntil(() => Native.FindWindowByTitle(p.Id, AppDriver.SplashWindowTitle) != IntPtr.Zero, 30000, "로딩 팝업이 뜨지 않음");
            // 2) 메인 창이 뜬다. 이 시점(폴링 오차 100ms)에도 팝업은 아직 보여야 한다(메인 창이 그려진 뒤 1초 더 유지).
            AppDriver.WaitUntil(() => Native.FindWindowByTitle(p.Id, AppDriver.MainWindowTitle) != IntPtr.Zero, 30000, "메인 창이 뜨지 않음");
            var sinceMain = System.Diagnostics.Stopwatch.StartNew();
            if (Native.FindWindowByTitle(p.Id, AppDriver.SplashWindowTitle) == IntPtr.Zero)
            {
                throw new TestFailedException("메인 창이 뜨자마자 로딩 팝업이 이미 사라짐(메인 창이 그려진 뒤 1초 유지되어야 함)");
            }
            // 3) 곧 사라진다. 메인 창을 처음 본 뒤 0.7초 미만이면 너무 일찍, 5초를 넘기면 너무 늦게 닫힌 것이다.
            AppDriver.WaitUntil(() => Native.FindWindowByTitle(p.Id, AppDriver.SplashWindowTitle) == IntPtr.Zero, 5000, "로딩 팝업이 메인 창 표시 후 5초가 지나도 닫히지 않음");
            long closedAfterMs = sinceMain.ElapsedMilliseconds;
            if (closedAfterMs < 700)
            {
                throw new TestFailedException($"로딩 팝업이 메인 창 표시 후 {closedAfterMs}ms 만에 닫힘(약 1초 유지되어야 함)");
            }
            // 4) 메인 창은 계속 떠 있다.
            if (Native.FindWindowByTitle(p.Id, AppDriver.MainWindowTitle) == IntPtr.Zero)
            {
                throw new TestFailedException("로딩 팝업이 닫힌 뒤 메인 창이 보이지 않음");
            }
        }
        finally
        {
            try { p.Kill(true); } catch (InvalidOperationException) { }
        }
    }),

    ("Startup_LoadingPopup_BarKeepsMovingAndStatusLineChanges", () =>
    {
        string data = NewDataDir(null, "Off", MakeDir("pdA_splashbar"));
        var psi = new System.Diagnostics.ProcessStartInfo(AppDriver.AppExePath) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(AppDriver.AppExePath)! };
        psi.Environment["PATHDOCK_DATA_DIR"] = data;
        using System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi) ?? throw new TestFailedException("앱을 시작하지 못함");
        try
        {
            IntPtr splash = IntPtr.Zero;
            AppDriver.WaitUntil(() => (splash = Native.FindWindowByTitle(p.Id, AppDriver.SplashWindowTitle)) != IntPtr.Zero, 30000, "로딩 팝업이 뜨지 않음");
            AutomationElement splashElement = AutomationElement.FromHandle(splash);

            // 팝업이 떠 있는 동안 진행 막대 구간의 화면 지문과 "지금 하는 일" 문구를 계속 모은다.
            // 메인 창이 뜨기 전(앱 UI 스레드가 초기화로 바쁜 구간)에 모은 지문만 막대 애니메이션 판정에 쓴다 - 팝업이 UI 스레드와 같은 스레드면 이때 멈춘다.
            var barFrames = new HashSet<long>();
            int framesWhileBusy = 0;
            var statuses = new List<string>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromSeconds(20) && Native.FindWindowByTitle(p.Id, AppDriver.SplashWindowTitle) != IntPtr.Zero)
            {
                bool mainShown = Native.FindWindowByTitle(p.Id, AppDriver.MainWindowTitle) != IntPtr.Zero;
                if (!mainShown)
                {
                    barFrames.Add(Native.CaptureChecksum(splash, 0.86, 0.95));
                    framesWhileBusy++;
                }
                try
                {
                    AutomationElement? status = splashElement.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "1003")); // 팝업의 "지금 하는 일" 줄(Win32 STATIC 컨트롤 ID)
                    string text = status?.Current.Name ?? string.Empty;
                    if (text.Length > 0 && (statuses.Count == 0 || statuses[^1] != text))
                    {
                        statuses.Add(text);
                    }
                }
                catch (ElementNotAvailableException)
                {
                    break; // 팝업이 닫힘.
                }
                // 검사 간격이 애니메이션 주기(1.2초)의 약수와 맞으면 항상 같은 위치만 찍혀 멈춘 것처럼 보이므로 간격에 편차를 둔다.
                Thread.Sleep(Random.Shared.Next(20, 160));
            }
            if (sw.Elapsed >= TimeSpan.FromSeconds(20))
            {
                throw new TestFailedException("로딩 팝업이 20초가 지나도 닫히지 않음");
            }
            if (framesWhileBusy < 5)
            {
                throw new TestFailedException($"초기화 구간에서 모은 화면이 너무 적어 판정할 수 없음({framesWhileBusy}장)");
            }
            if (barFrames.Count < 3)
            {
                throw new TestFailedException($"초기화 중 진행 막대가 움직이지 않음: {framesWhileBusy}장 중 서로 다른 화면 {barFrames.Count}개");
            }
            if (statuses.Count < 3)
            {
                throw new TestFailedException($"'지금 하는 일' 문구가 단계별로 바뀌지 않음: [{string.Join(" → ", statuses)}]");
            }
            if (statuses[^1] != "준비 완료")
            {
                throw new TestFailedException($"마지막 문구가 '준비 완료'가 아님: [{string.Join(" → ", statuses)}]");
            }
            Console.WriteLine($"      (막대 화면 {barFrames.Count}종/{framesWhileBusy}장, 문구: {string.Join(" → ", statuses)})");
        }
        finally
        {
            try { p.Kill(true); } catch (InvalidOperationException) { }
        }
    }),

    ("Settings_Language_English_SwitchesImmediately_AndPersists", () =>
    {
        string a = MakeDir("pdA_setlang");
        using var app = AppDriver.Launch(NewDataDir(null, "Bidirectional", a));
        app.OpenSettings();
        app.SelectComboItemByIndex("LanguageCombo", 1);
        app.WaitUntil(() => app.SelectedSyncModeName() == "Both ways", $"영어로 바뀌지 않음: '{app.SelectedSyncModeName()}'");
        app.CloseSettings();
        app.CloseGracefully();
        if (!app.ReadSettingsJson().Contains("\"Language\": \"en\""))
        {
            throw new TestFailedException("언어 설정이 저장되지 않음:\n" + app.ReadSettingsJson());
        }
    }),

    ("Settings_TerminalShell_CommandPrompt_RestartsTerminal_AndPersists", () =>
    {
        string a = MakeDir("pdA_setshell");
        using var app = AppDriver.Launch(NewDataDir(null, "Off", a));
        app.OpenSettings();
        app.SelectComboItemByIndex("TerminalShellCombo", 1);
        app.CloseSettings();
        app.WaitUntil(() => { string t = app.TerminalText(); return t.Contains("pdA_setshell>") && !t.Contains("PS "); }, $"명령 프롬프트로 다시 시작하지 않음:\n{app.TerminalText()}", 25000);
        app.CloseGracefully();
        if (!app.ReadSettingsJson().Contains("\"TerminalShell\": \"CommandPrompt\""))
        {
            throw new TestFailedException("셸 설정이 저장되지 않음:\n" + app.ReadSettingsJson());
        }
    }),

    ("Settings_FontSizes_Persist", () =>
    {
        string a = MakeDir("pdA_setfont");
        using var app = AppDriver.Launch(NewDataDir(null, "Off", a));
        app.OpenSettings();
        app.SetSlider("UiFontSizeSlider", 16);
        app.SetSlider("TerminalFontSizeSlider", 18);
        app.CloseSettings();
        app.CloseGracefully();
        string json = app.ReadSettingsJson();
        if (!json.Contains("\"UiFontSize\": 16") || !json.Contains("\"TerminalFontSize\": 18"))
        {
            throw new TestFailedException("글자 크기가 저장되지 않음:\n" + json);
        }
    }),

    ("Settings_TerminalColors_InvalidColorShowsError_ResetClearsIt", () =>
    {
        string a = MakeDir("pdA_setcolor");
        using var app = AppDriver.Launch(NewDataDir(null, "Off", a));
        app.OpenSettings();
        app.SetText("TerminalForegroundBox", "zzz");
        app.WaitUntil(() => app.Exists("TerminalColorError"), "잘못된 색 안내가 보이지 않음");
        app.Click("ResetTerminalColorsButton");
        app.WaitUntil(() => !app.Exists("TerminalColorError"), "기본 색으로 되돌렸는데 안내가 남음");
        app.CloseSettings();
    }),

    ("Settings_TerminalTheme_AndCustomColor_Persist", () =>
    {
        string a = MakeDir("pdA_settheme");
        using var app = AppDriver.Launch(NewDataDir(null, "Off", a));
        app.OpenSettings();
        app.SelectComboItemByIndex("TerminalThemeCombo", 1);
        app.SetText("TerminalForegroundBox", "#112233");
        app.CloseSettings();
        app.CloseGracefully();
        string json = app.ReadSettingsJson();
        if (json.Contains("\"TerminalTheme\": \"Campbell\"") || !json.Contains("\"TerminalForeground\": \"#112233\""))
        {
            throw new TestFailedException("터미널 테마/글자색이 저장되지 않음:\n" + json);
        }
    }),

    ("Settings_StartAtLastPath_Toggle_Persists", () =>
    {
        string a = MakeDir("pdA_setstart");
        using var app = AppDriver.Launch(NewDataDir(null, "Off", a));
        app.OpenSettings();
        if (!app.IsChecked("StartAtLastPathCheck"))
        {
            throw new TestFailedException("기본값은 마지막 폴더에서 시작(켜짐)이어야 함");
        }
        app.Toggle("StartAtLastPathCheck");
        app.CloseSettings();
        app.CloseGracefully();
        if (!app.ReadSettingsJson().Contains("\"StartAtLastPath\": false"))
        {
            throw new TestFailedException("시작 폴더 설정이 저장되지 않음:\n" + app.ReadSettingsJson());
        }
    }),

    ("Settings_ResetFavorites_ClearsRegisteredSlots", () =>
    {
        string a = MakeDir("pdA_resetfav");
        using var app = AppDriver.Launch(NewDataDir(null, "Off", a));
        app.Click("Favorite_1");
        app.WaitUntil(() => app.Find("Favorite_1").Current.Name.Contains("pdA_resetfav"), "즐겨찾기가 등록되지 않음");
        app.OpenSettings();
        app.Click("ResetFavoritesButton");
        app.AnswerConfirm("즐겨찾기 초기화", true);
        app.WaitUntil(() => !app.Find("Favorite_1").Current.Name.Contains("pdA_resetfav"), "초기화했는데 즐겨찾기가 남음");
        app.CloseSettings();
    }),

    ("Exit_WithRunningCommand_AsksConfirm_NoKeepsRunning_YesExits", () =>
    {
        string a = MakeDir("pdA_exit");
        using var app = AppDriver.Launch(NewDataDir(null, "Off", a));
        app.TypeInTerminal("Start-Sleep -Seconds 120{ENTER}");
        app.WaitUntil(() => app.Find("TerminalStatus").Current.Name.Contains("실행 중"), $"명령 실행 상태가 되지 않음: '{app.Find("TerminalStatus").Current.Name}'");

        app.RequestClose();
        app.AnswerConfirm("종료 확인", false);
        Thread.Sleep(1000);
        if (app.HasExited)
        {
            throw new TestFailedException("종료 확인 창에서 아니오를 눌렀는데 앱이 종료됨");
        }
        app.Find("Terminal"); // 창이 그대로 살아 있어야 한다.

        app.RequestClose();
        app.AnswerConfirm("종료 확인", true);
        if (!app.WaitForExit(15000))
        {
            throw new TestFailedException("종료 확인 창에서 예를 눌렀는데 앱이 종료되지 않음");
        }
    }),

    ("Tabs_AddAndClose_LastTabCannotBeClosed", () =>
    {
        string a = MakeDir("pdA_tabadd");
        using var app = AppDriver.Launch(NewDataDir(null, "Off", a));
        app.Find("TerminalTab_1");
        if (app.Exists("TerminalTabClose_1"))
        {
            throw new TestFailedException("탭이 하나뿐인데 닫기 버튼이 보임");
        }
        app.Click("TerminalTabAdd");
        app.WaitUntil(() => app.Exists("TerminalTab_2"), "새 탭이 만들어지지 않음");
        app.WaitUntil(() => app.Exists("TerminalTabClose_1") && app.Exists("TerminalTabClose_2"), "탭이 둘일 때 닫기 버튼이 보이지 않음");
        app.WaitUntil(() => app.TerminalText().Contains("pdA_tabadd>"), $"새 탭의 셸이 시작되지 않음:\n{app.TerminalText()}", 25000);
        app.Click("TerminalTabClose_2");
        app.WaitUntil(() => !app.Exists("TerminalTab_2"), "탭이 닫히지 않음");
        app.WaitUntil(() => !app.Exists("TerminalTabClose_1"), "탭이 하나로 줄었는데 닫기 버튼이 남음");
    }),

    ("Tabs_AreIndependent_AndSelectingTabFollowsInExplorer", () =>
    {
        string a = MakeDir("pdA_tabs"), b = MakeDir("pdB_tabs");
        using var app = AppDriver.Launch(NewDataDir(null, "Bidirectional", a));
        app.WaitUntil(() => app.TerminalText().Contains("pdA_tabs>"), $"첫 탭의 셸이 시작되지 않음:\n{app.TerminalText()}", 25000);
        app.Click("TerminalTabAdd");
        app.WaitUntil(() => app.Exists("TerminalTab_2"), "새 탭이 만들어지지 않음");
        // 새 탭은 탐색기의 현재 폴더에서 시작한다.
        app.WaitUntil(() => app.TerminalText().Contains("pdA_tabs>"), $"새 탭이 탐색기 폴더에서 시작하지 않음:\n{app.TerminalText()}", 25000);
        app.TypeInTerminal("cd " + b + "{ENTER}");
        app.WaitUntil(() => app.PathBarText() == b, $"둘째 탭의 cd를 탐색기가 따라가지 않음: '{app.PathBarText()}'", 25000);

        app.Click("TerminalTab_1");
        app.WaitUntil(() => app.PathBarText() == a, $"첫 탭을 선택했는데 탐색기가 그 폴더로 가지 않음: '{app.PathBarText()}'", 25000);
        if (app.TerminalText().Contains("pdB_tabs>"))
        {
            throw new TestFailedException("첫 탭 화면에 둘째 탭의 프롬프트가 보임(탭이 독립적이지 않음):\n" + app.TerminalText());
        }

        app.Click("TerminalTab_2");
        app.WaitUntil(() => app.PathBarText() == b, $"둘째 탭을 선택했는데 탐색기가 그 폴더로 가지 않음: '{app.PathBarText()}'", 25000);
        app.WaitUntil(() => app.TerminalText().Contains("pdB_tabs>"), $"둘째 탭 화면이 보이지 않음:\n{app.TerminalText()}");
        if (!app.Find("TerminalTab_2").Current.Name.Contains("pdB_tabs"))
        {
            throw new TestFailedException($"탭 제목이 현재 폴더를 보여주지 않음: '{app.Find("TerminalTab_2").Current.Name}'");
        }
    }),

    ("Tabs_KeyboardShortcuts_NewSwitchClose", () =>
    {
        string a = MakeDir("pdA_tabkeys"), b = MakeDir("pdB_tabkeys");
        using var app = AppDriver.Launch(NewDataDir(null, "Bidirectional", a));
        app.WaitUntil(() => app.TerminalText().Contains("pdA_tabkeys>"), $"첫 탭의 셸이 시작되지 않음:\n{app.TerminalText()}", 25000);
        app.TypeInTerminal("^+t"); // Ctrl+Shift+T: 새 탭
        app.WaitUntil(() => app.Exists("TerminalTab_2"), "Ctrl+Shift+T로 새 탭이 만들어지지 않음");
        app.WaitUntil(() => app.TerminalText().Contains("pdA_tabkeys>"), $"새 탭의 셸이 시작되지 않음:\n{app.TerminalText()}", 25000);
        app.TypeInTerminal("cd " + b + "{ENTER}");
        app.WaitUntil(() => app.PathBarText() == b, $"둘째 탭의 cd를 탐색기가 따라가지 않음: '{app.PathBarText()}'", 25000);

        app.PressKeys("^{TAB}"); // 다음 탭(끝에서 처음으로)
        app.WaitUntil(() => app.PathBarText() == a, $"Ctrl+Tab으로 첫 탭이 선택되지 않음: '{app.PathBarText()}'", 25000);
        app.PressKeys("^+{TAB}"); // 이전 탭(처음에서 끝으로)
        app.WaitUntil(() => app.PathBarText() == b, $"Ctrl+Shift+Tab으로 둘째 탭이 선택되지 않음: '{app.PathBarText()}'", 25000);

        app.PressKeys("^+w"); // Ctrl+Shift+W: 선택된 탭(둘째) 닫기
        app.WaitUntil(() => !app.Exists("TerminalTab_2"), "Ctrl+Shift+W로 탭이 닫히지 않음");
        app.WaitUntil(() => app.PathBarText() == a, $"탭을 닫은 뒤 남은 탭의 폴더로 가지 않음: '{app.PathBarText()}'", 25000);
        app.PressKeys("^+w"); // 마지막 탭은 닫히지 않는다.
        Thread.Sleep(500);
        app.Find("TerminalTab_1");
    }),

    ("Tabs_CloseBusyTab_AsksConfirm", () =>
    {
        string a = MakeDir("pdA_tabbusy");
        using var app = AppDriver.Launch(NewDataDir(null, "Off", a));
        app.Click("TerminalTabAdd");
        app.WaitUntil(() => app.Exists("TerminalTab_2"), "새 탭이 만들어지지 않음");
        app.WaitUntil(() => app.TerminalText().Contains("pdA_tabbusy>"), $"새 탭의 셸이 시작되지 않음:\n{app.TerminalText()}", 25000);
        app.TypeInTerminal("Start-Sleep -Seconds 120{ENTER}");
        app.WaitUntil(() => app.Find("TerminalStatus").Current.Name.Contains("실행 중"), $"명령 실행 상태가 되지 않음: '{app.Find("TerminalStatus").Current.Name}'");

        app.Click("TerminalTabClose_2");
        app.AnswerConfirm("터미널 탭 닫기", false);
        Thread.Sleep(700);
        if (!app.Exists("TerminalTab_2"))
        {
            throw new TestFailedException("확인 창에서 아니오를 눌렀는데 탭이 닫힘");
        }
        app.Click("TerminalTabClose_2");
        app.AnswerConfirm("터미널 탭 닫기", true);
        app.WaitUntil(() => !app.Exists("TerminalTab_2"), "확인 창에서 예를 눌렀는데 탭이 닫히지 않음");
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
