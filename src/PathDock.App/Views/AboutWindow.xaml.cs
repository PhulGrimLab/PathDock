using System.Diagnostics;
using System.IO;
using System.Windows;
using PathDock.App.Services;
using Wpf.Ui.Controls;

namespace PathDock.App.Views;

/// <summary>
/// 프로그램 정보 창(버전, 제작자, 문의처, 실행 환경, 데이터/로그 폴더). 제목 표시줄의 로고를 누르면 열린다(CodeAtlas와 같은 방식).
/// 표시 데이터는 <see cref="AppInfo"/>가 제공하고 code-behind에는 UI 동작(복사, 폴더 열기)만 둔다.
/// </summary>
public partial class AboutWindow : FluentWindow
{
    private const int CopiedMessageMs = 2500;

    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = $"버전 {AppInfo.Version}";
        TaglineText.Text = AppInfo.Tagline;
        AuthorText.Text = AppInfo.Author;
        MailLink.Inlines.Add(AppInfo.ContactEmail);
        TechText.Text = AppInfo.Technologies;
        DataDirText.Text = AppInfo.DataDirectory;
        LogDirText.Text = AppInfo.LogDirectory;
        string build = AppInfo.BuildTime is { } t ? $"빌드 {t} · " : string.Empty;
        EnvironmentText.Text = build + AppInfo.EnvironmentText;
        CopyrightText.Text = $"© {DateTime.Now.Year} {AppInfo.Author}. All rights reserved.";
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(AppInfo.PlainText());
            CopiedText.Text = "복사했습니다";
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // 다른 프로그램이 클립보드를 잡고 있는 순간: 실패 문구만 보여준다.
            CopiedText.Text = "클립보드를 사용할 수 없습니다";
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
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true })?.Dispose();
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
