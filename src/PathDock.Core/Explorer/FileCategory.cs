namespace PathDock.Core.Explorer;

/// <summary>파일 종류 분류(목록 아이콘/색 선택용).</summary>
public enum FileCategory
{
    Folder,
    Code,
    Document,
    Data,
    Image,
    Audio,
    Video,
    Archive,
    Executable,
    Library,
    Other,
}

/// <summary>확장자로 파일 종류를 분류한다(상태 없음, 대소문자 무시). UI 아이콘 매핑은 앱 쪽 책임이다.</summary>
public static class FileCategorizer
{
    private static readonly Dictionary<string, FileCategory> Map = Build();

    public static FileCategory Categorize(string name, bool isDirectory)
    {
        if (isDirectory)
        {
            return FileCategory.Folder;
        }
        string ext = Path.GetExtension(name);
        return !string.IsNullOrEmpty(ext) && Map.TryGetValue(ext, out FileCategory c) ? c : FileCategory.Other;
    }

    private static Dictionary<string, FileCategory> Build()
    {
        var m = new Dictionary<string, FileCategory>(StringComparer.OrdinalIgnoreCase);
        void Add(FileCategory c, params string[] exts)
        {
            foreach (string e in exts)
            {
                m[e] = c;
            }
        }
        Add(FileCategory.Code, ".cs", ".vb", ".fs", ".c", ".cpp", ".cc", ".h", ".hpp", ".py", ".js", ".ts", ".tsx", ".jsx", ".java", ".kt",
            ".go", ".rs", ".swift", ".php", ".rb", ".lua", ".ps1", ".psm1", ".bat", ".cmd", ".sh", ".sql", ".xaml", ".html", ".htm", ".css",
            ".scss", ".xml", ".csproj", ".vcxproj", ".sln", ".slnx", ".props", ".targets");
        Add(FileCategory.Document, ".txt", ".md", ".rtf", ".doc", ".docx", ".pdf", ".ppt", ".pptx", ".hwp", ".hwpx", ".odt", ".log");
        Add(FileCategory.Data, ".json", ".yaml", ".yml", ".toml", ".ini", ".cfg", ".conf", ".csv", ".tsv", ".xls", ".xlsx", ".db", ".sqlite", ".config", ".resx");
        Add(FileCategory.Image, ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico", ".svg", ".tif", ".tiff", ".heic");
        Add(FileCategory.Audio, ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma");
        Add(FileCategory.Video, ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".flv");
        Add(FileCategory.Archive, ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz", ".nupkg", ".iso", ".cab");
        Add(FileCategory.Executable, ".exe", ".msi", ".lnk", ".com", ".appx", ".msix");
        Add(FileCategory.Library, ".dll");
        return m;
    }
}
