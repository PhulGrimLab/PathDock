using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PathDock.Core.Settings;

namespace PathDock.App.ViewModels;

/// <summary>
/// 즐겨찾기 한 칸(총 <see cref="AppSettings.MaxFavorites"/>개 고정 슬롯). Owner: MainViewModel.
/// 동작은 소유자가 넘겨준 델리게이트로 위임한다(ViewModel 간 직접 참조 방지).
/// </summary>
public sealed partial class FavoriteSlotViewModel : ObservableObject
{
    private readonly Action<FavoriteSlotViewModel> _onClick;
    private readonly Action<FavoriteSlotViewModel> _onRegisterHere;
    private readonly Action<FavoriteSlotViewModel> _onClear;
    private readonly Action<FavoriteSlotViewModel> _onRename;
    private readonly Action<FavoriteSlotViewModel, int> _onMove;

    public FavoriteSlotViewModel(int index, Action<FavoriteSlotViewModel> onClick,
        Action<FavoriteSlotViewModel> onRegisterHere, Action<FavoriteSlotViewModel> onClear,
        Action<FavoriteSlotViewModel> onRename, Action<FavoriteSlotViewModel, int> onMove)
    {
        Index = index;
        _onClick = onClick;
        _onRegisterHere = onRegisterHere;
        _onClear = onClear;
        _onRename = onRename;
        _onMove = onMove;
    }

    /// <summary>1부터 시작하는 슬롯 번호.</summary>
    public int Index { get; }
    public string AutomationId => $"Favorite_{Index}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(Caption), nameof(ToolTipText), nameof(DisplayName))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(Caption), nameof(ToolTipText), nameof(DisplayName))]
    private string _path = string.Empty;

    public bool IsEmpty => string.IsNullOrEmpty(Path);
    public string Caption => IsEmpty ? $"{Index}. (비어 있음)" : $"{Index}. {Name}";

    /// <summary>칩에 보이는 이름(번호 배지는 별도). 빈 칸은 "등록"(클릭하면 현재 폴더 등록).</summary>
    public string DisplayName => IsEmpty ? "등록" : Name;
    public string ToolTipText => IsEmpty ? "클릭하면 현재 폴더를 이 칸에 등록합니다" : Path;

    [RelayCommand]
    private void Click() => _onClick(this);

    [RelayCommand]
    private void RegisterHere() => _onRegisterHere(this);

    [RelayCommand]
    private void Clear() => _onClear(this);

    [RelayCommand]
    private void Rename() => _onRename(this);

    [RelayCommand]
    private void MoveLeft() => _onMove(this, -1);

    [RelayCommand]
    private void MoveRight() => _onMove(this, 1);
}
