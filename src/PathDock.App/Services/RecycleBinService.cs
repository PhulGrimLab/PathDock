using System.Runtime.InteropServices;

namespace PathDock.App.Services;

/// <summary>모든 드라이브의 휴지통 비우기(Shell API). 정적 헬퍼, 호출 스레드 제한 없음(오래 걸릴 수 있으니 UI 스레드 밖에서 호출).</summary>
public static class RecycleBinService
{
    private const uint NoConfirmation = 0x1; // 확인은 앱이 이미 받았다.
    private const uint NoSound = 0x4;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? rootPath, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct ShQueryRbInfo
    {
        public int CbSize;
        public long Size;
        public long NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? rootPath, ref ShQueryRbInfo info);

    /// <summary>모든 드라이브 휴지통의 항목 수와 전체 크기. 조회에 실패하면 null.</summary>
    public static (long Items, long Bytes)? Query()
    {
        var info = new ShQueryRbInfo { CbSize = Marshal.SizeOf<ShQueryRbInfo>() };
        return SHQueryRecycleBin(null, ref info) == 0 ? (info.NumItems, info.Size) : null;
    }

    /// <summary>휴지통을 비운다. 실패하면 HRESULT 오류를 예외로 던진다. 이미 비어 있어도 성공으로 본다.</summary>
    public static void Empty()
    {
        int hr = SHEmptyRecycleBin(IntPtr.Zero, null, NoConfirmation | NoSound);
        const int EUnexpected = unchecked((int)0x8000FFFF); // 이미 비어 있을 때 일부 Windows가 돌려주는 값
        if (hr < 0 && hr != EUnexpected)
        {
            Marshal.ThrowExceptionForHR(hr);
        }
    }
}
