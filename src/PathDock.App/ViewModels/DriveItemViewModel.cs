using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PathDock.Core.Explorer;

namespace PathDock.App.ViewModels;

/// <summary>드라이브 목록의 한 칸. Owner: MainViewModel. 이동 동작은 델리게이트로 위임한다.</summary>
public sealed partial class DriveItemViewModel : ObservableObject
{
    private readonly Action<DriveItemViewModel> _onClick;

    public DriveItemViewModel(DriveEntry entry, Action<DriveItemViewModel> onClick)
    {
        Entry = entry;
        _onClick = onClick;
    }

    public DriveEntry Entry { get; }
    public string Root => Entry.Root;

    /// <summary>예: "C:" (UIA Id용으로 콜론/역슬래시 제거한 글자 하나).</summary>
    public string AutomationId => $"Drive_{Entry.Root.TrimEnd('\\', ':')}";

    public string Title => string.IsNullOrEmpty(Entry.Label)
        ? $"{Entry.Root.TrimEnd('\\')} ({TypeText})"
        : $"{Entry.Root.TrimEnd('\\')} {Entry.Label}";

    public double UsedPercent => Entry.UsedRatio * 100;

    /// <summary>용량 문구. 예: "120.5 GB 사용 / 465.8 GB (26%)". 용량 미상이면 "사용 불가".</summary>
    public string UsageText => Entry.TotalSize > 0
        ? $"{SizeFormatter.Format(Entry.UsedSize)} 사용 / {SizeFormatter.Format(Entry.TotalSize)} ({Entry.UsedRatio:P0})"
        : UnavailableText;

    /// <summary>용량을 읽지 못한 이유를 드라이브 종류별로 안내한다.</summary>
    private string UnavailableText => Entry.Type switch
    {
        DriveType.Removable => "사용 불가 · 미디어가 없거나 준비되지 않음",
        DriveType.CDRom => "사용 불가 · 디스크가 없음",
        DriveType.Network => "사용 불가 · 네트워크에 연결되지 않음",
        _ => "사용 불가 · 준비되지 않음",
    };

    /// <summary>여유 공간이 10% 미만이면 경고색으로 표시하기 위한 플래그.</summary>
    public bool IsLow => Entry.TotalSize > 0 && Entry.UsedRatio >= 0.9;

    /// <summary>현재 탐색기 경로가 이 드라이브에 있는지(강조 표시).</summary>
    [ObservableProperty]
    private bool _isCurrent;

    private string TypeText => Entry.Type switch
    {
        DriveType.Removable => "이동식",
        DriveType.Network => "네트워크",
        DriveType.CDRom => "CD/DVD",
        _ => "로컬 디스크",
    };

    [RelayCommand]
    private void Open() => _onClick(this);

}
