using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Threading;
using PathDock.App.ViewModels;

namespace PathDock.App.Views;

/// <summary>상위 폴더 열 한 개. 조부모/부모 열이 같은 뷰를 재사용한다. 선택된(현재 경로가 지나는) 항목이 보이도록 스크롤한다.</summary>
public partial class AncestorColumnView : UserControl
{
    private AncestorColumnViewModel? _vm;

    public AncestorColumnView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as AncestorColumnViewModel);
        Loaded += (_, _) => ScrollToSelection();
    }

    private void Attach(AncestorColumnViewModel? vm)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.Items.CollectionChanged -= OnItemsChanged;
        }
        _vm = vm;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.Items.CollectionChanged += OnItemsChanged;
        }
        ScrollToSelection();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AncestorColumnViewModel.SelectedItem))
        {
            ScrollToSelection();
        }
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ScrollToSelection();
    }

    /// <summary>항목이 채워지고 배치된 뒤에 선택 항목을 화면 안으로 가져온다.</summary>
    private void ScrollToSelection()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (FolderList.SelectedItem is { } item)
            {
                FolderList.ScrollIntoView(item);
            }
        });
    }
}
