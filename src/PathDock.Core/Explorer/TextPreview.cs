using System.Text;

namespace PathDock.Core.Explorer;

/// <summary>텍스트 미리보기 결과. TotalLines는 파일 전체를 읽었을 때만 알 수 있다(더 큰 파일이면 null).</summary>
public sealed record TextPreview(string Text, int? TotalLines, long FileBytes);

/// <summary>
/// 마우스를 올렸을 때 앞부분을 보여줄 텍스트/소스 파일 형식과, 앞부분을 읽는 순수 함수. 상태 없음, 임의 스레드 호출 가능.
/// 앞쪽 <see cref="MaxReadBytes"/>만 읽으므로 큰 로그도 빠르다. 실제로는 바이너리인 파일은 미리보기를 만들지 않는다(null).
/// </summary>
public static class TextPreviewer
{
    public const int MaxReadBytes = 64 * 1024;
    public const int MaxLines = 20;
    public const int MaxLineChars = 120;

    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".log", ".json", ".xml", ".csv", ".ini", ".yaml", ".yml",
        ".cs", ".py", ".js", ".ts", ".html", ".css", ".sql", ".sh", ".bat", ".ps1",
    };

    /// <summary>파일 이름의 확장자가 미리보기 대상 텍스트 형식인지.</summary>
    public static bool IsPreviewable(string? fileName)
    {
        return !string.IsNullOrEmpty(fileName) && Extensions.Contains(Path.GetExtension(fileName));
    }

    /// <summary>앞부분을 읽어 미리보기를 만든다. 읽을 수 없거나(IO 오류) 바이너리로 보이면 null.</summary>
    public static TextPreview? Read(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long length = stream.Length;
            var buffer = new byte[(int)Math.Min(length, MaxReadBytes)];
            int read = 0;
            while (read < buffer.Length)
            {
                int n = stream.Read(buffer, read, buffer.Length - read);
                if (n == 0)
                {
                    break;
                }
                read += n;
            }
            bool wholeFile = read >= length;
            string? text = Decode(buffer.AsSpan(0, read), wholeFile);
            return text is null ? null : Build(text, wholeFile, length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>BOM이 있으면 UTF-8/UTF-16, 없으면 UTF-8로 해석한다. NUL 바이트가 있으면 바이너리로 보고 null.</summary>
    private static string? Decode(ReadOnlySpan<byte> bytes, bool wholeFile)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes[2..]);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes[2..]);
        }
        if (bytes.Contains((byte)0))
        {
            return null;
        }
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            bytes = bytes[3..];
        }
        string text = Encoding.UTF8.GetString(bytes);
        // 읽다가 끊긴 마지막 글자는 깨진 문자(U+FFFD)로 나오므로 잘라낸다.
        return !wholeFile && text.EndsWith('�') ? text[..^1] : text;
    }

    private static TextPreview Build(string text, bool wholeFile, long fileBytes)
    {
        string[] lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        int count = lines.Length;
        if (count > 0 && lines[^1].Length == 0)
        {
            count--; // 끝의 줄바꿈 뒤에 생기는 빈 줄은 세지 않는다.
        }
        var shown = new StringBuilder();
        for (int i = 0; i < Math.Min(count, MaxLines); i++)
        {
            if (i > 0)
            {
                shown.Append('\n');
            }
            string line = lines[i].Replace("\t", "    ");
            shown.Append(line.Length > MaxLineChars ? line[..MaxLineChars] + "…" : line);
        }
        return new TextPreview(shown.ToString(), wholeFile ? count : null, fileBytes);
    }
}
