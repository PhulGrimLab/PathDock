using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PathDock.Core.Localization;
using PathDock.Core.Settings;
using PathDock.Core.Terminal;

namespace PathDock.App.ViewModels;

/// <summary>설정 창의 터미널 테마 콤보박스 항목. 이름은 언어를 따른다(항목 객체를 바꾸지 않아 선택이 유지된다).</summary>
public sealed class TerminalThemeOption : ObservableObject
{
    private readonly TerminalColorScheme _scheme;

    public TerminalThemeOption(TerminalColorScheme scheme) => _scheme = scheme;

    public string Name => _scheme.Key switch
    {
        _ when _scheme.Key == TerminalColorScheme.DefaultKey => Loc.T("기본 (어두움)", "Default (dark)"),
        "Light" => Loc.T("밝음", "Light"),
        _ => _scheme.DisplayName,
    };

    public void RefreshName() => OnPropertyChanged(nameof(Name));
}

/// <summary>
/// 설정 창의 ViewModel: 언어, 화면/터미널 글자 크기, 시작 폴더, 터미널 셸·색, 즐겨찾기 초기화.
/// 값을 바꾸면 즉시 반영되고 설정(<see cref="AppSettings"/>)에 담긴다(파일 저장은 종료 때). Owner: MainViewModel, Lifetime: 앱 전체.
/// 설정 창이 이 객체를 DataContext로 쓴다 - 바인딩 이름은 창(XAML)과 맞춰져 있다. UI 스레드 전용.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly TerminalViewModel _terminal;
    private readonly Func<ExplorerViewModel> _explorer;
    private readonly FavoritesViewModel _favorites;
    private readonly Services.IDialogService _dialogs;
    private bool _revertingShell;

    public SettingsViewModel(AppSettings settings, TerminalViewModel terminal, Func<ExplorerViewModel> explorer, FavoritesViewModel favorites, Services.IDialogService dialogs)
    {
        _settings = settings;
        _terminal = terminal;
        _explorer = explorer;
        _favorites = favorites;
        _dialogs = dialogs;

        // 저장된 값을 화면 상태로 옮긴다(필드를 직접 정해 변경 알림/적용 동작은 일으키지 않는다).
        _startAtLastPath = settings.StartAtLastPath;
        _terminalThemeIndex = Math.Max(0, TerminalThemes.All.ToList().FindIndex(t => t.Key == TerminalThemes.Find(settings.TerminalTheme).Key));
        _terminalForeground = settings.TerminalForeground ?? string.Empty;
        _terminalBackground = settings.TerminalBackground ?? string.Empty;
        terminal.ColorScheme = TerminalThemes.Resolve(settings.TerminalTheme, _terminalForeground, _terminalBackground);
        _terminalShellIndex = settings.TerminalShell == TerminalShell.CommandPrompt ? 1 : 0;
        _uiFontSize = Services.FontService.NormalizeUi(settings.UiFontSize);
        _terminalFontSize = Services.FontService.NormalizeTerminal(settings.TerminalFontSize);
        terminal.FontSize = _terminalFontSize;
        _languageIndex = Loc.Current == AppLanguage.English ? 1 : 0;
    }

    /// <summary>시작 때 터미널이 이미 시작되었는지(그 전에는 셸을 바꿔도 다시 시작할 것이 없다). MainViewModel이 시작 후에 켠다.</summary>
    public bool TerminalStarted { get; set; }

    // ----- 글자 크기 -----

    /// <summary>화면 글자 크기. 바꾸면 즉시 반영되고 설정에 담긴다.</summary>
    [ObservableProperty]
    private double _uiFontSize;

    /// <summary>터미널 글자 크기. 바꾸면 즉시 반영되고 설정에 담긴다.</summary>
    [ObservableProperty]
    private double _terminalFontSize;

    partial void OnUiFontSizeChanged(double value)
    {
        _settings.UiFontSize = Services.FontService.NormalizeUi(value);
        Services.FontService.ApplyUi(_settings.UiFontSize);
    }

    partial void OnTerminalFontSizeChanged(double value)
    {
        _settings.TerminalFontSize = Services.FontService.NormalizeTerminal(value);
        _terminal.FontSize = _settings.TerminalFontSize;
    }

    /// <summary>글자 크기를 기본값으로 되돌린다.</summary>
    [RelayCommand]
    private void ResetFonts()
    {
        UiFontSize = AppSettings.DefaultUiFontSize;
        TerminalFontSize = AppSettings.DefaultTerminalFontSize;
    }

    // ----- 시작 폴더 -----

    /// <summary>시작 폴더: true면 마지막으로 열었던 폴더, false면 사용자 폴더.</summary>
    [ObservableProperty]
    private bool _startAtLastPath;

    partial void OnStartAtLastPathChanged(bool value) => _settings.StartAtLastPath = value;

    // ----- 터미널 셸 -----

    /// <summary>터미널 셸 선택(0=PowerShell, 1=명령 프롬프트). 바꾸면 선택된 탭이 새 셸로 다시 시작된다.</summary>
    [ObservableProperty]
    private int _terminalShellIndex;

    partial void OnTerminalShellIndexChanged(int oldValue, int newValue)
    {
        if (_revertingShell)
        {
            return;
        }
        TerminalShell shell = newValue == 1 ? TerminalShell.CommandPrompt : TerminalShell.PowerShell;
        if (TerminalStarted)
        {
            if (!_terminal.Session.IsIdle
                && !_dialogs.Confirm(Loc.T("터미널 셸 변경", "Change terminal shell"), Loc.T("실행 중인 작업이 있습니다. 터미널을 새 셸로 다시 시작하면 종료됩니다. 계속할까요?", "A task is still running. Restarting the terminal with the new shell will end it. Continue?")))
            {
                // 콤보박스 갱신이 끝난 뒤 이전 값으로 되돌린다.
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    _revertingShell = true;
                    TerminalShellIndex = oldValue;
                    _revertingShell = false;
                });
                return;
            }
            _terminal.RestartShell(_terminal.CurrentPath ?? _explorer().CurrentPath, shell);
        }
        _settings.TerminalShell = shell;
    }

    // ----- 언어 -----

    /// <summary>설정 창의 언어 선택(0=한국어, 1=English). 바꾸면 화면 전체가 즉시 바뀌고 설정에 담긴다.</summary>
    [ObservableProperty]
    private int _languageIndex;

    partial void OnLanguageIndexChanged(int value)
    {
        AppLanguage language = value == 1 ? AppLanguage.English : AppLanguage.Korean;
        _settings.Language = Loc.ToCode(language);
        Loc.SetLanguage(language);
    }

    /// <summary>언어가 바뀐 뒤 호출: 코드에서 언어를 바꾼 경우에도 선택을 맞추고, 코드로 만든 문구(테마 이름, 색 입력 안내)를 다시 읽게 한다.</summary>
    public void RefreshLanguage()
    {
        LanguageIndex = Loc.Current == AppLanguage.English ? 1 : 0;
        foreach (TerminalThemeOption option in TerminalThemeOptions)
        {
            option.RefreshName();
        }
        OnPropertyChanged(string.Empty);
    }

    // ----- 터미널 색 -----

    /// <summary>설정 창 콤보박스에 보여줄 터미널 테마(TerminalThemes.All과 같은 순서).</summary>
    public IReadOnlyList<TerminalThemeOption> TerminalThemeOptions { get; } = TerminalThemes.All.Select(t => new TerminalThemeOption(t)).ToList();

    [ObservableProperty]
    private int _terminalThemeIndex;

    /// <summary>터미널 글자색(#RRGGBB). 비우면 테마 기본값.</summary>
    [ObservableProperty]
    private string _terminalForeground = string.Empty;

    /// <summary>터미널 배경색(#RRGGBB). 비우면 테마 기본값.</summary>
    [ObservableProperty]
    private string _terminalBackground = string.Empty;

    partial void OnTerminalThemeIndexChanged(int value) => ApplyTerminalColors();

    partial void OnTerminalForegroundChanged(string value)
    {
        ApplyTerminalColors();
        OnPropertyChanged(nameof(ColorErrorText));
        OnPropertyChanged(nameof(HasColorError));
    }

    partial void OnTerminalBackgroundChanged(string value)
    {
        ApplyTerminalColors();
        OnPropertyChanged(nameof(ColorErrorText));
        OnPropertyChanged(nameof(HasColorError));
    }

    /// <summary>글자색/배경색 입력이 #RRGGBB 형식이 아닐 때의 안내(올바르면 빈 문자열).</summary>
    public string ColorErrorText
    {
        get
        {
            var bad = new List<string>();
            if (!string.IsNullOrWhiteSpace(TerminalForeground) && TerminalThemes.TryParseColor(TerminalForeground) is null)
            {
                bad.Add(Loc.T("글자색", "Text color"));
            }
            if (!string.IsNullOrWhiteSpace(TerminalBackground) && TerminalThemes.TryParseColor(TerminalBackground) is null)
            {
                bad.Add(Loc.T("배경색", "Background color"));
            }
            return bad.Count == 0 ? string.Empty : Loc.T(
                $"{string.Join("·", bad)} 형식이 올바르지 않습니다. #RRGGBB 형식(예: #FFD700)으로 입력하세요. 그 전까지는 테마 색을 씁니다.",
                $"{string.Join(", ", bad)}: invalid format. Enter it as #RRGGBB (for example #FFD700). Until then the theme color is used.");
        }
    }

    public bool HasColorError => ColorErrorText.Length > 0;

    private void ApplyTerminalColors()
    {
        int index = Math.Clamp(TerminalThemeIndex, 0, TerminalThemes.All.Count - 1);
        _settings.TerminalTheme = TerminalThemes.All[index].Key;
        _settings.TerminalForeground = TerminalForeground;
        _settings.TerminalBackground = TerminalBackground;
        _terminal.ColorScheme = TerminalThemes.Resolve(_settings.TerminalTheme, TerminalForeground, TerminalBackground);
        OnPropertyChanged(nameof(TerminalForegroundPreview));
        OnPropertyChanged(nameof(TerminalBackgroundPreview));
    }

    /// <summary>설정 창 색 입력 옆 미리보기: 지금 터미널에 적용되는 글자색/배경색(비었거나 잘못되면 테마 기본값).</summary>
    public System.Windows.Media.Brush TerminalForegroundPreview => PreviewBrush(_terminal.ColorScheme.Foreground);

    public System.Windows.Media.Brush TerminalBackgroundPreview => PreviewBrush(_terminal.ColorScheme.Background);

    private static System.Windows.Media.Brush PreviewBrush(uint argb)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        brush.Freeze();
        return brush;
    }

    /// <summary>터미널 색을 기본 테마로 되돌린다.</summary>
    [RelayCommand]
    private void ResetTerminalColors()
    {
        TerminalThemeIndex = 0;
        TerminalForeground = string.Empty;
        TerminalBackground = string.Empty;
    }

    // ----- 즐겨찾기 -----

    /// <summary>즐겨찾기 10칸을 모두 비운다(확인 후).</summary>
    [RelayCommand]
    private void ResetFavorites() => _favorites.ResetAll();
}
