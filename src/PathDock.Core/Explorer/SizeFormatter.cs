namespace PathDock.Core.Explorer;

/// <summary>바이트 수를 사람이 읽는 문자열(B/KB/MB/GB/TB, 1024 단위)로 바꾼다.</summary>
public static class SizeFormatter
{
    private const double Unit = 1024;
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

    public static string Format(long bytes)
    {
        double v = Math.Max(0, bytes);
        int i = 0;
        while (v >= Unit && i < Units.Length - 1)
        {
            v /= Unit;
            i++;
        }
        return i == 0 ? $"{(long)v} B" : $"{v:0.#} {Units[i]}";
    }
}
