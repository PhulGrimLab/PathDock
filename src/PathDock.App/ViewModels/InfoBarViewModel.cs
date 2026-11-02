using CommunityToolkit.Mvvm.ComponentModel;
using PathDock.Core.Localization;

namespace PathDock.App.ViewModels;

/// <summary>
/// 탐색기 아래에 겹쳐 뜨는 알림 막대(InfoBar)의 상태: 문구, 종류(성공 초록 / 알림·오류 경고색), 자동 닫힘.
/// Owner: MainViewModel, Lifetime: 앱 전체. UI 스레드 전용.
/// 성공은 5초, 경고/오류는 읽을 시간을 더 주고 10초 뒤 저절로 닫힌다(직접 닫기도 가능).
/// </summary>
public sealed partial class InfoBarViewModel : ObservableObject
{
    private const int SuccessSeconds = 5;
    private const int WarningSeconds = 10;

    private bool _nextIsSuccess;
    private System.Windows.Threading.DispatcherTimer? _timer;

    /// <summary>표시할 문구(비어 있으면 숨김).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInfo))]
    private string _message = string.Empty;

    /// <summary>알림 종류(성공은 초록, 그 밖의 알림/오류는 경고색).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private Wpf.Ui.Controls.InfoBarSeverity _severity = Wpf.Ui.Controls.InfoBarSeverity.Warning;

    /// <summary>제목(성공이면 "완료", 그 밖에는 "알림").</summary>
    public string Title => Severity == Wpf.Ui.Controls.InfoBarSeverity.Success ? Loc.T("완료", "Done") : Loc.T("알림", "Notice");

    public bool HasInfo => !string.IsNullOrEmpty(Message);

    /// <summary>알림/오류를 보인다(경고색, 10초 뒤 자동 닫힘).</summary>
    public void Show(string message)
    {
        Message = string.Empty; // 같은 문구도 다시 보이고 자동 닫힘 시간이 새로 시작되게 한다.
        Message = message;
    }

    /// <summary>작업이 잘 끝났음을 알린다. 초록 막대로 보였다가 몇 초 뒤 저절로 사라진다.</summary>
    public void ShowSuccess(string message)
    {
        Message = string.Empty; // 같은 문구도 다시 보이게 한다.
        _nextIsSuccess = true;
        Message = message;
    }

    public void Clear() => Message = string.Empty;

    /// <summary>언어가 바뀌었을 때 코드에서 만든 문구(제목)를 다시 읽게 한다.</summary>
    public void RefreshLanguage() => OnPropertyChanged(nameof(Title));

    partial void OnMessageChanged(string value)
    {
        _timer?.Stop();
        if (string.IsNullOrEmpty(value))
        {
            _nextIsSuccess = false;
            return;
        }
        bool success = _nextIsSuccess;
        _nextIsSuccess = false;
        Severity = success ? Wpf.Ui.Controls.InfoBarSeverity.Success : Wpf.Ui.Controls.InfoBarSeverity.Warning;
        if (_timer is null)
        {
            _timer = new System.Windows.Threading.DispatcherTimer();
            _timer.Tick += (_, _) =>
            {
                _timer.Stop();
                Message = string.Empty;
            };
        }
        _timer.Interval = TimeSpan.FromSeconds(success ? SuccessSeconds : WarningSeconds);
        _timer.Start();
    }
}
