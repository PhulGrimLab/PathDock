using System.Collections.Specialized;
using System.IO;
using System.Windows;

namespace PathDock.App.Services;

/// <summary>
/// 파일 복사/잘라내기용 클립보드(Windows 탐색기와 호환: FileDrop + Preferred DropEffect). UI 스레드 전용.
/// 클립보드를 다른 프로그램이 잡고 있으면 실패할 수 있으므로 호출 측이 false 결과를 처리한다.
/// </summary>
public static class FileClipboard
{
    private const string DropEffectFormat = "Preferred DropEffect";
    private const int CopyEffect = 5;
    private const int MoveEffect = 2;

    public static bool TrySet(IReadOnlyList<string> paths, bool cut)
    {
        try
        {
            var data = new DataObject();
            var list = new StringCollection();
            list.AddRange(paths.ToArray());
            data.SetFileDropList(list);
            data.SetData(DropEffectFormat, new MemoryStream(BitConverter.GetBytes(cut ? MoveEffect : CopyEffect)));
            Clipboard.SetDataObject(data, true);
            return true;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    /// <summary>클립보드의 파일 목록과 잘라내기 여부를 읽는다. 파일이 없으면 false.</summary>
    public static bool TryGet(out List<string> paths, out bool cut)
    {
        paths = new List<string>();
        cut = false;
        try
        {
            if (!Clipboard.ContainsFileDropList())
            {
                return false;
            }
            paths = Clipboard.GetFileDropList().Cast<string>().ToList();
            if (Clipboard.GetDataObject()?.GetData(DropEffectFormat) is MemoryStream ms && ms.Length >= 4)
            {
                cut = (BitConverter.ToInt32(ms.ToArray(), 0) & MoveEffect) != 0 && (BitConverter.ToInt32(ms.ToArray(), 0) & 1) == 0;
            }
            return paths.Count > 0;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    public static void Clear()
    {
        try
        {
            Clipboard.Clear();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // 다른 프로그램이 잡고 있음: 비우지 못해도 동작에는 영향 없다.
        }
    }
}