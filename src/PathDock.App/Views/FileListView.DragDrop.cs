using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PathDock.App.ViewModels;
using PathDock.Core.Explorer;

namespace PathDock.App.Views;

/// <summary>
/// FileListView의 끌어 놓기: 선택한 항목을 끌어 폴더 행(또는 목록의 빈 곳 = 현재 폴더)에 놓으면 이동/복사한다(Ctrl=복사, Shift=이동).
/// 여러 개를 선택한 상태에서 선택된 항목을 눌러도 선택이 풀리지 않게 하고(탐색기와 같은 동작), 놓을 폴더 행은 강조한다.
/// 판단 규칙은 Core의 <see cref="DropRules"/>, 실제 복사/이동은 ExplorerViewModel(붙여넣기와 같은 진행 표시·취소)이 한다.
/// </summary>
public partial class FileListView
{
    private Point _dragStart;
    private FileItemViewModel? _dragCandidate;
    private FileItemViewModel? _pendingSelect;
    private FileItemViewModel? _highlighted;

    private static FileItemViewModel? ItemAt(DependencyObject? source) =>
        FileDragDrop.FindAncestor<ListViewItem>(source)?.DataContext as FileItemViewModel;

    private void OnListMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragCandidate = null;
        _pendingSelect = null;
        if (ItemAt(e.OriginalSource as DependencyObject) is not { } item)
        {
            return; // 머리글이나 빈 곳
        }
        _dragStart = e.GetPosition(null);
        _dragCandidate = item;
        if (Keyboard.Modifiers == ModifierKeys.None && FileList.SelectedItems.Count > 1 && FileList.SelectedItems.Contains(item))
        {
            // 여러 개를 고른 채 그중 하나를 누르면 끌려는 것일 수 있다: 버튼을 뗄 때까지 선택을 바꾸지 않는다.
            _pendingSelect = item;
            FileList.Focus();
            e.Handled = true;
        }
    }

    private void OnListMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_pendingSelect is { } item)
        {
            FileList.SelectedItems.Clear(); // 끌지 않고 뗐다: 그 항목 하나만 선택한다.
            FileList.SelectedItems.Add(item);
        }
        _pendingSelect = null;
        _dragCandidate = null;
    }

    private void OnListMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate is not { } candidate)
        {
            return;
        }
        Point p = e.GetPosition(null);
        if (Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(p.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }
        _dragCandidate = null;
        _pendingSelect = null;
        if (!FileList.SelectedItems.Contains(candidate))
        {
            FileList.SelectedItems.Clear();
            FileList.SelectedItems.Add(candidate);
        }
        string[] paths = FileList.SelectedItems.Cast<FileItemViewModel>().Select(i => i.FullPath).ToArray();
        var data = new DataObject(DataFormats.FileDrop, paths);
        try
        {
            DragDrop.DoDragDrop(FileList, data, DragDropEffects.Copy | DragDropEffects.Move);
        }
        finally
        {
            SetHighlight(null);
        }
    }

    /// <summary>놓을 폴더: 폴더 행 위면 그 폴더, 그 밖(파일 행, 빈 곳)이면 현재 폴더.</summary>
    private string? DropTarget(DragEventArgs e, out FileItemViewModel? folderItem)
    {
        folderItem = ItemAt(e.OriginalSource as DependencyObject) is { IsDirectory: true } dir ? dir : null;
        return folderItem?.FullPath ?? Vm?.CurrentPath;
    }

    private void OnListDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        IReadOnlyList<string> paths = FileDragDrop.GetPaths(e.Data);
        if (Vm is not { } vm || paths.Count == 0 || DropTarget(e, out FileItemViewModel? folder) is not { } target)
        {
            e.Effects = DragDropEffects.None;
            SetHighlight(null);
            return;
        }
        DropEffect effect = vm.GetDropEffect(paths, target, FileDragDrop.CtrlHeld(e), FileDragDrop.ShiftHeld(e));
        e.Effects = FileDragDrop.ToWpf(effect);
        SetHighlight(effect != DropEffect.None ? folder : null);
    }

    private void OnListDragLeave(object sender, DragEventArgs e) => SetHighlight(null);

    private async void OnListDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        SetHighlight(null);
        IReadOnlyList<string> paths = FileDragDrop.GetPaths(e.Data);
        if (Vm is not { } vm || paths.Count == 0 || DropTarget(e, out _) is not { } target)
        {
            return;
        }
        DropEffect effect = vm.GetDropEffect(paths, target, FileDragDrop.CtrlHeld(e), FileDragDrop.ShiftHeld(e));
        e.Effects = FileDragDrop.ToWpf(effect);
        await vm.DropAsync(paths, target, effect);
    }

    private void SetHighlight(FileItemViewModel? item)
    {
        if (ReferenceEquals(_highlighted, item))
        {
            return;
        }
        if (_highlighted is not null)
        {
            _highlighted.IsDropTarget = false;
        }
        _highlighted = item;
        if (item is not null)
        {
            item.IsDropTarget = true;
        }
    }
}
