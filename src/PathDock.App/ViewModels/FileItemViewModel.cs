using System.IO;
using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using PathDock.Core.Explorer;
using Wpf.Ui.Controls;

namespace PathDock.App.ViewModels;

/// <summary>
/// 파일 목록 한 줄의 표시용 래퍼. 폴더는 전체 크기/하위 개수가 백그라운드에서 계산되어
/// <see cref="SetStats"/>로 채워진다(그 전에는 "계산 중…"). UI 스레드에서만 변경한다.
/// </summary>
public sealed partial class FileItemViewModel : ObservableObject
{
    public const string CalculatingText = "계산 중…";

    public FileItemViewModel(FileItem item)
    {
        Item = item;
        Category = FileCategorizer.Categorize(item.Name, item.IsDirectory);
    }

    public FileItem Item { get; }

    // ----- 종류별 아이콘/색(목록의 이름 열). 종류 분류는 Core(FileCategorizer), 여기서는 표시 방식만 정한다. -----

    /// <summary>목록 아이콘 종류.</summary>
    public FileCategory Category { get; }

    public SymbolRegular IconSymbol => Category switch
    {
        FileCategory.Folder => SymbolRegular.Folder24,
        FileCategory.Code => SymbolRegular.Code24,
        FileCategory.Document => SymbolRegular.DocumentText24,
        FileCategory.Data => SymbolRegular.Table24,
        FileCategory.Image => SymbolRegular.Image24,
        FileCategory.Audio => SymbolRegular.MusicNote124,
        FileCategory.Video => SymbolRegular.Video24,
        FileCategory.Archive => SymbolRegular.FolderZip24,
        FileCategory.Executable => SymbolRegular.Rocket24,
        _ => SymbolRegular.Document24,
    };

    /// <summary>아이콘 색(다크/라이트 모두에서 읽히는 중간 채도). 고정 브러시라 Freeze되어 있다.</summary>
    public Brush IconBrush => IconBrushes[Category];

    private static readonly Dictionary<FileCategory, Brush> IconBrushes = new()
    {
        [FileCategory.Folder] = Make(0xF0, 0xB4, 0x29),
        [FileCategory.Code] = Make(0x3B, 0x9E, 0xF5),
        [FileCategory.Document] = Make(0x8F, 0x9B, 0xB3),
        [FileCategory.Data] = Make(0x2F, 0xB8, 0x7A),
        [FileCategory.Image] = Make(0xE0, 0x6C, 0xC0),
        [FileCategory.Audio] = Make(0xA9, 0x7A, 0xEA),
        [FileCategory.Video] = Make(0xEF, 0x62, 0x5E),
        [FileCategory.Archive] = Make(0xE8, 0x93, 0x3C),
        [FileCategory.Executable] = Make(0x2E, 0xB5, 0xC2),
        [FileCategory.Other] = Make(0x9A, 0xA0, 0xA6),
    };

    private static Brush Make(byte r, byte g, byte b)
    {
        var br = new SolidColorBrush(Color.FromRgb(r, g, b));
        br.Freeze();
        return br;
    }

    /// <summary>UI Automation 항목 이름으로 쓰이도록 파일 이름을 반환한다.</summary>
    public override string ToString() => Name;

    public string Name => Item.Name;
    public string FullPath => Item.FullPath;
    public bool IsDirectory => Item.IsDirectory;
    public string ModifiedText => Item.Modified.ToString("yyyy-MM-dd HH:mm");

    /// <summary>폴더의 집계 결과(계산 전 null).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText), nameof(CountText))]
    private FolderStats? _stats;

    /// <summary>파일은 자신의 크기, 폴더는 하위 전체 크기(일부 접근 불가면 '+' 표시).</summary>
    public string SizeText
    {
        get
        {
            if (!IsDirectory)
            {
                return SizeFormatter.Format(Item.Size);
            }
            if (Stats is null)
            {
                return CalculatingText;
            }
            return SizeFormatter.Format(Stats.TotalSize) + (Stats.Incomplete ? "+" : string.Empty) + (IsStale ? " (갱신 중)" : string.Empty);
        }
    }

    /// <summary>폴더의 하위 파일/폴더 수. 예: "파일 12 · 폴더 3".</summary>
    public string CountText => Stats is null ? string.Empty : $"파일 {Stats.FileCount:N0} · 폴더 {Stats.FolderCount:N0}";

    /// <summary>캐시에서 가져온 오래된 값이라 백그라운드에서 다시 계산 중인지.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText))]
    private bool _isStale;

    public void SetStats(FolderStats stats, bool stale = false)
    {
        IsStale = stale;
        Stats = stats;
    }
}

/// <summary>상위 단계 열의 폴더 한 칸.</summary>
public sealed class AncestorItemViewModel
{
    public AncestorItemViewModel(string name, string fullPath)
    {
        Name = name;
        FullPath = fullPath;
    }

    public string Name { get; }
    public string FullPath { get; }
    public override string ToString() => Name;
}

/// <summary>
/// Mdir 스타일의 상위 폴더 열 한 개: 어떤 폴더(<see cref="FolderPath"/>)의 하위 폴더 목록을 보여주고,
/// 현재 경로가 지나가는 폴더를 선택 상태로 표시한다. 사용자가 다른 폴더를 고르면 <c>onUserSelect</c>로 이동을 요청한다.
/// 프로그램이 선택을 바꾸는 중에는 이동을 요청하지 않는다(루프 방지). UI 스레드 전용.
/// </summary>
public sealed partial class AncestorColumnViewModel : ObservableObject
{
    private readonly Action<string> _onUserSelect;
    private bool _suppress;

    public AncestorColumnViewModel(string automationId, Action<string> onUserSelect)
    {
        AutomationId = automationId;
        _onUserSelect = onUserSelect;
    }

    public string AutomationId { get; }
    public ObservableCollection<AncestorItemViewModel> Items { get; } = new();

    /// <summary>열 머리글: 이 열이 나열하는 폴더의 이름(루트면 "C:\").</summary>
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private AncestorItemViewModel? _selectedItem;

    partial void OnSelectedItemChanged(AncestorItemViewModel? value)
    {
        if (!_suppress && value is not null)
        {
            _onUserSelect(value.FullPath);
        }
        else
        {
            // 프로그램이 바꾼 선택이거나 선택 해제: 이동 없음.
        }
    }

    /// <summary>열 내용을 교체한다. <paramref name="highlightPath"/>와 같은 항목을 선택 표시한다.</summary>
    public void Load(string? folderPath, IReadOnlyList<FileItem> folders, string? highlightPath)
    {
        _suppress = true;
        try
        {
            Items.Clear();
            Title = folderPath is null ? "—" : (Path.GetFileName(folderPath) is { Length: > 0 } n ? n : folderPath);
            AncestorItemViewModel? sel = null;
            foreach (FileItem f in folders)
            {
                var vm = new AncestorItemViewModel(f.Name, f.FullPath);
                Items.Add(vm);
                if (Core.Paths.PathNormalizer.AreSame(f.FullPath, highlightPath))
                {
                    sel = vm;
                }
            }
            SelectedItem = sel;
        }
        finally
        {
            _suppress = false;
        }
    }
}
