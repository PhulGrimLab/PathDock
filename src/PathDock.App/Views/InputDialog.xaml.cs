using System.Windows;
using PathDock.Core.Explorer;

namespace PathDock.App.Views;

/// <summary>
/// 한 줄 입력 대화상자(이름 변경/새 폴더). 입력값이 파일 이름 규칙에 맞지 않으면 닫지 않고 사유를 보여준다.
/// 로직은 <see cref="FileOperations.ValidateName"/>에 위임하고 code-behind에는 UI 흐름만 둔다.
/// </summary>
public partial class InputDialog : Window
{
    public InputDialog(string title, string label, string initialValue)
    {
        InitializeComponent();
        Title = title;
        LabelText.Text = label;
        InputBox.Text = initialValue;
        Loaded += (_, _) =>
        {
            InputBox.Focus();
            // 이름 변경 시 확장자를 제외한 부분만 선택해 바로 새 이름을 칠 수 있게 한다.
            int dot = initialValue.LastIndexOf('.');
            InputBox.Select(0, dot > 0 ? dot : initialValue.Length);
        };
    }

    public string Value => InputBox.Text.Trim();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        string? error = FileOperations.ValidateName(Value);
        if (error is not null)
        {
            ErrorText.Text = error;
            return;
        }
        DialogResult = true;
    }
}
