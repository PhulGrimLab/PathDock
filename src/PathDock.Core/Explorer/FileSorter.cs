namespace PathDock.Core.Explorer;

/// <summary>목록 정렬 기준.</summary>
public enum SortKey
{
    Name,
    Modified,
    Size,
}

/// <summary>
/// 파일 목록 정렬(상태 없음). 규칙: 폴더가 항상 먼저, 같은 묶음 안에서 기준/방향 적용, 동률은 이름 오름차순.
/// 이름 비교는 대소문자 무시 + 숫자 덩어리는 수 크기로 비교하는 자연 정렬(file2 &lt; file10).
/// 크기를 아직 모르는 항목(계산 전 폴더)은 -1로 취급한다. 제네릭이라 모델/ViewModel 모두에 쓴다.
/// </summary>
public static class FileSorter
{
    public static List<T> Sort<T>(IEnumerable<T> items, SortKey key, bool ascending,
        Func<T, bool> isDirectory, Func<T, string> name, Func<T, DateTime> modified, Func<T, long?> size)
    {
        IEnumerable<T> list = items;
        IOrderedEnumerable<T> ordered = list.OrderByDescending(isDirectory); // 폴더 먼저

        ordered = key switch
        {
            SortKey.Modified => Then(ordered, i => modified(i), ascending),
            SortKey.Size => Then(ordered, i => size(i) ?? -1L, ascending),
            _ => ThenName(ordered, name, ascending),
        };
        // 동률 정리: 이름 오름차순(이름 정렬이 기준일 때는 이미 반영됨).
        return key == SortKey.Name
            ? ordered.ToList()
            : ordered.ThenBy(name, NaturalComparer.Instance).ToList();
    }

    private static IOrderedEnumerable<T> Then<T, TKey>(IOrderedEnumerable<T> src, Func<T, TKey> sel, bool asc)
        => asc ? src.ThenBy(sel) : src.ThenByDescending(sel);

    private static IOrderedEnumerable<T> ThenName<T>(IOrderedEnumerable<T> src, Func<T, string> name, bool asc)
        => asc ? src.ThenBy(name, NaturalComparer.Instance) : src.ThenByDescending(name, NaturalComparer.Instance);

    /// <summary>자연 정렬 비교기(대소문자 무시, 숫자 덩어리는 수 비교).</summary>
    public sealed class NaturalComparer : IComparer<string>
    {
        public static readonly NaturalComparer Instance = new();

        public int Compare(string? a, string? b)
        {
            a ??= string.Empty;
            b ??= string.Empty;
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int si = i, sj = j;
                    while (i < a.Length && char.IsDigit(a[i])) { i++; }
                    while (j < b.Length && char.IsDigit(b[j])) { j++; }
                    // 앞의 0을 무시하고 자릿수 → 사전순으로 비교(오버플로 없이 임의 길이 숫자 처리).
                    string da = a[si..i].TrimStart('0'), db = b[sj..j].TrimStart('0');
                    int c = da.Length != db.Length ? da.Length.CompareTo(db.Length) : string.CompareOrdinal(da, db);
                    if (c != 0)
                    {
                        return c;
                    }
                }
                else
                {
                    int c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                    if (c != 0)
                    {
                        return c;
                    }
                    i++;
                    j++;
                }
            }
            return (a.Length - i).CompareTo(b.Length - j);
        }
    }
}
