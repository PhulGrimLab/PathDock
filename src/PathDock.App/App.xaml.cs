using System.Windows;
using PathDock.App.ViewModels;
using PathDock.App.Views;
using PathDock.Core.Explorer;
using PathDock.Core.Logging;
using PathDock.Core.Settings;

namespace PathDock.App;

/// <summary>
/// 앱 진입점. 로거/설정/MainViewModel을 조립(수동 DI)하고 창을 띄운다.
/// Owner: WPF 런타임. Lifetime: 프로세스 전체.
/// </summary>
public partial class App : Application
{
    private AsyncFileLogger? _logger;
    private MainViewModel? _viewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string dataDir = SettingsService.ResolveDataDir();
        _logger = new AsyncFileLogger(dataDir);
        DispatcherUnhandledException += (_, args) =>
        {
            _logger.Error($"처리되지 않은 예외: {args.Exception}");
        };

        var settings = new SettingsService(dataDir, _logger);
        // 테마는 창이 만들어지기 전에 적용해야 첫 화면부터 올바른 색으로 그려진다.
        AppSettings loaded = settings.Load();
        Services.ThemeService.Apply(loaded.Theme);
        Services.FontService.ApplyUi(loaded.UiFontSize);
        var sizeCache = new FolderSizeCache(dataDir, _logger);
        _viewModel = new MainViewModel(settings, sizeCache, _logger);
        var window = new MainWindow(_viewModel);
        MainWindow = window;
        window.Show();
        _viewModel.Initialize();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.Dispose();
        _logger?.Info("앱 종료");
        _logger?.Dispose();
        base.OnExit(e);
    }
}
