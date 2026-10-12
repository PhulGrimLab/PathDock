namespace PathDock.Core.Paths;

/// <summary>
/// 경로 문자열을 비교/전파 가능한 정규 형태로 바꾸는 유틸리티(상태 없음).
/// 규칙: 전체 경로화, 후행 구분자 제거(루트 제외), 드라이브 문자 대문자화, UNC 보존.
/// </summary>
public static class PathNormalizer
{
    private static readonly char[] InvalidChars = { '<', '>', '|', '*', '?', '"' };

    /// <summary>입력 경로를 정규화한다. 비어 있거나 잘못된 경로면 null을 반환한다.</summary>
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string trimmed = path.Trim().Trim('"');
        // .NET Core의 GetFullPath는 < > | * ? 를 검사하지 않으므로 직접 거른다(긴 경로 접두사 \\?\ 는 허용).
        bool isLongPrefix = trimmed.StartsWith(@"\\?\", StringComparison.Ordinal);
        if (trimmed.AsSpan(isLongPrefix ? 4 : 0).IndexOfAny(InvalidChars) >= 0)
        {
            return null;
        }

        string full;
        try
        {
            full = Path.GetFullPath(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // 잘못된 문자/형식 - 호출 측이 "경로 없음"으로 처리하도록 null 반환.
            return null;
        }

        string root = Path.GetPathRoot(full) ?? string.Empty;
        if (full.Length > root.Length)
        {
            full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        if (full.Length >= 2 && full[1] == ':' && char.IsLower(full[0]))
        {
            full = char.ToUpperInvariant(full[0]) + full[1..];
        }
        return full;
    }

    /// <summary>두 경로가 (대소문자 무시, 정규화 후) 같은 위치인지 비교한다. 하나라도 null이면 false.</summary>
    public static bool AreSame(string? a, string? b)
    {
        string? na = Normalize(a);
        string? nb = Normalize(b);
        return na is not null && nb is not null && string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
    }
}
