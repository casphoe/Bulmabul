/// <summary>
/// 땅에 건설할 수 있는 건물 종류.
/// 
/// 규칙:
/// - 땅 구매 직후: 작은집 / 집 중 하나만 선택 가능
/// - 시작지점 도착 후: 작은집 / 집 / 큰집 / 호텔 건설 가능
/// - 호텔은 작은집 + 집 + 큰집이 모두 있어야 건설 가능
/// - 호텔이 최종 랜드마크 역할을 한다.
/// - 관광지/특수 지역은 건물 건설 불가
/// </summary>
public enum BulmabulBuildPart
{
    None = 0,
    SmallHouse = 1,
    House = 2,
    BigHouse = 3,
    Hotel = 4
}

/// <summary>
/// 한 땅에 지어진 건물 상태를 int 비트 플래그로 저장한다.
/// NetworkArray에는 enum flags보다 int가 안전하다.
/// 
/// 0 = 없음
/// 1 = 작은집
/// 2 = 집
/// 4 = 큰집
/// 8 = 호텔
/// 1|2|4 = 작은집 + 집 + 큰집
/// 1|2|4|8 = 작은집 + 집 + 큰집 + 호텔
/// </summary>
public static class BulmabulBuildFlags
{
    public const int None = 0;
    public const int SmallHouse = 1 << 0; // 1
    public const int House = 1 << 1;      // 2
    public const int BigHouse = 1 << 2;   // 4
    public const int Hotel = 1 << 3;      // 8

    public static bool Has(int flags, int flag)
    {
        return (flags & flag) != 0;
    }

    public static int Add(int flags, int flag)
    {
        return flags | flag;
    }

    public static bool HasAllNormalBuildings(int flags)
    {
        return Has(flags, SmallHouse) &&
               Has(flags, House) &&
               Has(flags, BigHouse);
    }

    public static string ToText(int flags)
    {
        if (flags == None)
            return "없음";

        System.Collections.Generic.List<string> list = new System.Collections.Generic.List<string>();

        if (Has(flags, SmallHouse)) list.Add("작은집");
        if (Has(flags, House)) list.Add("집");
        if (Has(flags, BigHouse)) list.Add("큰집");
        if (Has(flags, Hotel)) list.Add("호텔");

        return string.Join(", ", list);
    }
}