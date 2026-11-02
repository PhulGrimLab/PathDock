using System.Windows;
using PathDock.App.Views;
using PathDock.Core.Localization;

namespace PathDock.App.Services;

/// <summary>ViewModel이 UI 창을 직접 만들지 않도록 하는 대화상자 추상화(MVVM).</summary>
public interface IDialogService
{
    /// <summary>예/아니오 확인. 기본 선택은 "아니오"(파괴적 작업의 실수 방지).</summary>
    bool Confirm(string title, string message);

    /// <summary>한 줄 입력. 취소하면 null.</summary>
    string? Prompt(string title, string label, string initialValue);

    /// <summary>파일 저장 위치 선택(경로·파일명·확장자). 취소하면 null.</summary>
    string? PickSaveFile(string title, string defaultFileName);

    /// <summary>프로그램 정보 창(모달)을 연다.</summary>
    void ShowAbout();

    /// <summary>설정 창(모달)을 연다. <paramref name="dataContext"/>는 창의 바인딩 대상(MainViewModel).</summary>
    void ShowSettings(object dataContext);

    /// <summary>PC 정리 창(모달)을 연다. <paramref name="dataContext"/>는 창의 바인딩 대상(CleanupViewModel).</summary>
    void ShowCleanup(object dataContext);
}

/// <summary>WPF 구현. Owner: App/MainViewModel, Lifetime: 앱 전체. UI 스레드에서만 호출한다.</summary>
public sealed class WpfDialogService : IDialogService
{
    public bool Confirm(string title, string message)
    {
        // 모달 창(PC 정리, 설정)이 열려 있으면 그 창 위에 띄운다: 비활성인 메인 창을 소유자로 하면 확인 창이 모달 창 뒤에 가려질 수 있다.
        Window? owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;
        return ConfirmDialog.Ask(owner, title, message);
    }

    public void ShowAbout()
    {
        new AboutWindow { Owner = Application.Current?.MainWindow }.ShowDialog();
    }

    public void ShowSettings(object dataContext)
    {
        new SettingsWindow { Owner = Application.Current?.MainWindow, DataContext = dataContext }.ShowDialog();
    }

    public void ShowCleanup(object dataContext)
    {
        new CleanupWindow { Owner = Application.Current?.MainWindow, DataContext = dataContext }.ShowDialog();
    }

    public string? PickSaveFile(string title, string defaultFileName)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = title,
            FileName = defaultFileName,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Filter = Loc.T("텍스트 파일 (*.txt)|*.txt|로그 파일 (*.log)|*.log|모든 파일 (*.*)|*.*", "Text files (*.txt)|*.txt|Log files (*.log)|*.log|All files (*.*)|*.*"),
            DefaultExt = ".txt",
            AddExtension = true,
            OverwritePrompt = true,
        };
        Window? owner = Application.Current?.MainWindow;
        bool? ok = owner is null ? dlg.ShowDialog() : dlg.ShowDialog(owner);
        return ok == true ? dlg.FileName : null;
    }

    public string? Prompt(string title, string label, string initialValue)
    {
        var dlg = new InputDialog(title, label, initialValue) { Owner = Application.Current?.MainWindow };
        return dlg.ShowDialog() == true ? dlg.Value : null;
    }
}
