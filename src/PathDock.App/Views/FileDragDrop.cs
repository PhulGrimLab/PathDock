using System.Windows;
using System.Windows.Media;
using PathDock.Core.Explorer;

namespace PathDock.App.Views;

/// <summary>
/// 파일 끌어 놓기(드래그 앤 드롭) 공용 도우미. 파일은 표준 파일 목록 형식(FileDrop)으로 주고받는다.
/// 참고: 앱이 관리자 권한이면 Windows가 일반 권한 프로그램(탐색기 등)과의 끌어 놓기를 막으므로(UIPI), 실제로 동작하는 범위는 앱 안(목록·상위 열·터미널)이다.
/// </summary>
internal static class FileDragDrop
{
    /// <summary>끌어 온 데이터의 파일 경로들(없으면 빈 목록).</summary>
    public static IReadOnlyList<string> GetPaths(IDataObject data)
    {
        return data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] paths
            ? paths
            : Array.Empty<string>();
    }

    public static DragDropEffects ToWpf(DropEffect effect) => effect switch
    {
        DropEffect.Copy => DragDropEffects.Copy,
        DropEffect.Move => DragDropEffects.Move,
        _ => DragDropEffects.None,
    };

    public static bool CtrlHeld(DragEventArgs e) => (e.KeyStates & DragDropKeyStates.ControlKey) != 0;

    public static bool ShiftHeld(DragEventArgs e) => (e.KeyStates & DragDropKeyStates.ShiftKey) != 0;

    /// <summary>요소에서 위로 올라가며 처음 만나는 <typeparamref name="T"/>(비주얼이 아닌 요소는 논리 트리로 올라간다).</summary>
    public static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        for (DependencyObject? d = start; d is not null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is T found)
            {
                return found;
            }
        }
        return null;
    }
}
