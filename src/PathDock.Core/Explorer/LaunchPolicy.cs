namespace PathDock.Core.Explorer;

/// <summary>
/// 파일을 "열" 때 어떤 권한으로 열지 정하는 규칙. 앱은 관리자 권한으로 실행되므로, 그대로 연결 프로그램을 띄우면 관리자 권한 프로세스가 되어
/// 일부 프로그램(예: Adobe Acrobat/Reader, 브라우저)이 아무 반응 없이 열리지 않을 수 있다. 그래서 문서·이미지 같은 파일은 탐색기와 같은 일반 사용자 권한으로 열고,
/// 실행 파일/스크립트는 기존처럼 앱의 권한으로 실행한다. 상태 없음.
/// </summary>
public static class LaunchPolicy
{
    private static readonly HashSet<string> RunAsAppExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".bat", ".cmd", ".msi", ".msc", ".scr", ".cpl", ".ps1", ".vbs", ".vbe", ".wsf", ".lnk",
    };

    /// <summary>true면 파일을 일반 사용자 권한(탐색기를 통해)으로 연다. 실행 파일/스크립트는 false(앱의 권한으로 실행).</summary>
    public static bool OpensAsNormalUser(string fileName)
    {
        string ext = Path.GetExtension(fileName);
        return ext.Length > 0 && !RunAsAppExtensions.Contains(ext);
    }
}