using System.Windows;
using Wpf.Ui.Controls;

namespace PathDock.App.Views;

/// <summary>설정 창(글자 크기, 즐겨찾기 초기화). 값과 동작은 MainViewModel에 바인딩되고 code-behind에는 닫기만 둔다.</summary>
public partial class SettingsWindow : FluentWindow
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}