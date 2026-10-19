using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PathDock.Core.Localization;
using Wpf.Ui.Controls;

namespace PathDock.App.Controls;

/// <summary>
/// 주소 표시줄 커스텀 컨트롤: 아이콘 + 편집 가능한 경로 텍스트. Enter를 누르면 <see cref="Command"/>를 실행한다.
/// 텍스트는 양방향 바인딩(입력 즉시 반영)이다. 접근성/자동화용 ID·이름은 안쪽 TextBox에 붙도록
/// <see cref="TextBoxAutomationId"/> / <see cref="TextBoxAutomationName"/>으로 받는다(ValuePattern은 TextBox가 제공).
/// Owner: NavigationBarView(XAML). UI 스레드 전용.
/// </summary>
[TemplatePart(Name = PartTextBox, Type = typeof(System.Windows.Controls.TextBox))]
[TemplatePart(Name = PartCopyButton, Type = typeof(Wpf.Ui.Controls.Button))]
public class AddressBar : Control
{
    private const string PartTextBox = "PART_TextBox";
    private const string PartCopyButton = "PART_CopyButton";
    private static readonly TimeSpan CopyFeedbackDuration = TimeSpan.FromMilliseconds(1500);

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(AddressBar),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(AddressBar), new PropertyMetadata(null));

    public static readonly DependencyProperty SymbolProperty = DependencyProperty.Register(
        nameof(Symbol), typeof(SymbolRegular), typeof(AddressBar), new PropertyMetadata(SymbolRegular.FolderOpen24));

    public static readonly DependencyProperty TextBoxAutomationIdProperty = DependencyProperty.Register(
        nameof(TextBoxAutomationId), typeof(string), typeof(AddressBar), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TextBoxAutomationNameProperty = DependencyProperty.Register(
        nameof(TextBoxAutomationName), typeof(string), typeof(AddressBar), new PropertyMetadata(string.Empty));

    private System.Windows.Controls.TextBox? _textBox;
    private Wpf.Ui.Controls.Button? _copyButton;
    private DispatcherTimer? _copyFeedbackTimer;

    static AddressBar()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(AddressBar), new FrameworkPropertyMetadata(typeof(AddressBar)));
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Enter로 실행할 명령(매개변수 없음).</summary>
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public SymbolRegular Symbol
    {
        get => (SymbolRegular)GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
    }

    public string TextBoxAutomationId
    {
        get => (string)GetValue(TextBoxAutomationIdProperty);
        set => SetValue(TextBoxAutomationIdProperty, value);
    }

    public string TextBoxAutomationName
    {
        get => (string)GetValue(TextBoxAutomationNameProperty);
        set => SetValue(TextBoxAutomationNameProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (_textBox is not null)
        {
            _textBox.PreviewKeyDown -= OnTextBoxKeyDown;
        }
        if (_copyButton is not null)
        {
            _copyButton.Click -= OnCopyClick;
        }
        _copyButton = GetTemplateChild(PartCopyButton) as Wpf.Ui.Controls.Button;
        if (_copyButton is not null)
        {
            _copyButton.Click += OnCopyClick;
        }
        _textBox = GetTemplateChild(PartTextBox) as System.Windows.Controls.TextBox;
        if (_textBox is not null)
        {
            _textBox.PreviewKeyDown += OnTextBoxKeyDown;
        }
        else
        {
            // 템플릿에 PART_TextBox가 없으면 Enter 처리는 비활성(스타일 오류를 앱 중단으로 만들지 않는다).
        }
    }

    /// <summary>현재 경로(주소 표시줄에 보이는 글자)를 클립보드에 복사하고, 버튼 아이콘·툴팁으로 결과를 잠깐 알린다.</summary>
    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        string text = Text;
        if (string.IsNullOrEmpty(text) || _copyButton is null)
        {
            return;
        }
        bool copied = Services.ClipboardText.TryCopy(text); // 다른 프로그램이 클립보드를 잡고 있으면 false: 실패를 아이콘으로 알리고 앱은 계속 동작한다.
        _copyButton.Icon = new SymbolIcon(copied ? SymbolRegular.Checkmark24 : SymbolRegular.ErrorCircle24);
        _copyButton.ToolTip = copied
            ? Loc.T("복사했습니다", "Copied")
            : Loc.T("복사하지 못했습니다(클립보드 사용 중)", "Could not copy (clipboard is busy)");
        _copyFeedbackTimer ??= new DispatcherTimer(DispatcherPriority.Normal) { Interval = CopyFeedbackDuration };
        _copyFeedbackTimer.Tick -= OnCopyFeedbackEnd;
        _copyFeedbackTimer.Tick += OnCopyFeedbackEnd;
        _copyFeedbackTimer.Stop();
        _copyFeedbackTimer.Start();
    }

    private void OnCopyFeedbackEnd(object? sender, EventArgs e)
    {
        _copyFeedbackTimer?.Stop();
        if (_copyButton is not null)
        {
            _copyButton.Icon = new SymbolIcon(SymbolRegular.Copy24);
            _copyButton.ToolTip = Loc.T("현재 경로 복사", "Copy current path");
        }
    }

    private void OnTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Command is { } cmd && cmd.CanExecute(null))
        {
            cmd.Execute(null);
            e.Handled = true;
        }
        else
        {
            // 그 밖의 키는 TextBox 기본 편집 동작.
        }
    }
}
