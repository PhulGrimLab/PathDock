namespace PathDock.Core.Explorer;

/// <summary>드라이브 한 개의 표시용 정보(단순 DTO). Total/Free가 0이면 준비되지 않은 드라이브(빈 CD 등)다.</summary>
public sealed record DriveEntry(string Root, string Label, DriveType Type, long TotalSize, long FreeSpace)
{
    public long UsedSize => Math.Max(0, TotalSize - FreeSpace);

    /// <summary>사용 비율(0~1). 용량을 모르면 0.</summary>
    public double UsedRatio => TotalSize > 0 ? (double)UsedSize / TotalSize : 0;
}

/// <summary>
/// 로컬/이동식/네트워크 드라이브 목록을 읽는다(상태 없음, 임의 스레드 호출 가능).
/// 준비되지 않았거나 조회 중 오류가 나는 드라이브는 용량 0으로 표시만 하고 목록에서 빼지 않는다.
/// </summary>
public static class DriveService
{
    public static List<DriveEntry> List()
    {
        var result = new List<DriveEntry>();
        foreach (DriveInfo d in DriveInfo.GetDrives())
        {
            if (d.DriveType is DriveType.Unknown or DriveType.NoRootDirectory)
            {
                continue;
            }
            result.Add(Describe(d));
        }
        return result;
    }

    private static DriveEntry Describe(DriveInfo d)
    {
        try
        {
            if (!d.IsReady)
            {
                return new DriveEntry(d.Name, string.Empty, d.DriveType, 0, 0);
            }
            return new DriveEntry(d.Name, d.VolumeLabel, d.DriveType, d.TotalSize, d.AvailableFreeSpace);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 조회 도중 끊긴 네트워크/이동식 드라이브: 용량 없이 이름만 표시한다.
            return new DriveEntry(d.Name, string.Empty, d.DriveType, 0, 0);
        }
    }

    /// <summary>경로가 속한 드라이브 루트(예: C:\). UNC 등 알 수 없으면 null.</summary>
    public static string? RootOf(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }
        string? root = Path.GetPathRoot(path);
        return string.IsNullOrEmpty(root) ? null : root;
    }
}
