using System.Globalization;

namespace PathDock.Core.Localization;

/// <summary>화면 언어.</summary>
public enum AppLanguage
{
    Korean,
    English,
}

/// <summary>
/// 화면 문구의 한국어/영어 선택. 문구는 코드에서 <c>Loc.T("한국어", "English")</c>처럼 두 언어를 나란히 적고, 현재 언어에 맞는 쪽을 돌려준다.
/// 언어를 바꾸면 <see cref="LanguageChanged"/>가 발생해 화면이 다시 그려진다. 상태는 프로세스 전체에서 하나이며 UI 스레드에서 바꾼다.
/// 개발자용 로그 문구는 번역하지 않는다.
/// </summary>
public static class Loc
{
    public static AppLanguage Current { get; private set; } = AppLanguage.Korean;

    public static bool IsKorean => Current == AppLanguage.Korean;

    /// <summary>언어가 실제로 바뀌었을 때 발생한다.</summary>
    public static event Action? LanguageChanged;

    /// <summary>현재 언어에 맞는 문구를 고른다.</summary>
    public static string T(string korean, string english) => IsKorean ? korean : english;

    /// <summary>영어 개수 표현의 단수/복수. 예: Plural(1, "file", "files") → "1 file", Plural(3, ...) → "3 files".</summary>
    public static string Plural(long n, string singular, string plural) => $"{n:N0} {(n == 1 ? singular : plural)}";

    public static void SetLanguage(AppLanguage language)
    {
        if (Current == language)
        {
            return;
        }
        Current = language;
        LanguageChanged?.Invoke();
    }

    /// <summary>저장된 설정값("ko"/"en")을 언어로. 비었거나 모르는 값이면 운영체제 표시 언어(한국어면 한국어, 그 밖에는 영어)를 따른다.</summary>
    public static AppLanguage Parse(string? code, CultureInfo? systemCulture = null)
    {
        if (string.Equals(code, "ko", StringComparison.OrdinalIgnoreCase))
        {
            return AppLanguage.Korean;
        }
        if (string.Equals(code, "en", StringComparison.OrdinalIgnoreCase))
        {
            return AppLanguage.English;
        }
        CultureInfo culture = systemCulture ?? CultureInfo.CurrentUICulture;
        return culture.TwoLetterISOLanguageName == "ko" ? AppLanguage.Korean : AppLanguage.English;
    }

    /// <summary>설정 파일에 저장할 코드.</summary>
    public static string ToCode(AppLanguage language) => language == AppLanguage.Korean ? "ko" : "en";
}
