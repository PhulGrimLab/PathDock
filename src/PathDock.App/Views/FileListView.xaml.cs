using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PathDock.App.ViewModels;
using PathDock.Core.Explorer;

namespace PathDock.App.Views;

/// <summary>
/// 현재 폴더 파일 목록 뷰. code-behind에는 UI 이벤트(머리글 클릭, 더블클릭, 단축키)를 ExplorerViewModel 명령으로
/// 전달하는 코드와 정렬 표시(▲▼) 갱신만 둔다. 로직은 모두 <see cref="ExplorerViewModel"/>에 있다.
/// </summary>
public partial class FileListView : UserControl
{
    public FileListView()
    {
        InitializeComponent();
        FileList.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(OnColumnHeaderClick));
        DataContextChanged += OnDataContextChanged;
        FileList.SizeChanged += (_, _) => FitColumns();
        Loaded += (_, _) =>
        {
            Services.FontService.UiFontChanged += FitColumns;
            FitColumns();
        };
        Unloaded += (_, _) => Services.FontService.UiFontChanged -= FitColumns;
    }

    /// <summary>
    /// 날짜/크기/하위 항목 열은 글자 크기에 비례한 폭으로, 이름 열은 남는 폭을 채운다.
    /// 고정 폭이면 기본 창에서도 크기 열이 잘려 가로 스크롤이 필요했다.
    /// </summary>
    private void FitColumns()
    {
        double fs = TryFindResource("PdFontSize") is double d ? d : 12;
        ModifiedColumn.Width = Math.Round(fs * 10.7);
        SizeColumn.Width = Math.Round(fs * 7.3);
        CountColumn.Width = Math.Round(fs * 11);
        double available = FileList.ActualWidth - ModifiedColumn.Width - SizeColumn.Width - CountColumn.Width - SystemParameters.VerticalScrollBarWidth - 14;
        NameColumn.Width = Math.Max(110, available);
    }

    private ExplorerViewModel? Vm => DataContext as ExplorerViewModel;

    /// <summary>ViewModel이 바뀌면 정렬 표시를 갱신하도록 변경 알림을 구독한다.</summary>
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged old)
        {
            old.PropertyChanged -= OnViewModelPropertyChanged;
        }
        if (e.NewValue is INotifyPropertyChanged now)
        {
            now.PropertyChanged += OnViewModelPropertyChanged;
        }
        UpdateColumnHeaders();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ExplorerViewModel.NameHeader) or nameof(ExplorerViewModel.ModifiedHeader) or nameof(ExplorerViewModel.SizeHeader))
        {
            UpdateColumnHeaders();
        }
    }

    private void UpdateColumnHeaders()
    {
        if (Vm is not { } vm)
        {
            return;
        }
        NameColumn.Header = vm.NameHeader;
        ModifiedColumn.Header = vm.ModifiedHeader;
        SizeColumn.Header = vm.SizeHeader;
    }

    private void OnColumnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || e.OriginalSource is not GridViewColumnHeader { Column: { } column })
        {
            return;
        }
        if (column == NameColumn)
        {
            vm.SortByCommand.Execute(SortKey.Name);
        }
        else if (column == ModifiedColumn)
        {
            vm.SortByCommand.Execute(SortKey.Modified);
        }
        else if (column == SizeColumn)
        {
            vm.SortByCommand.Execute(SortKey.Size);
        }
        else
        {
            // "하위 항목" 열은 정렬 기준이 아니다.
        }
    }

    private void FilterBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Vm is { } vm)
        {
            vm.FilterText = string.Empty;
            FileList.Focus();
            e.Handled = true;
        }
    }

    /// <summary>이름 칸 툴팁: 이미지는 썸네일, 텍스트는 앞 20줄을 읽어 보여주고, 그 밖의 항목이거나 읽을 수 없으면 열지 않는다.</summary>
    private async void OnNameToolTipOpening(object sender, ToolTipEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FileItemViewModel item } element)
        {
            return;
        }
        if (!item.IsPreviewable)
        {
            e.Handled = true; // 미리보기 대상이 아니면 툴팁을 띄우지 않는다.
            return;
        }
        if (!await item.LoadPreviewAsync() && ToolTipService.GetToolTip(element) is ToolTip tip)
        {
            tip.IsOpen = false; // 손상되었거나 읽을 수 없는 이미지: 조용히 닫는다.
        }
    }

    private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Vm?.SetSelection(FileList.SelectedItems.OfType<FileItemViewModel>());
    }

    private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm is { SelectedItem: { } item } vm)
        {
            vm.OpenItemCommand.Execute(item);
        }
        else
        {
            // 빈 영역 더블클릭: 동작 없음.
        }
    }

    /// <summary>목록 단축키: Ctrl+F=이름 필터, Ctrl+C/X/V=복사/잘라내기/붙여넣기, Enter=열기, Backspace=상위, F2=이름 변경, Delete=삭제, F5=새로 고침, Ctrl+Shift+N=새 폴더.</summary>
    private void FileList_KeyDown(object sender, KeyEventArgs e)
    {
        if (Vm is not { } vm)
        {
            return;
        }
        if (e.Key == Key.Enter && vm.SelectedItem is { } item)
        {
            vm.OpenItemCommand.Execute(item);
        }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            vm.CopySelectedCommand.Execute(null);
        }
        else if (e.Key == Key.X && Keyboard.Modifiers == ModifierKeys.Control)
        {
            vm.CutSelectedCommand.Execute(null);
        }
        else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            vm.PasteCommand.Execute(null);
        }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            FilterBox.Focus();
            FilterBox.SelectAll();
        }
        else if (e.Key == Key.F2)
        {
            vm.RenameSelectedCommand.Execute(null);
        }
        else if (e.Key == Key.Delete)
        {
            vm.DeleteSelectedCommand.Execute(null);
        }
        else if (e.Key == Key.F5)
        {
            vm.RefreshCommand.Execute(null);
        }
        else if (e.Key == Key.N && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            vm.NewFolderCommand.Execute(null);
        }
        else if (e.Key == Key.Back)
        {
            vm.GoUpCommand.Execute(null);
        }
        else
        {
            return; // 그 밖의 키는 ListView 기본 동작.
        }
        e.Handled = true;
    }
}
