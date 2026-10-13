using System.IO;
using System.Windows.Media.Imaging;
using PathDock.Core.Explorer;

namespace PathDock.App.Services;

/// <summary>목록 툴팁에 보여줄 이미지 미리보기. Image는 고정(Freeze)되어 어느 스레드에서나 쓸 수 있다.</summary>
public sealed record ImagePreview(BitmapSource Image, int PixelWidth, int PixelHeight, long FileBytes)
{
    /// <summary>예: "1920×1080 · 239.2 KB".</summary>
    public string InfoText => $"{PixelWidth}×{PixelHeight} · {SizeFormatter.Format(FileBytes)}";
}

/// <summary>
/// 이미지 파일의 작은 미리보기(가로 <see cref="ThumbnailWidth"/>px)를 백그라운드에서 읽는다. 큰 사진도 줄여서 디코딩해 메모리를 아낀다.
/// 같은 파일(경로·수정 시각·크기가 같음)은 최근 <see cref="MaxCached"/>개까지 다시 읽지 않는다. 읽을 수 없으면 null.
/// 호출은 UI 스레드에서 하고, 읽기는 내부에서 스레드 풀로 보낸다(캐시 접근은 UI 스레드 전용).
/// </summary>
public static class ImagePreviewService
{
    public const int ThumbnailWidth = 240;
    private const int MaxCached = 40;

    private static readonly Dictionary<string, ImagePreview> Cache = new();
    private static readonly Queue<string> CacheOrder = new();

    public static async Task<ImagePreview?> LoadAsync(string path)
    {
        string? key = KeyOf(path);
        if (key is null)
        {
            return null;
        }
        if (Cache.TryGetValue(key, out ImagePreview? cached))
        {
            return cached;
        }
        ImagePreview? loaded = await Task.Run(() => Read(path));
        if (loaded is not null)
        {
            Remember(key, loaded);
        }
        return loaded;
    }

    private static string? KeyOf(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length <= ImagePreviewTypes.MaxFileBytes
                ? $"{path}|{info.LastWriteTimeUtc.Ticks}|{info.Length}"
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static void Remember(string key, ImagePreview preview)
    {
        if (Cache.TryAdd(key, preview))
        {
            CacheOrder.Enqueue(key);
            while (CacheOrder.Count > MaxCached)
            {
                Cache.Remove(CacheOrder.Dequeue());
            }
        }
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
                                       or ArgumentException or System.Runtime.InteropServices.COMException or FileFormatException)
        {
            return null;
        }
    }
}
