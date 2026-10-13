namespace PathDock.Core.Explorer;

/// <summary>마우스를 올렸을 때 썸네일 미리보기를 보여줄 이미지 형식(WPF 기본 디코더가 읽는 형식). 상태 없음.</summary>
public static class ImagePreviewTypes
{
    /// <summary>미리보기를 읽을 최대 파일 크기. 이보다 크면 미리보기를 만들지 않는다.</summary>
    public const long MaxFileBytes = 200L * 1024 * 1024;

    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".tif", ".tiff", ".webp",
    };

    /// <summary>PDF 파일인지(Windows 내장 PDF 렌더러로 첫 쪽을 그려 같은 모양의 미리보기로 보여준다).</summary>
    public static bool IsPdf(string? fileName) =>
        !string.IsNullOrEmpty(fileName) && string.Equals(Path.GetExtension(fileName), ".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>파일 이름의 확장자가 미리보기 대상 이미지 형식인지.</summary>
    public static bool IsPreviewable(string? fileName)
    {
        return !string.IsNullOrEmpty(fileName) && Extensions.Contains(Path.GetExtension(fileName));
    }
}
