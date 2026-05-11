using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 주사위 결과 데이터.
/// </summary>
public struct BulmabulDiceRollResult
{
    public int left;
    public int right;
    public int sum;
    public bool isDouble;

    /// <summary>주사위 컨트롤이 성공했는지</summary>
    public bool controlSuccess;

    /// <summary>선택한 홀/짝</summary>
    public BulmabulDiceParityChoice parityChoice;

    /// <summary>게이지 값 0~1</summary>
    public float gauge01;
}

/// <summary>
/// 장착 주사위를 기준으로 주사위 2개를 굴리는 계산 담당.
/// 화면 연출은 하지 않는다.
/// </summary>
public static class BulmabulDiceRoller
{
    /// <summary>
    /// 기존 방식.
    /// 주사위 능력치의 면 가중치만 사용해서 주사위 2개를 굴린다.
    /// </summary>
    public static BulmabulDiceRollResult RollTwoDice(int diceGrade, int diceStar, int diceLevel)
    {
        Dice runtimeDice = CreateRuntimeDice(diceGrade, diceStar, diceLevel);

        int left = runtimeDice.Roll();
        int right = runtimeDice.Roll();

        return new BulmabulDiceRollResult
        {
            left = left,
            right = right,
            sum = left + right,
            isDouble = left == right,
            controlSuccess = false,
            parityChoice = BulmabulDiceParityChoice.None,
            gauge01 = 0f
        };
    }

    /// <summary>
    /// 홀/짝 + 게이지 + 장착 주사위 능력치 기반 주사위 컨트롤.
    /// 
    /// 성공:
    /// - 선택한 홀/짝 합계가 나오도록 보정
    /// - 게이지가 높을수록 큰 이동 칸 수를 노림
    /// 
    /// 실패:
    /// - 기존 주사위 가중치대로 일반 굴림
    /// </summary>
    public static BulmabulDiceRollResult RollTwoDiceControlled(
        int diceGrade,
        int diceStar,
        int diceLevel,
        int parityChoiceInt,
        int gaugePermille)
    {
        BulmabulDiceParityChoice parityChoice = (BulmabulDiceParityChoice)parityChoiceInt;
        float gauge01 = Mathf.Clamp01(gaugePermille / 1000f);

        if (parityChoice == BulmabulDiceParityChoice.None)
            return RollTwoDice(diceGrade, diceStar, diceLevel);

        Dice runtimeDice = CreateRuntimeDice(diceGrade, diceStar, diceLevel);

        float controlChance = GetDiceControlChance(diceGrade, diceStar, diceLevel, gauge01);
        bool success = UnityEngine.Random.value <= controlChance;

        if (!success)
        {
            BulmabulDiceRollResult failed = RollTwoDice(diceGrade, diceStar, diceLevel);
            failed.controlSuccess = false;
            failed.parityChoice = parityChoice;
            failed.gauge01 = gauge01;
            return failed;
        }

        int targetSum = GetTargetSumByParityAndGauge(parityChoice, gauge01);
        Vector2Int pair = PickDicePairForSum(targetSum, runtimeDice.faceWeights);

        return new BulmabulDiceRollResult
        {
            left = pair.x,
            right = pair.y,
            sum = pair.x + pair.y,
            isDouble = pair.x == pair.y,
            controlSuccess = true,
            parityChoice = parityChoice,
            gauge01 = gauge01
        };
    }

    /// <summary>
    /// 장착 주사위 능력치로 컨트롤 성공률 계산.
    /// 현재 별도 능력치가 없으므로 등급/별/레벨을 사용한다.
    /// </summary>
    private static float GetDiceControlChance(int diceGrade, int diceStar, int diceLevel, float gauge01)
    {
        DiceGrade grade = (DiceGrade)Mathf.Clamp(
            diceGrade,
            0,
            Enum.GetValues(typeof(DiceGrade)).Length - 1
        );

        float baseChance = grade switch
        {
            DiceGrade.Common => 0.20f,
            DiceGrade.Rare => 0.30f,
            DiceGrade.Epic => 0.40f,
            DiceGrade.Legendary => 0.50f,
            _ => 0.20f
        };

        int star = Mathf.Clamp(diceStar, 1, 5);
        int level = Mathf.Clamp(diceLevel, 1, 10);

        float statBonus = (star - 1) * 0.04f + (level - 1) * 0.02f;

        // 게이지 중앙보다 높은 구간에서 떼는 것을 약간 유리하게 처리
        // 너무 낮은 위치에서 떼면 성공률이 조금 낮아진다.
        float gaugeBonus = Mathf.Lerp(-0.05f, 0.05f, gauge01);

        return Mathf.Clamp(baseChance + statBonus + gaugeBonus, 0.05f, 0.85f);
    }

    /// <summary>
    /// 선택한 홀/짝과 게이지 위치로 목표 합계를 정한다.
    /// 게이지가 낮으면 낮은 합계, 높으면 높은 합계를 노린다.
    /// </summary>
    private static int GetTargetSumByParityAndGauge(BulmabulDiceParityChoice parityChoice, float gauge01)
    {
        int[] sums;

        if (parityChoice == BulmabulDiceParityChoice.Odd)
        {
            // 2개 주사위 합계 중 홀수
            sums = new[] { 3, 5, 7, 9, 11 };
        }
        else
        {
            // 2개 주사위 합계 중 짝수
            sums = new[] { 2, 4, 6, 8, 10, 12 };
        }

        int index = Mathf.RoundToInt(gauge01 * (sums.Length - 1));
        index = Mathf.Clamp(index, 0, sums.Length - 1);

        return sums[index];
    }

    /// <summary>
    /// 목표 합계를 만들 수 있는 주사위 조합 중 하나를 고른다.
    /// 장착 주사위의 faceWeights를 이용해서 강한 면이 조금 더 잘 나오도록 한다.
    /// </summary>
    private static Vector2Int PickDicePairForSum(int targetSum, int[] faceWeights)
    {
        List<Vector2Int> pairs = new List<Vector2Int>();
        List<int> weights = new List<int>();

        for (int left = 1; left <= 6; left++)
        {
            for (int right = 1; right <= 6; right++)
            {
                if (left + right != targetSum)
                    continue;

                pairs.Add(new Vector2Int(left, right));

                int leftWeight = GetFaceWeight(faceWeights, left);
                int rightWeight = GetFaceWeight(faceWeights, right);

                weights.Add(Mathf.Max(1, leftWeight + rightWeight));
            }
        }

        if (pairs.Count <= 0)
            return new Vector2Int(1, 1);

        int total = 0;
        for (int i = 0; i < weights.Count; i++)
            total += Mathf.Max(1, weights[i]);

        int r = UnityEngine.Random.Range(0, total);
        int acc = 0;

        for (int i = 0; i < pairs.Count; i++)
        {
            acc += Mathf.Max(1, weights[i]);

            if (r < acc)
                return pairs[i];
        }

        return pairs[pairs.Count - 1];
    }

    private static int GetFaceWeight(int[] faceWeights, int face)
    {
        if (faceWeights == null || faceWeights.Length < 6)
            return 1;

        int index = Mathf.Clamp(face - 1, 0, 5);
        return Mathf.Max(1, faceWeights[index]);
    }

    private static Dice CreateRuntimeDice(int diceGrade, int diceStar, int diceLevel)
    {
        TryLoadDiceTables();

        OwnedDice owned = new OwnedDice
        {
            Grade = (DiceGrade)diceGrade,
            Star = Mathf.Clamp(diceStar, 1, 5),
            Level = Mathf.Clamp(diceLevel, 1, 10)
        };

        try
        {
            return DiceTables.CreateDiceFromOwned(owned);
        }
        catch
        {
            return new Dice(DiceGrade.Common, 1, 1, new[] { 1, 1, 1, 1, 1, 1 });
        }
    }

    private static void TryLoadDiceTables()
    {
        try
        {
            if (!DiceTables.IsLoaded)
                DiceTables.LoadOrThrow();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[BulmabulDiceRoller] DiceTables load failed: {e.Message}");
        }
    }
}