using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace PathDock.App.Controls;

/// <summary>
/// 드라이브 한 칸을 보여주는 카드 버튼: 아이콘 + 이름, 사용량 막대, 용량 문구.
/// 현재 탐색 중인 드라이브(<see cref="IsCurrent"/>)는 강조 테두리, 사용률이 높으면(<see cref="IsLow"/>) 막대가 경고색이 된다.
/// Button을 상속하므로 Command/클릭/접근성이 그대로 동작한다. Owner: DrivePanelView(XAML).
/// </summary>
public class DriveCard : System.Windows.Controls.Button
{
    public static readonly DependencyProperty SymbolProperty = DependencyProperty.Register(
        nameof(Symbol), typeof(SymbolRegular), typeof(DriveCard), new PropertyMetadata(SymbolRegular.HardDrive20));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(DriveCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DetailProperty = DependencyProperty.Register(
        nameof(Detail), typeof(string), typeof(DriveCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty UsedPercentProperty = DependencyProperty.Register(
        nameof(UsedPercent), typeof(double), typeof(DriveCard), new PropertyMetadata(0.0));

    public static readonly DependencyProperty IsLowProperty = DependencyProperty.Register(
        nameof(IsLow), typeof(bool), typeof(DriveCard), new PropertyMetadata(false));

    public static readonly DependencyProperty IsCurrentProperty = DependencyProperty.Register(
        nameof(IsCurrent), typeof(bool), typeof(DriveCard), new PropertyMetadata(false));

    static DriveCard()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(DriveCard), new FrameworkPropertyMetadata(typeof(DriveCard)));
    }

    public SymbolRegular Symbol
    {
        get => (SymbolRegular)GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>용량 문구(예: "239.5 GB 사용 / 475.8 GB (50%)").</summary>
    public string Detail
    {
        get => (string)GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    public double UsedPercent
    {
        get => (double)GetValue(UsedPercentProperty);
        set => SetValue(UsedPercentProperty, value);
    }

    public bool IsLow
    {
        get => (bool)GetValue(IsLowProperty);
        set => SetValue(IsLowProperty, value);
    }

    public bool IsCurrent
    {
        get => (bool)GetValue(IsCurrentProperty);
        set => SetValue(IsCurrentProperty, value);
    }
}
