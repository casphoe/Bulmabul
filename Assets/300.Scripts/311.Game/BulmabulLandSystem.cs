using UnityEngine;

/// <summary>
/// 땅 구매, 건물 건설 가능 여부, 통행료 계산을 담당하는 순수 계산 클래스.
/// NetworkBehaviour가 아니므로 NetworkObject가 필요 없다.
/// 
/// 호텔은 최종 랜드마크 역할을 한다.
/// </summary>
public static class BulmabulLandSystem
{
    public static int GetBuildFlag(BulmabulBuildPart part)
    {
        switch (part)
        {
            case BulmabulBuildPart.SmallHouse:
                return BulmabulBuildFlags.SmallHouse;

            case BulmabulBuildPart.House:
                return BulmabulBuildFlags.House;

            case BulmabulBuildPart.BigHouse:
                return BulmabulBuildFlags.BigHouse;

            case BulmabulBuildPart.Hotel:
                return BulmabulBuildFlags.Hotel;
        }

        return BulmabulBuildFlags.None;
    }

    public static int GetBuildCost(BulmabulCellData cell, BulmabulBuildPart part)
    {
        if (cell == null)
            return 0;

        switch (part)
        {
            case BulmabulBuildPart.SmallHouse:
                return Mathf.Max(0, cell.smallHouseBuildCost);

            case BulmabulBuildPart.House:
                return Mathf.Max(0, cell.houseBuildCost);

            case BulmabulBuildPart.BigHouse:
                return Mathf.Max(0, cell.bigHouseBuildCost);

            case BulmabulBuildPart.Hotel:
                return Mathf.Max(0, cell.hotelBuildCost);
        }

        return 0;
    }

    public static string GetBuildName(BulmabulBuildPart part)
    {
        switch (part)
        {
            case BulmabulBuildPart.SmallHouse:
                return "작은집";

            case BulmabulBuildPart.House:
                return "집";

            case BulmabulBuildPart.BigHouse:
                return "큰집";

            case BulmabulBuildPart.Hotel:
                return "호텔";
        }

        return "-";
    }

    /// <summary>
    /// 현재 건물 상태에 따른 최종 통행료 계산.
    /// 기본 통행료 + 작은집 추가 통행료 + 집 추가 통행료 + 큰집 추가 통행료 + 호텔 추가 통행료.
    /// 
    /// 호텔은 최종 랜드마크 역할이므로 별도 Landmark 통행료는 없다.
    /// </summary>
    public static int CalculateToll(BulmabulCellData cell, int buildFlags)
    {
        if (cell == null)
            return 0;

        int toll = Mathf.Max(0, cell.tollCost);

        if (BulmabulBuildFlags.Has(buildFlags, BulmabulBuildFlags.SmallHouse))
            toll += Mathf.Max(0, cell.smallHouseToll);

        if (BulmabulBuildFlags.Has(buildFlags, BulmabulBuildFlags.House))
            toll += Mathf.Max(0, cell.houseToll);

        if (BulmabulBuildFlags.Has(buildFlags, BulmabulBuildFlags.BigHouse))
            toll += Mathf.Max(0, cell.bigHouseToll);

        if (BulmabulBuildFlags.Has(buildFlags, BulmabulBuildFlags.Hotel))
            toll += Mathf.Max(0, cell.hotelToll);

        return toll;
    }

    /// <summary>
    /// 특정 건물을 지을 수 있는지 검사.
    /// 
    /// initialBuildAfterBuy = true:
    /// - 땅 구매 직후
    /// - 작은집 / 집만 가능
    /// 
    /// initialBuildAfterBuy = false:
    /// - 시작지점 도착 후 건설
    /// - 작은집 / 집 / 큰집 / 호텔 가능
    /// 
    /// 호텔 조건:
    /// - 작은집 + 집 + 큰집이 모두 있어야 가능
    /// - 이미 호텔이 있으면 불가
    /// 
    /// 관광지/특수 지역:
    /// - cell.isLandmark == true면 건설 불가
    /// </summary>
    public static bool CanBuild(
        BulmabulCellData cell,
        int currentFlags,
        int playerCash,
        BulmabulBuildPart part,
        bool initialBuildAfterBuy)
    {
        if (cell == null)
            return false;

        if (cell.cellType != BulmabulCellType.Land)
            return false;

        if (cell.isLandmark)
            return false;

        int needCost = GetBuildCost(cell, part);

        if (needCost <= 0)
            return false;

        if (playerCash < needCost)
            return false;

        switch (part)
        {
            case BulmabulBuildPart.SmallHouse:
                return !BulmabulBuildFlags.Has(currentFlags, BulmabulBuildFlags.SmallHouse);

            case BulmabulBuildPart.House:
                return !BulmabulBuildFlags.Has(currentFlags, BulmabulBuildFlags.House);

            case BulmabulBuildPart.BigHouse:
                if (initialBuildAfterBuy)
                    return false;

                return !BulmabulBuildFlags.Has(currentFlags, BulmabulBuildFlags.BigHouse);

            case BulmabulBuildPart.Hotel:
                if (initialBuildAfterBuy)
                    return false;

                if (BulmabulBuildFlags.Has(currentFlags, BulmabulBuildFlags.Hotel))
                    return false;

                return BulmabulBuildFlags.HasAllNormalBuildings(currentFlags);
        }

        return false;
    }

    /// <summary>
    /// 재화 조건을 제외하고 건설 규칙만 검사한다.
    /// 재화가 부족한지 토스트로 알려주기 위해 사용한다.
    /// </summary>
    public static bool CanBuildIgnoringCash(
        BulmabulCellData cell,
        int currentFlags,
        BulmabulBuildPart part,
        bool initialBuildAfterBuy)
    {
        return CanBuild(
            cell,
            currentFlags,
            int.MaxValue,
            part,
            initialBuildAfterBuy
        );
    }

    public static bool CanBuildAny(
        BulmabulCellData cell,
        int currentFlags,
        int playerCash,
        bool initialBuildAfterBuy)
    {
        return CanBuild(cell, currentFlags, playerCash, BulmabulBuildPart.SmallHouse, initialBuildAfterBuy) ||
               CanBuild(cell, currentFlags, playerCash, BulmabulBuildPart.House, initialBuildAfterBuy) ||
               CanBuild(cell, currentFlags, playerCash, BulmabulBuildPart.BigHouse, initialBuildAfterBuy) ||
               CanBuild(cell, currentFlags, playerCash, BulmabulBuildPart.Hotel, initialBuildAfterBuy);
    }

    /// <summary>
    /// 인수 비용 계산.
    /// 땅 가격 + 현재 지어진 건물 가격 합산.
    /// 호텔이 있으면 인수 불가이므로 이 함수는 호텔 없는 상태에서만 쓰는 것을 권장한다.
    /// </summary>
    public static int CalculateTakeOverCost(BulmabulCellData cell, int buildFlags)
    {
        if (cell == null)
            return 0;

        int cost = Mathf.Max(0, cell.buyCost);

        if (BulmabulBuildFlags.Has(buildFlags, BulmabulBuildFlags.SmallHouse))
            cost += Mathf.Max(0, cell.smallHouseBuildCost);

        if (BulmabulBuildFlags.Has(buildFlags, BulmabulBuildFlags.House))
            cost += Mathf.Max(0, cell.houseBuildCost);

        if (BulmabulBuildFlags.Has(buildFlags, BulmabulBuildFlags.BigHouse))
            cost += Mathf.Max(0, cell.bigHouseBuildCost);

        if (BulmabulBuildFlags.Has(buildFlags, BulmabulBuildFlags.Hotel))
            cost += Mathf.Max(0, cell.hotelBuildCost);

        return cost;
    }

    /// <summary>
    /// 호텔이 없는 땅만 인수 가능.
    /// </summary>
    public static bool CanTakeOver(int buildFlags)
    {
        return !BulmabulBuildFlags.Has(buildFlags, BulmabulBuildFlags.Hotel);
    }
}