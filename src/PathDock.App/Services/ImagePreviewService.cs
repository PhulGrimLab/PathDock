using System.IO;
using System.Windows.Media.Imaging;
using PathDock.Core.Explorer;
using PathDock.Core.Localization;

namespace PathDock.App.Services;

/// <summary>목록 툴팁에 보여줄 이미지 미리보기. Image는 고정(Freeze)되어 어느 스레드에서나 쓸 수 있다.</summary>
public sealed record ImagePreview(BitmapSource Image, int PixelWidth, int PixelHeight, long FileBytes, int PageCount = 0)
{
    /// <summary>예: "1920×1080 · 239.2 KB". PDF는 "7쪽 · 1.2 MB".</summary>
    public string InfoText => PageCount > 0
        ? Loc.T($"{PageCount:N0}쪽 · {SizeFormatter.Format(FileBytes)}", $"{Loc.Plural(PageCount, "page", "pages")} · {SizeFormatter.Format(FileBytes)}")
        : $"{PixelWidth}×{PixelHeight} · {SizeFormatter.Format(FileBytes)}";
}

/// <summary>
/// 이미지 파일의 작은 미리보기(가로 <see cref="ThumbnailWidth"/>px)를 백그라운드에서 읽는다. 큰 사진도 줄여서 디코딩해 메모리를 아낀다.
/// 같은 파일은 최근 40개까지 다시 읽지 않는다(<see cref="PreviewCache{T}"/>). 읽을 수 없으면 null.
/// 호출은 UI 스레드에서 하고, 읽기는 내부에서 스레드 풀로 보낸다(캐시 접근은 UI 스레드 전용).
/// </summary>
public static class ImagePreviewService
{
    public const int ThumbnailWidth = 240;
    private static readonly PreviewCache<ImagePreview> Cache = new(40);

    public static async Task<ImagePreview?> LoadAsync(string path)
    {
        string? key = PreviewCache<ImagePreview>.KeyOf(path, ImagePreviewTypes.MaxFileBytes);
        if (key is null)
        {
            return null;
        }
        if (Cache.TryGet(key, out ImagePreview? cached))
        {
            return cached;
        }
        ImagePreview? loaded = await Task.Run(() => Read(path));
        if (loaded is not null)
        {
            Cache.Add(key, loaded);
        }
        return loaded;
    }
    /// <summary>스레드 풀에서 실행: 원본 크기를 읽고, 가로가 넘치면 줄여서 디코딩한다. 손상/미지원 형식이면 null.</summary>
    private static ImagePreview? Read(string path)
    {
        try
        {
            long bytes = new FileInfo(path).Length;
            int width, height;
            using (FileStream probe = File.OpenRead(path))
            {
                BitmapFrame frame = BitmapDecoder.Create(probe, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None).Frames[0];
                width = frame.PixelWidth;
                height = frame.PixelHeight;
            }

            var image = new BitmapImage();
            using (FileStream stream = File.OpenRead(path))
            {
                image.BeginInit();
                image.StreamSource = stream;
                image.CacheOption = BitmapCacheOption.OnLoad; // 스트림을 닫아도 되도록 즉시 메모리로 읽는다.
                if (width > ThumbnailWidth)
                {
                    image.DecodePixelWidth = ThumbnailWidth;
                }
                image.EndInit();
            }
            image.Freeze();
            return new ImagePreview(image, width, height, bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException
                                       or ArgumentException or System.Runtime.InteropServices.COMException or FileFormatException
                                       or OutOfMemoryException) // 해상도가 비정상적으로 큰 이미지: 미리보기만 포기한다(앱이 종료되지 않게).
        {
            return null;
        }
    }
}
