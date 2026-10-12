using System.Windows;
using PathDock.App.Views;

namespace PathDock.App.Services;

/// <summary>ViewModel이 UI 창을 직접 만들지 않도록 하는 대화상자 추상화(MVVM).</summary>
public interface IDialogService
{
    /// <summary>예/아니오 확인. 기본 선택은 "아니오"(파괴적 작업의 실수 방지).</summary>
    bool Confirm(string title, string message);

    /// <summary>한 줄 입력. 취소하면 null.</summary>
    string? Prompt(string title, string label, string initialValue);

    /// <summary>프로그램 정보 창(모달)을 연다.</summary>
    void ShowAbout();
}

/// <summary>WPF 구현. Owner: App/MainViewModel, Lifetime: 앱 전체. UI 스레드에서만 호출한다.</summary>
public sealed class WpfDialogService : IDialogService
{
    public bool Confirm(string title, string message)
    {
        Window? owner = Application.Current?.MainWindow;
        MessageBoxResult r = owner is null
            ? MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
            : MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        return r == MessageBoxResult.Yes;
    }

    public void ShowAbout()
    {
        new AboutWindow { Owner = Application.Current?.MainWindow }.ShowDialog();
    }

    public string? Prompt(string title, string label, string initialValue)
    {
        var dlg = new InputDialog(title, label, initialValue) { Owner = Application.Current?.MainWindow };
        return dlg.ShowDialog() == true ? dlg.Value : null;
    }
}
