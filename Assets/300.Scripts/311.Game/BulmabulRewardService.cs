using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 부루마불 게임 결과 보상 결과.
/// 승리/패배 결과 패널에서 경험치, 재화, 레벨업 보상을 보여주기 위해 사용한다.
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

    public bool IsWin;
    public bool IsDefeat;

    public bool IsLevelUp => LevelUpCount > 0;
}

/// <summary>
/// 부루마불 게임 결과 보상 저장 담당.
/// 
/// 규칙:
/// - 승리: 승리 횟수 +1, 승리 재화, 승리 경험치 지급.
/// - 정상 패배: 패배 횟수 +1, 소량 경험치 지급.
/// - 이탈 패배: BulmabulPenaltyService에서 별도로 처리하므로 여기서는 지급하지 않는다.
/// </summary>
public static class BulmabulRewardService
{
    // 기본 승리 보상
    private const int WinCashReward = 200;
    // 기본 패배 보상
    // 게임을 나가지 않고 파산 등으로 정상 패배한 플레이어에게 지급한다.
    private const int DefeatCashReward = 60;

    // 기본 경험치
    private const int BaseWinExpReward = 10;
    private const int BaseDefeatExpReward = 3;

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
            IsWin = true,
            BaseExpReward = BaseWinExpReward,
            ExpMultiplier = GetGameExpMultiplier(),
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
        result.FinalExpReward = Mathf.Max(0, Mathf.RoundToInt(result.BaseExpReward * result.ExpMultiplier));

        // 승리 횟수 증가
        acc.BulmabulWinCount += 1;

        // 승리 기본 재화 지급
        acc.Cash += result.WinCashReward;

        // 경험치 지급
        acc.AccountExp += result.FinalExpReward;

        // 레벨업 처리 + 레벨업 보상 재화 지급
        ApplyLevelUpReward(acc, result);

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
    /// 로컬 계정에 정상 패배 보상을 적용하고 결과를 반환한다.
    /// 
    /// 주의:
    /// - 게임을 직접 나간 이탈 패배에는 사용하지 않는다.
    /// - 파산으로 끝까지 플레이한 패배자에게만 호출한다.
    /// </summary>
    public static async Task<BulmabulRewardResult> ApplyLocalDefeatRewardAsync()
    {
        BulmabulRewardResult result = new BulmabulRewardResult
        {
            IsDefeat = true,
            BaseExpReward = BaseDefeatExpReward,
            ExpMultiplier = GetGameExpMultiplier(),
            WinCashReward = DefeatCashReward
        };

        var auth = FireBaseAuthManager.Instance;

        if (auth == null || auth.CurrentAccount == null)
        {
            Debug.LogWarning("[BulmabulRewardService] CurrentAccount가 없어 패배 보상을 저장할 수 없습니다.");
            return result;
        }

        Account acc = auth.CurrentAccount;

        result.BeforeLevel = acc.AccountLevel;
        result.FinalExpReward = Mathf.Max(0, Mathf.RoundToInt(result.BaseExpReward * result.ExpMultiplier));

        // 정상 패배 횟수 증가
        acc.BulmabulLoseCount += 1;

        // 정상 패배 경험치 지급
        acc.AccountExp += result.FinalExpReward;

        // 정상 패배 재화 지급
        acc.Cash += DefeatCashReward;

        // 패배 경험치로 레벨업이 가능하면 레벨업 보상은 동일하게 지급
        ApplyLevelUpReward(acc, result);

        // 패배 기본 재화 + 레벨업 보상 재화
        result.TotalCashReward = DefeatCashReward + result.LevelUpCashReward;

        await AccountCloudStore.SaveFullAsync(acc);

        Debug.Log(
            $"[BulmabulRewardService] 정상 패배 보상 저장 완료. " +
            $"EXP +{result.FinalExpReward}, EXP x{result.ExpMultiplier:0.##}, " +
            $"LevelUpCash +{result.LevelUpCashReward}, " +
            $"Level {result.BeforeLevel}->{result.AfterLevel}"
        );

        return result;
    }

    /// <summary>
    /// 게임 경험치 배율.
    /// 현재는 기본 x1.0.
    /// 나중에 이벤트 배율, 아이템 배율, PC방 배율 등을 여기서 계산하면 된다.
    /// </summary>
    private static float GetGameExpMultiplier()
    {
        return DefaultExpMultiplier;
    }

    private static void ApplyLevelUpReward(Account acc, BulmabulRewardResult result)
    {
        int levelUpCount = TryLevelUp(acc);
        result.LevelUpCount = levelUpCount;
        result.AfterLevel = acc.AccountLevel;

        if (levelUpCount <= 0)
            return;

        result.LevelUpCashReward = levelUpCount * LevelUpCashRewardPerLevel;
        acc.Cash += result.LevelUpCashReward;
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