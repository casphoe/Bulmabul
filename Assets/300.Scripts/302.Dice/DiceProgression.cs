using UnityEngine;

//뽑기에서 중복된 주사위 나왔을 때 처리
public static class DiceProgression
{
    // 중복 지급률: 다음 레벨 필요 경험치의 20%
    public const float DUPLICATE_EXP_RATE = 0.20f;

    // 10단위로 끊고 싶으면 true
    public const bool ROUND_DOWN_TO_10 = true;

    public static int CalcDuplicateExp(OwnedDice target)
    {
        int need = DiceExpTable.GetExpToNext(target.Grade, target.Star, target.Level);
        if (need <= 0) return 0;

        int gain = Mathf.FloorToInt(need * DUPLICATE_EXP_RATE);
        if (ROUND_DOWN_TO_10)
            gain = Mathf.Max(10, (gain / 10) * 10); // 최소 10 보장(원치 않으면 Max 제거)

        return gain;
    }


    public static void AddExpAndTryLevelUp(OwnedDice d, int addExp)
    {
        if (d.Level >= 10) return;

        d.Exp += addExp;

        while (d.Level < 10)
        {
            int need = DiceExpTable.GetExpToNext(d.Grade, d.Star, d.Level);
            if (need <= 0) break;

            if (d.Exp < need) break;

            d.Exp -= need;
            d.Level += 1;
        }

        if (d.Level >= 10)
            d.Exp = 0; // 만렙이면 exp는 0으로 정리(취향)
    }
}
