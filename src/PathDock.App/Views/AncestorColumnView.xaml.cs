using System.Windows.Controls;

namespace PathDock.App.Views;

/// <summary>상위 폴더 열 한 개. 조부모/부모 열이 같은 뷰를 재사용한다. 표시 전용.</summary>
public partial class AncestorColumnView : UserControl
{
    public AncestorColumnView()
    {
        InitializeComponent();
    }
}
