using UnityEngine;

public static class DiceGradePromotion
{
    private static DiceGrade NextGrade(DiceGrade g) => g switch
    {
        DiceGrade.Common => DiceGrade.Rare,
        DiceGrade.Rare => DiceGrade.Epic,
        DiceGrade.Epic => DiceGrade.Legendary,
        DiceGrade.Legendary => DiceGrade.Legendary,
        _ => g
    };

    public static bool TryPromoteGrade(Account acc, OwnedDice from)
    {
        if (acc == null || from == null) return false;

        // 조건: ★5, Lv10, PromoteExp 5 이상
        if (from.Star < DiceProgression.MAX_STAR) return false;
        if (from.Level < DiceProgression.MAX_LEVEL) return false;
        if (from.PromoteExp < DiceProgression.GRADE_UP_COST) return false;
        if (from.Grade == DiceGrade.Legendary) return false; // 최종 등급

        // 승급 전 장착 여부 체크 (참조 + 저장키)
        string beforeEquipKey = $"{from.Grade}|{from.Star}";
        bool wasEquipped =
            (acc.EquippedDice == from) ||
            (!string.IsNullOrWhiteSpace(acc.EquippedDiceKey) && acc.EquippedDiceKey == beforeEquipKey);


        // 비용 차감
        from.PromoteExp -= DiceProgression.GRADE_UP_COST;

        DiceGrade next = NextGrade(from.Grade);

        // 승급 결과는 Grade만 상승, Star=1 Level=1 리셋(네 요구사항)
        int newStar = 1;

        // 대상 주사위(다음 Grade + Star 1) 찾기
        var target = acc.DiceInventory.Find(x => x.Grade == next && x.Star == newStar);

        if (target == null)
        {
            target = new OwnedDice
            {
                Grade = next,
                Star = newStar,
                Level = 1,
                Exp = 0,
                Shard = 0,
                PromoteExp = 0,
                Count = 0
            };
            acc.DiceInventory.Add(target);
        }

        // 보유 수 합치기(기획에 따라 달라질 수 있음)
        target.Count += Mathf.Max(1, from.Count);
        target.Level = 1;
        target.Exp = 0;
        target.Shard = 0;
        target.PromoteExp = 0;

        // 기존 항목 제거(Grade가 바뀌면 다른 주사위니까)
        acc.DiceInventory.Remove(from);

        // 장착 유지 (승급 결과를 장착)
        if (wasEquipped)
        {
            acc.EquippedDice = target;
            acc.EquippedDiceKey = $"{target.Grade}|{target.Star}";
        }
        else
        {
            // 혹시 참조가 깨졌으면 키 기반 복구
            if (acc.EquippedDice != null && !acc.DiceInventory.Contains(acc.EquippedDice))
            {
                acc.EquippedDice = acc.DiceInventory.Find(x =>
                    x != null && $"{x.Grade}|{x.Star}" == acc.EquippedDiceKey);
            }
        }

        DiceInventorySort.SortDiceInventory(acc);

        return true;
    }
}