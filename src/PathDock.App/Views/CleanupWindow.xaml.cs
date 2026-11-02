using System.Windows;
using PathDock.App.ViewModels;
using Wpf.Ui.Controls;

namespace PathDock.App.Views;

/// <summary>PC 정리 창(임시 파일/캐시/휴지통/메모리). 열리면 바로 검사한다. 로직은 <see cref="CleanupViewModel"/>에 있고 여기에는 창 동작만 둔다.</summary>
public partial class CleanupWindow : FluentWindow
{
    public CleanupWindow()
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height; // 작은 화면에서는 창이 잘리지 않고 안쪽이 스크롤된다.
        Loaded += (_, _) =>
        {
            if (DataContext is CleanupViewModel vm)
            {
                vm.ScanCommand.Execute(null);
            }
        };
        Closed += (_, _) => (DataContext as CleanupViewModel)?.Cancel();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
