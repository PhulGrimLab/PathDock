using System.Windows;

namespace PathDock.App.Views;

/// <summary>예/아니오 확인 창. Owner: 호출한 창, Lifetime: 모달로 한 번 표시. 기본 선택은 "아니요".</summary>
public partial class ConfirmDialog : Window
{
    private ConfirmDialog(string title, string message)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        Loaded += (_, _) => NoButton.Focus();
    }

    /// <summary>모달로 묻고 "예"이면 true. 소유 창이 없으면 화면 가운데에 띄운다.</summary>
    public static bool Ask(Window? owner, string title, string message)
    {
        var dlg = new ConfirmDialog(title, message);
        if (owner is not null && owner.IsVisible)
        {
            dlg.Owner = owner;
        }
        else
        {
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        return dlg.ShowDialog() == true;
    }

    private void Yes_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
