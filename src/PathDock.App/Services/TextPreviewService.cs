using PathDock.Core.Explorer;

namespace PathDock.App.Services;

/// <summary>
/// 텍스트/소스 파일 앞부분 미리보기를 백그라운드에서 읽는다(읽기는 Core의 <see cref="TextPreviewer"/>).
/// 같은 파일은 최근 40개까지 다시 읽지 않는다. 읽을 수 없거나 바이너리면 null. 호출은 UI 스레드에서 한다.
/// </summary>
public static class TextPreviewService
{
    private static readonly PreviewCache<TextPreview> Cache = new(40);

    public static async Task<TextPreview?> LoadAsync(string path)
    {
        string? key = PreviewCache<TextPreview>.KeyOf(path);
        if (key is null)
        {
            return null;
        }
        if (Cache.TryGet(key, out TextPreview? cached))
        {
            return cached;
        }
        TextPreview? loaded = await Task.Run(() => TextPreviewer.Read(path));
        if (loaded is not null)
        {
            Cache.Add(key, loaded);
        }
        return loaded;
    }
}