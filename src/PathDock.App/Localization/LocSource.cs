using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using PathDock.Core.Localization;

namespace PathDock.App.Localization;

/// <summary>
/// XAML 문구의 한국어/영어 선택. 사용: <c>Text="{loc:T '드라이브', 'Drives'}"</c>.
/// 언어를 바꾸면 이 확장으로 만든 모든 바인딩이 즉시 새 문구로 바뀐다(앱 재시작 불필요).
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TExtension : MarkupExtension
{
    private readonly string _korean;
    private readonly string _english;

    public TExtension(string korean, string english)
    {
        _korean = korean;
        _english = english;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        int id = LocSource.Register(_korean, _english);
        return new Binding($"[{id}]") { Source = LocSource.Instance, Mode = BindingMode.OneWay }
            .ProvideValue(serviceProvider);
    }
}

/// <summary>XAML 문구 바인딩의 원본. 인덱서 번호는 <see cref="Register"/>가 문구 쌍마다 하나씩 준다. 언어가 바뀌면 모든 인덱서를 갱신 알림한다.</summary>
public sealed class LocSource : INotifyPropertyChanged
{
    private static readonly List<(string Ko, string En)> Pairs = new();
    private static readonly Dictionary<(string, string), int> Ids = new();

    public static LocSource Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private LocSource()
    {
        Loc.LanguageChanged += () =>
        {
            void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(Notify);
            }
            else
            {
                Notify();
            }
        };
    }

    internal static int Register(string korean, string english)
    {
        lock (Pairs)
        {
            if (Ids.TryGetValue((korean, english), out int id))
            {
                return id;
            }
            Pairs.Add((korean, english));
            Ids[(korean, english)] = Pairs.Count - 1;
            return Pairs.Count - 1;
        }
    }

    public string this[int id]
    {
        get
        {
            lock (Pairs)
            {
                (string ko, string en) = Pairs[id];
                return Loc.T(ko, en);
            }
        }
    }
}