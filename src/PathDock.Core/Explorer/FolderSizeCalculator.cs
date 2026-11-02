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
    /// <summary>이 크기에 이르면 집계를 멈춘다. 이때 결과의 크기와 개수는 실제보다 작을 수 있다.</summary>
    public const long SizeLimit = 5L * 1024 * 1024 * 1024;

    public static FolderStats Calculate(string path, CancellationToken ct = default)
    {
        long total = 0;
        int files = 0, folders = 0;
        bool incomplete = false;

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false, // 접근 오류를 SizeEnumerator.ContinueOnError가 받아 "불완전"로 기록하고 건너뛴다.
            AttributesToSkip = FileAttributes.ReparsePoint, // 숨김/시스템은 크기에 포함한다.
            ReturnSpecialDirectories = false,
        };

        SizeEnumerator? enumerator = null;
        try
        {
            enumerator = new SizeEnumerator(path, options);
            while (enumerator.MoveNext())
            {
                (bool isDir, long length) = enumerator.Current;
                ct.ThrowIfCancellationRequested();
                if (isDir)
                {
                    folders++;
                }
                else
                {
                    files++;
                    total += length;
                    if (total >= SizeLimit)
                    {
                        break; // 5GB 이상이면 더 세지 않는다(표시는 "5 GB 이상").
                    }
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            // 루트 자체에 접근 불가/삭제됨: 지금까지의 합계만 돌려주고 불완전으로 표시한다.
            incomplete = true;
        }
        bool sawError = enumerator?.SawError ?? false;
        enumerator?.Dispose();
        return new FolderStats(total, files, folders, incomplete || sawError);
    }

    /// <summary>하위 항목 접근 오류를 건너뛰되 "불완전"으로 기록하는 열거자(IgnoreInaccessible은 오류를 조용히 삼켜 표시할 수 없다).</summary>
    private sealed class SizeEnumerator : FileSystemEnumerator<(bool IsDir, long Length)>
    {
        public bool SawError { get; private set; }

        public SizeEnumerator(string path, EnumerationOptions options) : base(path, options)
        {
        }

        protected override (bool IsDir, long Length) TransformEntry(ref FileSystemEntry entry) => (entry.IsDirectory, entry.IsDirectory ? 0 : entry.Length);

        protected override bool ContinueOnError(int error)
        {
            SawError = true;
            return true;
        }
    }
}
