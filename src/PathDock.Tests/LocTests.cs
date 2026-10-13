using System.Globalization;
using PathDock.Core.Explorer;
using PathDock.Core.Localization;

namespace PathDock.Tests;

[TestClass]
public class LocTests
{
    // Loc은 프로세스 전체 상태라 각 테스트가 끝나면 기본(한국어)으로 되돌린다.
    [TestCleanup]
    public void Reset() => Loc.SetLanguage(AppLanguage.Korean);

    [TestMethod]
    public void T_PicksTextForCurrentLanguage_AndSetLanguageRaisesEventOnlyOnChange()
    {
        int raised = 0;
        void Handler() => raised++;
        Loc.LanguageChanged += Handler;
        try
        {
            Assert.AreEqual("닫기", Loc.T("닫기", "Close"));
            Loc.SetLanguage(AppLanguage.Korean); // 같은 언어: 알림 없음
            Assert.AreEqual(0, raised);
            Loc.SetLanguage(AppLanguage.English);
            Assert.AreEqual("Close", Loc.T("닫기", "Close"));
            Assert.AreEqual(1, raised);
        }
        finally
        {
            Loc.LanguageChanged -= Handler;
        }
    }

    [TestMethod]
    public void Parse_UsesSavedCode_ElseSystemLanguage()
    {
        var ko = new CultureInfo("ko-KR");
        var en = new CultureInfo("en-US");
        var ja = new CultureInfo("ja-JP");
        Assert.AreEqual(AppLanguage.English, Loc.Parse("en", ko));
        Assert.AreEqual(AppLanguage.Korean, Loc.Parse("KO", en));
        Assert.AreEqual(AppLanguage.Korean, Loc.Parse("", ko));
        Assert.AreEqual(AppLanguage.Korean, Loc.Parse(null, ko));
        Assert.AreEqual(AppLanguage.English, Loc.Parse("", en));
        Assert.AreEqual(AppLanguage.English, Loc.Parse("fr", ja)); // 모르는 값은 시스템 언어, 한국어가 아니면 영어
    }

    [TestMethod]
    public void ToCode_RoundTripsThroughParse()
    {
        Assert.AreEqual("ko", Loc.ToCode(AppLanguage.Korean));
        Assert.AreEqual("en", Loc.ToCode(AppLanguage.English));
        Assert.AreEqual(AppLanguage.English, Loc.Parse(Loc.ToCode(AppLanguage.English)));
    }

    [TestMethod]
    public void FileOperationMessages_FollowLanguage()
    {
        Assert.AreEqual("이름을 입력하세요.", FileOperations.ValidateName(""));
        Loc.SetLanguage(AppLanguage.English);
        Assert.AreEqual("Enter a name.", FileOperations.ValidateName(""));
        Assert.AreEqual("New folder", FileOperations.SuggestNewFolderName(Path.Combine(Path.GetTempPath(), "pd_loc_" + Guid.NewGuid().ToString("N"))));
    }
}
