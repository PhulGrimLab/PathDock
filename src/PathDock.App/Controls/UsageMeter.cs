using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace PathDock.App.Controls;

/// <summary>
/// 작은 사용률 표시기: "라벨 [막대] 값" 한 줄. 상태 표시줄의 CPU/GPU/메모리에 쓴다.
/// 값이 <see cref="WarningThreshold"/> 이상이면 경고색으로 바뀌고, <see cref="IsAvailable"/>=false면 막대를 비워 "N/A"를 보여준다.
/// Owner: StatusBarView(XAML). UI 스레드 전용. 접근성: 이름/ID를 UI Automation에 노출한다.
/// </summary>
public class UsageMeter : Control
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(UsageMeter), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(UsageMeter), new PropertyMetadata(0.0, OnValueChanged));

    public static readonly DependencyProperty ValueTextProperty = DependencyProperty.Register(
        nameof(ValueText), typeof(string), typeof(UsageMeter), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty WarningThresholdProperty = DependencyProperty.Register(
        nameof(WarningThreshold), typeof(double), typeof(UsageMeter), new PropertyMetadata(85.0, OnValueChanged));

    public static readonly DependencyProperty IsAvailableProperty = DependencyProperty.Register(
        nameof(IsAvailable), typeof(bool), typeof(UsageMeter), new PropertyMetadata(true));

    private static readonly DependencyPropertyKey IsWarningPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsWarning), typeof(bool), typeof(UsageMeter), new PropertyMetadata(false));

    public static readonly DependencyProperty IsWarningProperty = IsWarningPropertyKey.DependencyProperty;

    static UsageMeter()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(UsageMeter), new FrameworkPropertyMetadata(typeof(UsageMeter)));
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>0~100 사용률.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>막대 오른쪽에 표시할 문구(예: "12%", "8.1 / 15.7 GB").</summary>
    public string ValueText
    {
        get => (string)GetValue(ValueTextProperty);
        set => SetValue(ValueTextProperty, value);
    }

    public double WarningThreshold
    {
        get => (double)GetValue(WarningThresholdProperty);
        set => SetValue(WarningThresholdProperty, value);
    }

    /// <summary>측정값을 쓸 수 있는지(GPU 카운터가 없으면 false).</summary>
    public bool IsAvailable
    {
        get => (bool)GetValue(IsAvailableProperty);
        set => SetValue(IsAvailableProperty, value);
    }

    public bool IsWarning => (bool)GetValue(IsWarningProperty);

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var m = (UsageMeter)d;
        m.SetValue(IsWarningPropertyKey, m.Value >= m.WarningThreshold);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
}
