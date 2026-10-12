using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace PathDock.App.Controls;

/// <summary>
/// 주소 표시줄 커스텀 컨트롤: 아이콘 + 편집 가능한 경로 텍스트. Enter를 누르면 <see cref="Command"/>를 실행한다.
/// 텍스트는 양방향 바인딩(입력 즉시 반영)이다. 접근성/자동화용 ID·이름은 안쪽 TextBox에 붙도록
/// <see cref="TextBoxAutomationId"/> / <see cref="TextBoxAutomationName"/>으로 받는다(ValuePattern은 TextBox가 제공).
/// Owner: NavigationBarView(XAML). UI 스레드 전용.
/// </summary>
[TemplatePart(Name = PartTextBox, Type = typeof(System.Windows.Controls.TextBox))]
public class AddressBar : Control
{
    private const string PartTextBox = "PART_TextBox";

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
