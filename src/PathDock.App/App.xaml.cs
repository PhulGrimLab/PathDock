using System.Windows;
using PathDock.App.ViewModels;
using PathDock.App.Views;
using PathDock.Core.Explorer;
using PathDock.Core.Localization;
using PathDock.Core.Logging;
using PathDock.Core.Settings;

namespace PathDock.App;

/// <summary>
/// 앱 진입점. 로거/설정/MainViewModel을 조립(수동 DI)하고 창을 띄운다.
/// Owner: WPF 런타임. Lifetime: 프로세스 전체.
/// </summary>
public partial class App : Application
{
    /// <summary>메인 창이 그려진 뒤 로딩 팝업을 계속 보여주는 시간.</summary>
    private static readonly TimeSpan SplashAfterMainShown = TimeSpan.FromSeconds(1);

    private AsyncFileLogger? _logger;
    private MainViewModel? _viewModel;
    private Services.SplashHost? _splash;

    /// <summary>프로세스가 시작된 뒤 지금까지 걸린 시간(ms). 시작이 느린 구간을 로그로 찾기 위한 진단용.</summary>
    private static long SinceProcessStartMs()
    {
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        return (long)(DateTime.Now - self.StartTime).TotalMilliseconds;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string dataDir = SettingsService.ResolveDataDir();
        _logger = new AsyncFileLogger(dataDir);
        _logger.Info($"시작 시간: OnStartup 진입 - 프로세스 시작 후 {SinceProcessStartMs()}ms");
        DispatcherUnhandledException += (_, args) =>
        {
            _logger.Error($"처리되지 않은 예외: {args.Exception}");
        };

        var settings = new SettingsService(dataDir, _logger);
        // 테마는 창이 만들어지기 전에 적용해야 첫 화면부터 올바른 색으로 그려진다.
        AppSettings loaded = settings.Load();
        Loc.SetLanguage(Loc.Parse(loaded.Language)); // 문구는 첫 화면부터 선택한 언어로 만들어야 한다.
        // 무거운 초기화(테마/뷰모델/메인 창 생성) 전에 로딩 팝업부터 띄운다. 문구가 언어를 따르므로 언어를 정한 뒤에 만든다.
        // 팝업은 전용 스레드에서 돈다: 아래 초기화가 이 스레드를 오래 막아도 진행 막대가 계속 움직이고 문구가 바뀐다.
        var splash = new Services.SplashHost();
        _splash = splash;
        splash.Start();
        _logger.Info($"시작 시간: 로딩 팝업 표시 - {SinceProcessStartMs()}ms");
        if (splash.FailureReason is { } reason)
        {
            _logger.Warn($"로딩 팝업을 띄우지 못함: {reason}");
        }
        splash.SetStatus(Loc.T("테마와 글꼴 적용 중...", "Applying theme and fonts..."));
        Services.ThemeService.Apply(loaded.Theme);
        Services.FontService.ApplyUi(loaded.UiFontSize);
        var sizeCache = new FolderSizeCache(dataDir, _logger);
        _viewModel = new MainViewModel(settings, sizeCache, _logger, splash.SetStatus);
        splash.SetStatus(Loc.T("화면 구성 중...", "Building the main window..."));
        var window = new MainWindow(_viewModel);
        MainWindow = window;
        // 메인 창이 실제로 그려진 뒤 1초 더 보여주고 닫는다. 그 사이 사용자가 창을 닫으면 팝업도 함께 닫는다.
        window.ContentRendered += (_, _) =>
        {
            _logger.Info($"시작 시간: 메인 창 표시 완료 - {SinceProcessStartMs()}ms");
            splash.SetStatus(Loc.T("준비 완료", "Ready"));
            splash.CloseAfter(SplashAfterMainShown);
        };
        window.Closed += (_, _) => splash.CloseNow();
        window.Show();
        _viewModel.Initialize(splash.SetStatus);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _splash?.CloseNow();
        _viewModel?.Dispose();
        _logger?.Info("앱 종료");
        _logger?.Dispose();
        base.OnExit(e);
    }
}
