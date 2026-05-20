using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 부루마불 중도 이탈 기록 / 일일 패널티 담당.
/// 
/// 규칙:
/// - 이탈 시 패배 수 +1
/// - 전체 이탈 수 +1
/// - 오늘 이탈 수 +1
/// - 오늘 이탈 1~4회: 재화 패널티 없음
/// - 오늘 이탈 5회 이상: 그때부터 재화 차감 + 패널티 시간 적용
/// </summary>
public static class BulmabulPenaltyService
{
    private const int DailyPenaltyStartCount = 5;
    private const int LeaveCashPenalty = 500;

    /// <summary>
    /// 로컬 계정에 게임 중도 이탈 기록을 적용한다.
    /// Alt+F4에서는 이 함수가 항상 실행된다고 보장할 수 없다.
    /// 완전한 처리는 서버 / Cloud Function이 필요하다.
    /// </summary>
    public static async Task ApplyLocalLeaveRecordAsync()
    {
        var auth = FireBaseAuthManager.Instance;

        if (auth == null || auth.CurrentAccount == null)
        {
            Debug.LogWarning("[BulmabulPenaltyService] CurrentAccount가 없어 이탈 기록을 저장할 수 없습니다.");
            return;
        }

        Account acc = auth.CurrentAccount;

        string todayKey = DateTime.Now.ToString("yyyy-MM-dd");

        // 날짜가 바뀌었으면 오늘 이탈 횟수 초기화
        if (acc.BulmabulLeaveDateKey != todayKey)
        {
            acc.BulmabulLeaveDateKey = todayKey;
            acc.BulmabulTodayLeaveCount = 0;
        }

        acc.BulmabulLoseCount += 1;
        acc.BulmabulLeaveCount += 1;
        acc.BulmabulTodayLeaveCount += 1;
        acc.BulmabulLastLeaveAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 오늘 5회째부터 패널티 적용
        if (acc.BulmabulTodayLeaveCount >= DailyPenaltyStartCount)
        {
            acc.Cash = Mathf.Max(0, acc.Cash - LeaveCashPenalty);

            int penaltyMinutes = GetPenaltyMinutes(acc.BulmabulTodayLeaveCount);
            acc.BulmabulPenaltyUntil = DateTime.Now.AddMinutes(penaltyMinutes).ToString("yyyy-MM-dd HH:mm:ss");

            Debug.Log(
                $"[BulmabulPenaltyService] 일일 이탈 {acc.BulmabulTodayLeaveCount}회. " +
                $"패널티 적용: Cash -{LeaveCashPenalty}, 제한 {penaltyMinutes}분"
            );
        }
        else
        {
            Debug.Log(
                $"[BulmabulPenaltyService] 일일 이탈 {acc.BulmabulTodayLeaveCount}회. " +
                $"아직 재화 패널티 없음."
            );
        }

        await AccountCloudStore.SaveFullAsync(acc);
    }

    /// <summary>
    /// 오늘 이탈 횟수에 따른 제한 시간.
    /// </summary>
    private static int GetPenaltyMinutes(int todayLeaveCount)
    {
        if (todayLeaveCount >= 10)
            return 30;

        if (todayLeaveCount >= 7)
            return 15;

        return 5;
    }
}