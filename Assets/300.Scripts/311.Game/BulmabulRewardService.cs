using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 부루마불 승리 보상 결과.
/// GameScene 토스트 메시지에서 경험치 배율, 경험치, 재화, 레벨업 보상을 보여주기 위해 사용한다.
/// </summary>
public class BulmabulRewardResult
{
    public int BeforeLevel;
    public int AfterLevel;

    public int BaseExpReward;
    public float ExpMultiplier;
    public int FinalExpReward;

    public int WinCashReward;
    public int LevelUpCashReward;
    public int LevelUpCount;
    public int TotalCashReward;

    public bool IsLevelUp => LevelUpCount > 0;
}

/// <summary>
/// 부루마불 승리 보상 저장 담당.
/// 승리한 로컬 유저의 Account에 승리 횟수, 재화, 경험치, 레벨업 보상을 반영한다.
/// </summary>
public static class BulmabulRewardService
{
    // 기본 승리 보상
    private const int WinCashReward = 300;

    // 기본 경험치
    private const int BaseWinExpReward = 10;

    // 현재 기본 경험치 배율
    // 나중에 이벤트, PC방, 아이템, 주사위 효과 등을 붙일 때 이 값을 계산식으로 바꾸면 됨
    private const float DefaultExpMultiplier = 1.0f;

    // 레벨업 1회당 추가 재화 보상
    private const int LevelUpCashRewardPerLevel = 1000;

    /// <summary>
    /// 로컬 계정에 승리 보상을 적용하고 결과를 반환한다.
    /// </summary>
    public static async Task<BulmabulRewardResult> ApplyLocalWinRewardAsync()
    {
        BulmabulRewardResult result = new BulmabulRewardResult
        {
            BaseExpReward = BaseWinExpReward,
            ExpMultiplier = GetWinExpMultiplier(),
            WinCashReward = WinCashReward
        };

        var auth = FireBaseAuthManager.Instance;

        if (auth == null || auth.CurrentAccount == null)
        {
            Debug.LogWarning("[BulmabulRewardService] CurrentAccount가 없어 승리 보상을 저장할 수 없습니다.");
            return result;
        }

        Account acc = auth.CurrentAccount;

        result.BeforeLevel = acc.AccountLevel;

        int finalExpReward = Mathf.RoundToInt(result.BaseExpReward * result.ExpMultiplier);
        result.FinalExpReward = Mathf.Max(0, finalExpReward);

        // 승리 횟수 증가
        acc.BulmabulWinCount += 1;

        // 승리 기본 재화 지급
        acc.Cash += result.WinCashReward;

        // 경험치 지급
        acc.AccountExp += result.FinalExpReward;

        // 레벨업 처리 + 레벨업 보상 재화 지급
        int levelUpCount = TryLevelUp(acc);
        result.LevelUpCount = levelUpCount;
        result.AfterLevel = acc.AccountLevel;

        if (levelUpCount > 0)
        {
            result.LevelUpCashReward = levelUpCount * LevelUpCashRewardPerLevel;
            acc.Cash += result.LevelUpCashReward;
        }

        result.TotalCashReward = result.WinCashReward + result.LevelUpCashReward;

        await AccountCloudStore.SaveFullAsync(acc);

        Debug.Log(
            $"[BulmabulRewardService] 승리 보상 저장 완료. " +
            $"EXP +{result.FinalExpReward}, EXP x{result.ExpMultiplier:0.##}, " +
            $"WinCash +{result.WinCashReward}, LevelUpCash +{result.LevelUpCashReward}, " +
            $"Level {result.BeforeLevel}->{result.AfterLevel}"
        );

        return result;
    }

    /// <summary>
    /// 승리 경험치 배율.
    /// 현재는 기본 x1.0.
    /// 나중에 이벤트 배율, 아이템 배율, PC방 배율 등을 여기서 계산하면 된다.
    /// </summary>
    private static float GetWinExpMultiplier()
    {
        return DefaultExpMultiplier;
    }

    /// <summary>
    /// 경험치에 따라 레벨업 처리.
    /// 레벨업한 횟수를 반환한다.
    /// </summary>
    private static int TryLevelUp(Account acc)
    {
        int levelUpCount = 0;

        AccountLevelExpTable table = AccountLevelExpTable.Instance;

        if (table == null || !table.IsLoaded || table.Table == null || table.Table.Count <= 0)
            return levelUpCount;

        while (table.Table.TryGetValue(acc.AccountLevel, out LevelExpRow row))
        {
            if (row == null)
                break;

            if (row.NeedExp <= 0)
                break;

            if (acc.AccountExp < row.NeedExp)
                break;

            acc.AccountExp -= row.NeedExp;
            acc.AccountLevel += 1;
            levelUpCount++;

            if (acc.AccountLevel >= table.MaxLevelInTable)
                break;
        }

        return levelUpCount;
    }
}