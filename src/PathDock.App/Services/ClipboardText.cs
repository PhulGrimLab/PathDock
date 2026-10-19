using System.Runtime.InteropServices;
using System.Windows;

namespace PathDock.App.Services;

/// <summary>클립보드에 글자를 넣는다. 다른 프로그램이 클립보드를 잡고 있으면 잠깐 다시 시도하고, 그래도 안 되면 false. UI 스레드에서 호출한다.</summary>
public static class ClipboardText
{
    private const int Attempts = 4;
    private const int RetryDelayMs = 40;

    public static bool TryCopy(string text)
    {
        for (int i = 0; i < Attempts; i++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (Exception ex) when (ex is COMException or ExternalException)
            {
                Thread.Sleep(RetryDelayMs); // CLIPBRD_E_CANT_OPEN: 다른 프로그램이 잠시 사용 중
            }
        }
        return false;
    }
}
