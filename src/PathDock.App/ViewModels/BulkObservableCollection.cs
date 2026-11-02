using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace PathDock.App.ViewModels;

/// <summary>
/// 목록 전체를 한 번에 바꿀 수 있는 컬렉션. <see cref="ObservableCollection{T}"/>에서 항목을 하나씩 Add하면 항목 수만큼 변경 알림이 나가
/// 큰 폴더에서 화면 갱신이 수만 번 일어나므로, 전체 교체는 알림을 한 번(Reset)만 보낸다. UI 스레드에서만 사용한다.
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (T item in items)
        {
            Items.Add(item);
        }
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
