using System.Diagnostics;
using System.IO;
using System.Windows;
using PathDock.App.Services;
using PathDock.Core.Localization;
using Wpf.Ui.Controls;

namespace PathDock.App.Views;

/// <summary>
/// 프로그램 정보 창(버전, 제작자, 문의처, 실행 환경, 데이터/로그 폴더). 제목 표시줄의 로고를 누르면 열린다(CodeAtlas와 같은 방식).
/// 표시 데이터는 <see cref="AppInfo"/>가 제공하고 code-behind에는 UI 동작(복사, 폴더 열기)만 둔다.
/// </summary>
public partial class AboutWindow : FluentWindow
{
    private const int CopiedMessageMs = 2500;

    /// <summary>정보 창에 보여줄 단축키 한 줄.</summary>
    private sealed record ShortcutRow(string Keys, string Description);

    // 언어가 실행 중에 바뀔 수 있어 창을 열 때마다 만든다.
    private static ShortcutRow[] BuildShortcutRows() => new ShortcutRow[]
    {
        new("Enter / Backspace", Loc.T("폴더·파일 열기 / 위 폴더로 (파일 목록)", "Open folder or file / go up (file list)")),
        new("F2 / Delete", Loc.T("이름 변경 / 삭제 - 휴지통으로", "Rename / delete to the Recycle Bin")),
        new("Ctrl+Shift+N", Loc.T("새 폴더", "New folder")),
        new("Ctrl+C / X / V", Loc.T("파일 복사 / 잘라내기 / 붙여넣기", "Copy / cut / paste files")),
        new("F5", Loc.T("새로 고침", "Refresh")),
        new("Ctrl+F", Loc.T("이름 검색 (Esc로 해제)", "Filter by name (Esc to clear)")),
        new("Ctrl+Alt+C / E", Loc.T("계산기 / 휴지통 비우기", "Calculator / empty Recycle Bin")),
        new("Ctrl+Shift+T / W", Loc.T("터미널 새 탭 / 현재 탭 닫기", "New terminal tab / close current tab")),
        new("Ctrl+Tab / Ctrl+Shift+Tab", Loc.T("다음 / 이전 터미널 탭", "Next / previous terminal tab")),
        new(Loc.T("터미널 Ctrl+C", "Terminal Ctrl+C"), Loc.T("선택이 있으면 복사, 없으면 실행 중단", "Copy if text is selected, otherwise interrupt")),
        new(Loc.T("터미널 Ctrl+Shift+C / Ctrl+V", "Terminal Ctrl+Shift+C / Ctrl+V"), Loc.T("복사 / 붙여넣기", "Copy / paste")),
        new(Loc.T("터미널 우클릭", "Terminal right-click"), Loc.T("선택이 있으면 복사, 없으면 붙여넣기", "Copy if text is selected, otherwise paste")),
    };

    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = Loc.T($"버전 {AppInfo.Version}", $"Version {AppInfo.Version}");
        TaglineText.Text = AppInfo.Tagline;
        FeaturesText.Text = Loc.T(
            "즐겨찾기 10칸(빈 칸 클릭=현재 폴더 등록, 우클릭=편집) · 탐색기 · 내장 터미널(PowerShell/명령 프롬프트, 여러 탭) · 탐색기와 터미널의 경로 동기화(양방향/단방향/끄기).",
            "10 favorite slots (click an empty slot to register the current folder, right-click to edit) · file explorer · built-in terminal (PowerShell/Command Prompt, multiple tabs) · path sync between explorer and terminal (two-way/one-way/off).");
        ShortcutList.ItemsSource = BuildShortcutRows();
        MaxHeight = SystemParameters.WorkArea.Height; // 작은 화면에서는 창이 잘리지 않고 안쪽이 스크롤된다.
        AuthorText.Text = AppInfo.Author;
        MailLink.Inlines.Add(AppInfo.ContactEmail);
        TechText.Text = AppInfo.Technologies;
        DataDirText.Text = AppInfo.DataDirectory;
        LogDirText.Text = AppInfo.LogDirectory;
        string build = AppInfo.BuildTime is { } t ? $"{Loc.T("빌드", "Built")} {t} · " : string.Empty;
        EnvironmentText.Text = build + AppInfo.EnvironmentText;
        CopyrightText.Text = $"© {DateTime.Now.Year} {AppInfo.Author}. All rights reserved.";
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(AppInfo.PlainText());
            CopiedText.Text = Loc.T("복사했습니다", "Copied");
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // 다른 프로그램이 클립보드를 잡고 있는 순간: 실패 문구만 보여준다.
            CopiedText.Text = Loc.T("클립보드를 사용할 수 없습니다", "The clipboard is not available");
        }
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(CopiedMessageMs) };
        timer.Tick += (_, _) =>
        {
            CopiedText.Text = string.Empty;
            timer.Stop();
        };
        timer.Start();
    }

    private void OnOpenLogsClick(object sender, RoutedEventArgs e)
    {
        string dir = AppInfo.LogDirectory;
        Directory.CreateDirectory(dir);
        // 전체 경로로 실행한다: 이름만 주면 앱 폴더 등에서 같은 이름의 파일을 먼저 찾을 수 있다(이 앱은 관리자 권한).
        string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        Process.Start(new ProcessStartInfo(explorer, $"\"{dir}\"") { UseShellExecute = true })?.Dispose();
    }

    private void OnMailClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo($"mailto:{AppInfo.ContactEmail}") { UseShellExecute = true })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // 연결된 메일 프로그램이 없음: 주소는 화면에 보이므로 조용히 넘어간다.
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
