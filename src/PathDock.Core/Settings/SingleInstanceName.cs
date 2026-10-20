using System.Security.Cryptography;
using System.Text;

namespace PathDock.Core.Settings;

/// <summary>
/// 단일 인스턴스 확인용 이름(상태 없음). 같은 데이터 폴더를 쓰는 실행은 같은 이름이 되어 하나만 뜨고,
/// 다른 데이터 폴더(UI 테스트, 개발 중 별도 설정)는 다른 이름이라 따로 뜰 수 있다.
/// 폴더 경로는 대소문자와 끝의 구분자를 무시하고 비교한다.
/// </summary>
public static class SingleInstanceName
{
    /// <summary>이름 접미사(경로 해시 16자리, 16진수).</summary>
    public static string For(string dataDir)
    {
        string full = Path.GetFullPath(dataDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(full.ToUpperInvariant()));
        return Convert.ToHexString(hash, 0, 8);
    }
}
