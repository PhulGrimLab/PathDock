using System.IO;
using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using PathDock.Core.Explorer;
using PathDock.Core.Localization;
using Wpf.Ui.Controls;

namespace PathDock.App.ViewModels;

/// <summary>
/// 파일 목록 한 줄의 표시용 래퍼. 폴더는 전체 크기/하위 개수가 백그라운드에서 계산되어
/// <see cref="SetStats"/>로 채워진다(그 전에는 "계산 중…"). UI 스레드에서만 변경한다.
/// </summary>
public sealed partial class FileItemViewModel : ObservableObject
{
    public static string CalculatingText => Loc.T("계산 중…", "Calculating…");

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
        FileCategory.Library => SymbolRegular.PuzzlePiece24,
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
        [FileCategory.Library] = Make(0x7C, 0x8C, 0xF0),
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
            string size = OverLimit ? SizeFormatter.Format(FolderSizeCalculator.SizeLimit) + Loc.T(" 이상", " or more") : SizeFormatter.Format(Stats.TotalSize) + (Stats.Incomplete ? "+" : string.Empty);
            return size + (IsStale ? Loc.T(" (갱신 중)", " (updating)") : string.Empty);
        }
    }

    /// <summary>집계가 5GB 한도에서 멈췄는지(이 경우 크기와 개수는 "이상"으로 표시).</summary>
    public bool OverLimit => Stats is not null && Stats.TotalSize >= FolderSizeCalculator.SizeLimit;

    /// <summary>폴더의 하위 파일/폴더 수. 예: "파일 12 · 폴더 3", 1000개 이상은 "1천개 이상".</summary>
    public string CountText => Stats is null
        ? string.Empty
        : Loc.T($"파일 {CountPart(Stats.FileCount)} · 폴더 {CountPart(Stats.FolderCount)}",
            $"{CountPart(Stats.FileCount, "file", "files")} · {CountPart(Stats.FolderCount, "folder", "folders")}");

    /// <summary>개수 문구. 한국어는 숫자만("파일 {0}"), 영어는 단수/복수 단위를 붙인다. 1000개 이상은 "1천개 이상"/"1,000+".</summary>
    private string CountPart(int n, string singular = "", string plural = "")
    {
        string number = n >= 1000
            ? Loc.T("1천개 이상", "1,000+")
            : OverLimit ? Loc.T($"{n:N0}개 이상", $"{n:N0}+") : n.ToString("N0");
        if (Loc.IsKorean || singular.Length == 0)
        {
            return number;
        }
        return n >= 1000 || OverLimit ? $"{number} {plural}" : Loc.Plural(n, singular, plural);
    }

    /// <summary>마우스를 올리면 썸네일을 보여줄 이미지 파일인지.</summary>
    public bool IsImageFile => !IsDirectory && ImagePreviewTypes.IsPreviewable(Name);

    /// <summary>마우스를 올리면 앞부분을 보여줄 텍스트/소스 파일인지.</summary>
    public bool IsTextFile => !IsDirectory && TextPreviewer.IsPreviewable(Name);

    public bool IsPreviewable => IsImageFile || IsTextFile;

    /// <summary>툴팁 이미지 미리보기(처음 보여줄 때 읽어 채운다. 읽을 수 없으면 null로 남는다).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewInfo))]
    private Services.ImagePreview? _preview;

    /// <summary>툴팁 텍스트 미리보기(앞 20줄).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewInfo))]
    private TextPreview? _textPreview;

    private bool _previewFailed;

    /// <summary>툴팁 아래줄 문구. 예: "1920×1080 · 239.2 KB", "45줄 중 앞 20줄 · 1.2 KB". 아직 읽는 중이면 "불러오는 중…".</summary>
    public string PreviewInfo
    {
        get
        {
            if (Preview is { } image)
            {
                return image.InfoText;
            }
            if (TextPreview is { } text)
            {
                string size = SizeFormatter.Format(text.FileBytes);
                return text.TotalLines switch
                {
                    null => Loc.T($"앞 {TextPreviewer.MaxLines}줄 · {size}", $"First {TextPreviewer.MaxLines} lines · {size}"),
                    0 => Loc.T($"빈 파일 · {size}", $"Empty file · {size}"),
                    int n when n > TextPreviewer.MaxLines => Loc.T($"{n:N0}줄 중 앞 {TextPreviewer.MaxLines}줄 · {size}", $"First {TextPreviewer.MaxLines} of {n:N0} lines · {size}"),
                    int n => Loc.T($"{n:N0}줄 · {size}", $"{Loc.Plural(n, "line", "lines")} · {size}"),
                };
            }
            return Loc.T("불러오는 중…", "Loading…");
        }
    }

    /// <summary>미리보기를 읽는다. 이미 있거나 읽으면 true, 읽을 수 없는 파일이면 false(같은 항목은 다시 시도하지 않는다).</summary>
    public async Task<bool> LoadPreviewAsync()
    {
        if (Preview is not null || TextPreview is not null)
        {
            return true;
        }
        if (_previewFailed)
        {
            return false;
        }
        if (IsImageFile)
        {
            Preview = await Services.ImagePreviewService.LoadAsync(FullPath);
        }
        else if (IsTextFile)
        {
            TextPreview = await Services.TextPreviewService.LoadAsync(FullPath);
        }
        _previewFailed = Preview is null && TextPreview is null;
        return !_previewFailed;
    }
    /// <summary>캐시에서 가져온 오래된 값이라 백그라운드에서 다시 계산 중인지.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText))]
    private bool _isStale;

    /// <summary>언어가 바뀌었을 때 코드에서 만든 문구(크기·개수·미리보기 설명)를 다시 읽게 한다.</summary>
    public void RefreshLanguage() => OnPropertyChanged(string.Empty);

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
    private readonly bool _marksHiddenAncestors;

    public AncestorColumnViewModel(string automationId, Action<string> onUserSelect, bool marksHiddenAncestors = false)
    {
        AutomationId = automationId;
        _marksHiddenAncestors = marksHiddenAncestors;
        _onUserSelect = onUserSelect;
    }

    public string AutomationId { get; }
    public ObservableCollection<AncestorItemViewModel> Items { get; } = new();

    /// <summary>열 머리글: 이 열이 나열하는 폴더의 이름(루트면 "C:\").</summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>머리글에 "…\"가 붙은 상태(위에 가려진 폴더가 더 있음). 위로 가는 버튼 표시용.</summary>
    [ObservableProperty]
    private bool _hasHiddenAncestors;

    /// <summary>가려진 상위로 한 단계 올라간다(이 열의 폴더의 부모로 이동).</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void GoAbove()
    {
        if (FullPath.Length > 0 && Core.Explorer.ExplorerService.GetParent(FullPath) is { } parent)
        {
            _onUserSelect(parent);
        }
    }

    /// <summary>이 열 폴더의 전체 경로(머리글 툴팁). 비어 있으면 폴더 없음.</summary>
    [ObservableProperty]
    private string _fullPath = string.Empty;

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
            string name = folderPath is null ? "—" : (Path.GetFileName(folderPath) is { Length: > 0 } n ? n : folderPath);
            // 이 열의 왼쪽에 더 보여줄 열이 없는데 위에 폴더가 더 있으면 "…\"로 알린다.
            bool hasHiddenAncestors = _marksHiddenAncestors && folderPath is not null && Core.Explorer.ExplorerService.GetParent(folderPath) is not null;
            HasHiddenAncestors = hasHiddenAncestors;
            Title = hasHiddenAncestors ? $"…\\{name}" : name;
            FullPath = folderPath ?? string.Empty;
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
