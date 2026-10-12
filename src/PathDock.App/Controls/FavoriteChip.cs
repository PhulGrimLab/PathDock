using System.Windows;
using System.Windows.Controls;

namespace PathDock.App.Controls;

/// <summary>
/// 즐겨찾기 한 칸을 나타내는 칩 버튼: 번호 배지 + 폴더 이름. 빈 칸(<see cref="IsEmpty"/>)은 흐리게 "+ 등록" 모양으로 보인다.
/// Button을 상속하므로 Command/컨텍스트 메뉴/접근성이 그대로 동작한다. Owner: FavoritesBarView(XAML).
/// </summary>
public class FavoriteChip : Button
{
    public static readonly DependencyProperty IndexProperty = DependencyProperty.Register(
        nameof(Index), typeof(int), typeof(FavoriteChip), new PropertyMetadata(0));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(FavoriteChip), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsEmptyProperty = DependencyProperty.Register(
        nameof(IsEmpty), typeof(bool), typeof(FavoriteChip), new PropertyMetadata(true));

    static FavoriteChip()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(FavoriteChip), new FrameworkPropertyMetadata(typeof(FavoriteChip)));
    }

    /// <summary>1부터 시작하는 슬롯 번호(배지에 표시).</summary>
    public int Index
    {
        get => (int)GetValue(IndexProperty);
        set => SetValue(IndexProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public bool IsEmpty
    {
        get => (bool)GetValue(IsEmptyProperty);
        set => SetValue(IsEmptyProperty, value);
    }
}
