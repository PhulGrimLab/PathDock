using System.IO.Enumeration;

namespace PathDock.Core.Explorer;

/// <summary>폴더 하나의 재귀 집계 결과. Incomplete=true면 접근 불가 항목이 있어 실제보다 작을 수 있다.</summary>
public sealed record FolderStats(long TotalSize, int FileCount, int FolderCount, bool Incomplete);

/// <summary>
/// 폴더의 전체 크기와 하위 파일/폴더 수를 재귀 집계한다(상태 없음, 임의 스레드 호출 가능, 취소 가능).
/// 재분석 지점(junction/symlink)은 따라가지 않아 순환과 중복 집계를 피한다. 접근 불가 항목은 건너뛰고 Incomplete로 표시한다.
/// 큰 폴더는 오래 걸리므로 호출 측이 백그라운드에서 실행하고 CancellationToken으로 중단해야 한다.
/// </summary>
public static class FolderSizeCalculator
{
    public static FolderStats Calculate(string path, CancellationToken ct = default)
    {
        long total = 0;
        int files = 0, folders = 0;
        bool incomplete = false;

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint, // 숨김/시스템은 크기에 포함한다.
            ReturnSpecialDirectories = false,
        };

        try
        {
            var enumerable = new FileSystemEnumerable<(bool IsDir, long Length)>(path,
                (ref FileSystemEntry e) => (e.IsDirectory, e.IsDirectory ? 0 : e.Length), options);
            foreach ((bool isDir, long length) in enumerable)
            {
                ct.ThrowIfCancellationRequested();
                if (isDir)
                {
                    folders++;
                }
                else
                {
                    files++;
                    total += length;
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            // 루트 자체에 접근 불가/삭제됨: 지금까지의 합계만 돌려주고 불완전으로 표시한다.
            incomplete = true;
        }
        return new FolderStats(total, files, folders, incomplete);
    }
}
