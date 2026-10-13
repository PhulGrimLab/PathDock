using System.IO;
using System.Windows.Media.Imaging;
using PathDock.Core.Explorer;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PathDock.App.Services;

/// <summary>
/// PDF 첫 쪽의 작은 미리보기(가로 <see cref="ImagePreviewService.ThumbnailWidth"/>px)를 Windows 내장 PDF 렌더러(Windows.Data.Pdf)로 만든다.
/// 추가 프로그램이 필요 없다. 비밀번호가 걸렸거나 손상된 PDF, 너무 큰 파일은 null. 같은 파일은 최근 40개까지 다시 그리지 않는다.
/// 호출은 UI 스레드에서 하고, 렌더링은 비동기로 처리한다(캐시 접근은 UI 스레드 전용).
/// </summary>
public static class PdfPreviewService
{
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
        ImagePreview? loaded = await Task.Run(() => RenderFirstPageAsync(path));
        if (loaded is not null)
        {
            Cache.Add(key, loaded);
        }
        return loaded;
    }

    private static async Task<ImagePreview?> RenderFirstPageAsync(string path)
    {
        try
        {
            long bytes = new FileInfo(path).Length;
            StorageFile file = await StorageFile.GetFileFromPathAsync(path);
            PdfDocument document = await PdfDocument.LoadFromFileAsync(file);
            if (document.PageCount == 0)
            {
                return null;
            }
            using PdfPage page = document.GetPage(0);
            var options = new PdfPageRenderOptions
            {
                DestinationWidth = (uint)ImagePreviewService.ThumbnailWidth,
                BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255), // 투명한 쪽도 흰 종이로
            };
            using var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream, options);

            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = stream.AsStreamForRead();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return new ImagePreview(image, image.PixelWidth, image.PixelHeight, bytes, (int)document.PageCount);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException
                                       or InvalidOperationException or ArgumentException or NotSupportedException or FileFormatException)
        {
            return null; // 암호가 걸렸거나 손상된 PDF
        }
    }
}