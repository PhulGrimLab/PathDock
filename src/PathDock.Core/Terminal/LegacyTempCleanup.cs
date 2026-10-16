namespace PathDock.Core.Terminal;

/// <summary>
/// 이전 버전이 임시 폴더(%TEMP%\PathDock)에 남긴 셸 통합 스크립트(shell-integration-*.ps1)를 지운다. 지금은 스크립트 파일을 만들지 않는다.
/// 다른 버전의 앱이 동시에 실행 중일 수 있으므로 오래된 파일만 지운다(상태 없음, 모든 실패는 무시).
/// </summary>
public static class LegacyTempCleanup
{
    /// <summary>이 시간보다 오래된 파일만 지운다. 이전 버전 앱은 시작할 때 스크립트를 한 번 읽고 끝이라, 한 시간이면 쓰는 중인 파일을 건드릴 일이 없다.</summary>
    public static readonly TimeSpan DefaultMinAge = TimeSpan.FromHours(1);

    /// <summary>오래된 스크립트를 지우고 지운 개수를 돌려준다. <paramref name="dir"/>이 null이면 %TEMP%\PathDock.</summary>
    public static int Run(string? dir = null, TimeSpan? minAge = null)
    {
        string folder = dir ?? Path.Combine(Path.GetTempPath(), "PathDock");
        int deleted = 0;
        try
        {
            if (!Directory.Exists(folder) || Paths.SafeIo.IsReparsePoint(folder))
            {
                return 0; // 링크로 바뀐 폴더 안의 파일은 지우지 않는다.
            }
            DateTime limit = DateTime.UtcNow - (minAge ?? DefaultMinAge);
            foreach (string file in Directory.EnumerateFiles(folder, "shell-integration-*.ps1"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < limit && !Paths.SafeIo.IsReparsePoint(file))
                    {
                        File.Delete(file);
                        deleted++;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // 사용 중이거나 권한이 없는 파일은 건너뛴다.
                }
            }
            if (!Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 정리는 보조 작업이다: 실패해도 실행에 영향이 없다.
        }
        return deleted;
    }
}
