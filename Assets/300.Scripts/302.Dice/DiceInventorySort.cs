public static class DiceInventorySort
{
    // Grade 우선순위 (높을수록 먼저)
    private static int GradeRank(DiceGrade g) => g switch
    {
        DiceGrade.Legendary => 4,
        DiceGrade.Epic => 3,
        DiceGrade.Rare => 2,
        DiceGrade.Common => 1,
        _ => 0
    };

    public static void SortDiceInventory(Account acc)
    {
        if (acc?.DiceInventory == null) return;

        acc.DiceInventory.Sort((a, b) =>
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;

            int g = GradeRank(b.Grade).CompareTo(GradeRank(a.Grade)); // desc
            if (g != 0) return g;

            int s = b.Star.CompareTo(a.Star); // desc
            if (s != 0) return s;

            int l = b.Level.CompareTo(a.Level); // desc
            if (l != 0) return l;

            // 동일하면 보유 개수 많은 순(선택)
            return b.Count.CompareTo(a.Count);
        });
    }
}
