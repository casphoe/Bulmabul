using UnityEngine;

//뽑기에서 중복된 주사위 나왔을 때 처리
public static class DiceProgression
{
    // 중복 지급률: 다음 레벨 필요 경험치의 20%
    public const float DUPLICATE_EXP_RATE = 0.20f;

    // 10단위로 끊고 싶으면 true
    public const bool ROUND_DOWN_TO_10 = true;

    // 레벨10 이후 중복 시 샤드 지급량
    public const int SHARD_PER_DUPLICATE = 1;

    // 별 승급 비용(샤드)
    public const int STAR_UP_COST = 5;

    // 최대 별
    public const int MAX_STAR = 5;

    //최대 레벨
    public const int MAX_LEVEL = 10;

    public const int PROMOTE_EXP_PER_DUPLICATE = 1;
    public const int GRADE_UP_COST = 5; // PromoteExp 5개 모으면 등급승급 가능


    public static int CalcDuplicateExp(OwnedDice target)
    {
        int need = DiceExpTable.GetExpToNext(target.Grade, target.Star, target.Level);
        if (need <= 0) return 0;

        int gain = Mathf.FloorToInt(need * DUPLICATE_EXP_RATE);
        if (ROUND_DOWN_TO_10)
            gain = Mathf.Max(10, (gain / 10) * 10); // 최소 10 보장(원치 않으면 Max 제거)

        return gain;
    }

    /// <summary>
    /// 중복 처리(단일 책임)
    /// - wasMaxLevelBefore == false : Exp로 레벨업만 (이번에 10이 되어도 shard/promoteExp 지급 X)
    /// - wasMaxLevelBefore == true  : 만렙 이후 처리
    ///     - Star < 5 : Shard +1, Shard 5면 Star 자동승급(인벤 병합/장착유지는 DiceTables가 처리)
    ///     - Star == 5: PromoteExp +1 (등급승급 재화)
    /// </summary>
    public static void OnDuplicate(Account acc, OwnedDice d, bool wasMaxLevelBefore)
    {
        if (d == null) return;

        // 1) 만렙이 아니었던 상태에서 들어온 중복이면 -> Exp만 처리
        if (!wasMaxLevelBefore)
        {
            int addExp = CalcDuplicateExp(d);

            AddExpAndTryLevelUp(d, addExp);

            // 규칙: 이번에 10레벨이 되었더라도 shard/promoteExp는 이번 중복에서 지급하지 않음
            return;
        }

        // 2) 만렙 이후
        if (d.Star < MAX_STAR)
        {
            d.Shard += SHARD_PER_DUPLICATE;

            // shard 5개 이상이면 자동 별 승급
            if (acc != null && d.Shard >= STAR_UP_COST)
            {
                DiceTables.PromoteStarFromShards(acc, d);
            }
        }
        else
        {
            // ★5 만렙이면 등급승급 재화만 누적
            d.PromoteExp += PROMOTE_EXP_PER_DUPLICATE;
        }
    }


    /// <summary>
    /// Exp 추가 후 레벨업 수행.
    /// return: 이번 호출로 Level 10에 도달했으면 true
    /// </summary>
    private static void AddExpAndTryLevelUp(OwnedDice d, int addExp)
    {
        if (d == null) return;
        if (d.Level >= MAX_LEVEL) return;

        d.Exp += addExp;

        while (d.Level < MAX_LEVEL)
        {
            int need = DiceExpTable.GetExpToNext(d.Grade, d.Star, d.Level);
            if (need <= 0) break;
            if (d.Exp < need) break;

            d.Exp -= need;
            d.Level += 1;
        }

        if (d.Level >= MAX_LEVEL)
            d.Exp = 0;
    }

    public static bool CanGradePromote(OwnedDice d)
    {
        if (d == null) return false;

        return d.Level >= MAX_LEVEL
            && d.Star >= MAX_STAR
            && d.PromoteExp >= GRADE_UP_COST
            && d.Grade != DiceGrade.Legendary;
    }
}
