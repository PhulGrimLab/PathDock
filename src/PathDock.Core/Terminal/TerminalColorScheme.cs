using System.Globalization;

namespace PathDock.Core.Terminal;

/// <summary>터미널 색 구성: 기본 배경/글자색(0xFFRRGGBB)과 ANSI 16색.</summary>
public sealed record TerminalColorScheme(string Key, string DisplayName, uint Background, uint Foreground, uint[] Ansi)
{
    public const string DefaultKey = "Campbell";
}

/// <summary>기본 제공 터미널 테마와 사용자 지정 색 해석. 상태가 없는 정적 헬퍼.</summary>
public static class TerminalThemes
{
    public static readonly IReadOnlyList<TerminalColorScheme> All = new[]
    {
        new TerminalColorScheme(TerminalColorScheme.DefaultKey, "기본 (어두움)", 0xFF0C0C0C, 0xFFCCCCCC, Enumerable.Range(0, 16).Select(Palette.Get).ToArray()),
        new TerminalColorScheme("OneDark", "One Dark", 0xFF282C34, 0xFFABB2BF, new uint[]
        {
            0xFF3F4451, 0xFFE06C75, 0xFF98C379, 0xFFE5C07B, 0xFF61AFEF, 0xFFC678DD, 0xFF56B6C2, 0xFFABB2BF,
            0xFF4F5666, 0xFFFF7A85, 0xFFA9D98A, 0xFFF0CB8B, 0xFF7BBFFF, 0xFFD68BEB, 0xFF6CC6D3, 0xFFFFFFFF,
        }),
        new TerminalColorScheme("Dracula", "Dracula", 0xFF282A36, 0xFFF8F8F2, new uint[]
        {
            0xFF21222C, 0xFFFF5555, 0xFF50FA7B, 0xFFF1FA8C, 0xFFBD93F9, 0xFFFF79C6, 0xFF8BE9FD, 0xFFF8F8F2,
            0xFF6272A4, 0xFFFF6E6E, 0xFF69FF94, 0xFFFFFFA5, 0xFFD6ACFF, 0xFFFF92DF, 0xFFA4FFFF, 0xFFFFFFFF,
        }),
        new TerminalColorScheme("SolarizedDark", "Solarized Dark", 0xFF002B36, 0xFF839496, new uint[]
        {
            0xFF073642, 0xFFDC322F, 0xFF859900, 0xFFB58900, 0xFF268BD2, 0xFFD33682, 0xFF2AA198, 0xFFEEE8D5,
            0xFF586E75, 0xFFE0603A, 0xFF8BA500, 0xFFD9A400, 0xFF4AA3E8, 0xFFE0509A, 0xFF4CBDB3, 0xFFFDF6E3,
        }),
        new TerminalColorScheme("Light", "밝음", 0xFFFFFFFF, 0xFF1E1E1E, new uint[]
        {
            0xFF000000, 0xFFCD3131, 0xFF00A000, 0xFF949800, 0xFF0451A5, 0xFFBC05BC, 0xFF0598BC, 0xFF555555,
            0xFF666666, 0xFFCD3131, 0xFF14CE14, 0xFFB5BA00, 0xFF0451A5, 0xFFBC05BC, 0xFF0598BC, 0xFFA5A5A5,
        }),
    };

    /// <summary>키로 테마를 찾는다. 모르는 키(손상된 설정 등)는 기본 테마.</summary>
    public static TerminalColorScheme Find(string? key) =>
        All.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    /// <summary>테마에 사용자가 지정한 글자색/배경색을 덮어쓴다. 비었거나 잘못된 색 문자열은 테마 값을 그대로 쓴다.</summary>
    public static TerminalColorScheme Resolve(string? key, string? foreground, string? background)
    {
        TerminalColorScheme t = Find(key);
        uint fg = TryParseColor(foreground) ?? t.Foreground;
        uint bg = TryParseColor(background) ?? t.Background;
        return t with { Foreground = fg, Background = bg };
    }

    /// <summary>"#RRGGBB" 또는 "RRGGBB"를 0xFFRRGGBB로. 형식이 다르면 null.</summary>
    public static uint? TryParseColor(string? text)
    {
        string s = (text ?? string.Empty).Trim().TrimStart('#');
        if (s.Length == 6 && uint.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint rgb))
        {
            return 0xFF000000u | rgb;
        }
        return null;
    }
}
