using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace PathDock.App.Views;

/// <summary>즐겨찾기 칩 줄. 폭이 충분하면 한 줄, 모자라면 5칸씩 두 줄로 균형 있게 배치한다(로직은 MainViewModel/FavoriteSlotViewModel).</summary>
public partial class FavoritesBarView : UserControl
{
    private UniformGrid? _panel;

    public FavoritesBarView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateColumns();
        Services.FontService.UiFontChanged += () => Dispatcher.BeginInvoke(UpdateColumns, DispatcherPriority.Loaded);
    }

    private void OnPanelLoaded(object sender, RoutedEventArgs e)
    {
        _panel = (UniformGrid)sender;
        UpdateColumns();
    }

    private void UpdateColumns()
    {
        if (_panel is null || _panel.Children.Count == 0 || ActualWidth <= 0)
        {
            return;
        }
        double natural = 0;
        foreach (UIElement child in _panel.Children)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            natural = Math.Max(natural, child.DesiredSize.Width);
        }
        int count = _panel.Children.Count;
        int columns = ActualWidth >= natural * count ? count : (count + 1) / 2;
        if (_panel.Columns != columns)
        {
            _panel.Columns = columns;
        }
    }
}
