using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace PathDock.App.Controls;

/// <summary>
/// 알림 막대(InfoBar)를 감싸 UI Automation(화면 낭독기)에 알림 문구를 노출한다.
/// InfoBar 자체는 자동화 요소를 만들지 않아 낭독기가 알림을 읽지 못했다. 문구가 바뀌면 LiveRegionChanged를 발생시켜 즉시 읽게 한다.
/// 문구가 비어 있으면(알림이 닫힘) 자동화 트리에서 숨긴다. Owner: MainWindow, Lifetime: 창 전체. UI 스레드 전용.
/// </summary>
public sealed class AlertHost : ContentControl
{
    public static readonly DependencyProperty AlertTextProperty = DependencyProperty.Register(
        nameof(AlertText), typeof(string), typeof(AlertHost), new PropertyMetadata(string.Empty, OnAlertTextChanged));

    /// <summary>현재 알림 문구(비어 있으면 알림 없음).</summary>
    public string AlertText
    {
        get => (string)GetValue(AlertTextProperty);
        set => SetValue(AlertTextProperty, value);
    }

    public AlertHost()
    {
        Focusable = false;
    }

    private static void OnAlertTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var host = (AlertHost)d;
        if (!string.IsNullOrEmpty(host.AlertText) && UIElementAutomationPeer.FromElement(host) is { } peer)
        {
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new AlertHostPeer(this);

    private sealed class AlertHostPeer(AlertHost owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override string GetClassNameCore() => nameof(AlertHost);

        protected override string GetNameCore() => owner.AlertText;

        protected override bool IsControlElementCore() => !string.IsNullOrEmpty(owner.AlertText);

        protected override bool IsContentElementCore() => IsControlElementCore();

        protected override AutomationLiveSetting GetLiveSettingCore() => AutomationLiveSetting.Assertive;
    }
}
