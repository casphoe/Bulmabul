using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 월간 출석 체크 서비스
///
/// 규칙:
/// - 하루 1회만 출석 가능
/// - 매일 출석 기본 보상: Cash +100
/// - 7일 연속 출석: Cash +1000
/// - 14일 연속 출석: Rare 주사위 (Lv1, Star1)
/// - 21일 연속 출석: Cash +3000
/// - 해당 월의 모든 날짜를 연속 출석: Epic 주사위 (Lv1, Star1)  <-- SR 대체
///
/// 기준 시간:
/// - DateTime.Now.Date (현재 컴퓨터 날짜)
///
/// 주의:
/// - 연속 출석은 "현재 달 안에서" 계산
/// - 달이 바뀌면 출석 목록/카운트 초기화
/// - 예: 2월은 28일이면 28일 모두 출석 시 한달 연속으로 인정
/// </summary>
public static class AttendanceService
{
    // 기본 일일 보상
    public const float DAILY_CASH_REWARD = 100f;

    // 연속 출석 보상
    public const float STREAK_7_CASH_REWARD = 1000f;
    public const float STREAK_21_CASH_REWARD = 3000f;

    [Serializable]
    public class AttendanceClaimResult
    {
        /// <summary>
        /// 이번 호출에서 실제로 출석 처리가 성공했는지 여부
        /// true면 오늘 출석 등록 + 보상 지급까지 완료된 상태
        /// false면 이미 출석했거나 처리 실패로 간주할 수 있음
        /// </summary>
        public bool Claimed;

        /// <summary>
        /// 오늘 이미 출석을 끝낸 상태인지 여부
        /// true면 중복 출석 방지를 위해 추가 보상 지급 없이 종료됨
        /// </summary>
        public bool AlreadyClaimedToday;

        /// <summary>
        /// 출석 처리 기준이 된 오늘 날짜 문자열
        /// 형식 예시: "2026-03-21"
        /// </summary>
        public string TodayString;

        /// <summary>
        /// 오늘 날짜의 일(day) 값
        /// 예: 21일이면 21
        /// </summary>
        public int TodayDay;

        /// <summary>
        /// 현재 달의 총 일수
        /// 예: 2월이면 28 또는 29, 3월이면 31
        /// </summary>
        public int DaysInMonth;

        /// <summary>
        /// 이번 달 누적 출석 횟수
        /// 중복 제거된 claimedAttendanceDays 개수와 동일한 의미
        /// </summary>
        public int AttendanceCountThisMonth;

        /// <summary>
        /// 오늘을 기준으로 역산했을 때의 연속 출석 일수
        /// 예: 1,2,3,4,5일 연속 출석했고 오늘이 5일이면 5
        /// 중간에 하루라도 비면 그 전까지만 계산됨
        /// </summary>
        public int CurrentStreak;

        /// <summary>
        /// 이번 출석 처리로 지급된 보상 메시지 목록
        /// 예:
        /// - "일일 출석 보상: Cash +100"
        /// - "7일 연속 출석 보상: Cash +1000"
        /// - "14일 연속 출석 보상: Rare 주사위 1개 지급 (Lv1, Star1)"
        /// UI 팝업이나 토스트에 그대로 활용 가능
        /// </summary>
        public List<string> RewardMessages = new List<string>();

        /// <summary>
        /// 디버그 로그 출력용 요약 문자열
        /// 출석 성공 여부, 오늘 날짜, 이번 달 출석 수, 연속 출석 수를 한 줄로 보여줌
        /// </summary>
        public string DebugSummary =>
            $"Claimed={Claimed}, Already={AlreadyClaimedToday}, Today={TodayString}, " +
            $"Count={AttendanceCountThisMonth}, Streak={CurrentStreak}/{DaysInMonth}";
    }

    [Serializable]
    public class AttendanceStatus
    {
        /// <summary>
        /// 상태 조회 기준이 된 오늘 날짜 문자열
        /// 형식 예시: "2026-03-21"
        /// </summary>
        public string TodayString;

        /// <summary>
        /// 현재 출석이 속한 월 키
        /// 형식 예시: "2026-03"
        /// 달이 바뀌면 출석 데이터 초기화 기준으로 사용됨
        /// </summary>
        public string MonthKey;

        /// <summary>
        /// 오늘 날짜의 day 값
        /// 예: 오늘이 21일이면 21
        /// </summary>
        public int TodayDay;

        /// <summary>
        /// 현재 달의 총 일수
        /// 달력 슬롯 UI를 몇 개까지 활성화할지 판단할 때 사용 가능
        /// </summary>
        public int DaysInMonth;

        /// <summary>
        /// 이번 달 현재까지의 누적 출석 횟수
        /// </summary>
        public int AttendanceCountThisMonth;

        /// <summary>
        /// 오늘 기준 연속 출석 일수
        /// 출석 보상 단계(7일/14일/21일/월간)를 표시할 때 사용
        /// </summary>
        public int CurrentStreak;

        /// <summary>
        /// 오늘 이미 출석했는지 여부
        /// true면 출석 버튼 비활성화,
        /// false면 출석 버튼 활성화 같은 UI 처리 가능
        /// </summary>
        public bool IsClaimedToday;

        /// <summary>
        /// 이번 달에 실제 출석 완료한 날짜 목록
        /// 예: [1,2,3,5,6]
        /// 달력 UI에서 체크 표시할 때 사용
        /// </summary>
        public List<int> ClaimedDays = new List<int>();
    }


    /// <summary>
    /// 오늘 출석 처리 시도
    /// - 하루 1회만 가능
    /// - 보상 지급 후 saveImmediately가 true면 즉시 Firebase 저장
    /// </summary>
    public static async Task<AttendanceClaimResult> TryClaimTodayAsync(Account acc, bool saveImmediately = true)
    {
        if (acc == null) throw new Exception("Account is null.");

        DateTime today = DateTime.Now.Date;
        string todayStr = today.ToString("yyyy-MM-dd");
        string monthKey = today.ToString("yyyy-MM");
        int todayDay = today.Day;
        int daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);

        EnsureAttendanceCollections(acc);
        NormalizeMonth(acc, today);

        var result = new AttendanceClaimResult
        {
            TodayString = todayStr,
            TodayDay = todayDay,
            DaysInMonth = daysInMonth,
            AttendanceCountThisMonth = acc.AttendanceCountThisMonth,
            CurrentStreak = CalculateCurrentMonthStreak(acc, todayDay)
        };

        // 이미 오늘 출석했는지 검사
        if (IsClaimedToday(acc, today))
        {
            result.Claimed = false;
            result.AlreadyClaimedToday = true;
            result.AttendanceCountThisMonth = acc.AttendanceCountThisMonth;
            result.CurrentStreak = CalculateCurrentMonthStreak(acc, todayDay);
            return result;
        }

        // 오늘 출석 등록
        AddClaimedDay(acc, todayDay);

        acc.LastAttendanceDate = todayStr;
        acc.AttendanceMonthKey = monthKey;
        acc.AttendanceCountThisMonth = acc.ClaimedAttendanceDays.Count;

        result.Claimed = true;
        result.AlreadyClaimedToday = false;
        result.AttendanceCountThisMonth = acc.AttendanceCountThisMonth;
        result.CurrentStreak = CalculateCurrentMonthStreak(acc, todayDay);

        // 1) 기본 보상
        acc.Cash += DAILY_CASH_REWARD;
        result.RewardMessages.Add($"일일 출석 보상: Cash +{DAILY_CASH_REWARD:0}");

        // 2) 연속 출석 보상
        if (result.CurrentStreak == 7)
        {
            acc.Cash += STREAK_7_CASH_REWARD;
            result.RewardMessages.Add($"7일 연속 출석 보상: Cash +{STREAK_7_CASH_REWARD:0}");
        }

        if (result.CurrentStreak == 14)
        {
            GrantRewardDice(acc, DiceGrade.Rare, 1, 1);
            result.RewardMessages.Add("14일 연속 출석 보상: Rare 주사위 1개 지급 (Lv1, Star1)");
        }

        if (result.CurrentStreak == 21)
        {
            acc.Cash += STREAK_21_CASH_REWARD;
            result.RewardMessages.Add($"21일 연속 출석 보상: Cash +{STREAK_21_CASH_REWARD:0}");
        }

        // 3) 한 달 연속 출석 보상
        // 해당 달 마지막 날에, 그 달 1일부터 오늘까지 전부 연속 출석이면 지급
        if (todayDay == daysInMonth && result.CurrentStreak == daysInMonth)
        {
            GrantRewardDice(acc, DiceGrade.Epic, 1, 1);
            result.RewardMessages.Add("한 달 연속 출석 보상: Epic 주사위 1개 지급 (Lv1, Star1)");
        }

        if (saveImmediately)
            await AccountCloudStore.SaveFullAsync(acc);

        Debug.Log($"[AttendanceService] {result.DebugSummary}");
        return result;
    }

    /// <summary>
    /// 현재 출석 상태 조회용
    /// </summary>
    public static AttendanceStatus GetStatus(Account acc)
    {
        if (acc == null) return null;

        DateTime today = DateTime.Now.Date;
        EnsureAttendanceCollections(acc);
        NormalizeMonth(acc, today);

        return new AttendanceStatus
        {
            TodayString = today.ToString("yyyy-MM-dd"),
            MonthKey = today.ToString("yyyy-MM"),
            TodayDay = today.Day,
            DaysInMonth = DateTime.DaysInMonth(today.Year, today.Month),
            AttendanceCountThisMonth = acc.AttendanceCountThisMonth,
            CurrentStreak = CalculateCurrentMonthStreak(acc, today.Day),
            IsClaimedToday = IsClaimedToday(acc, today),
            ClaimedDays = new List<int>(acc.ClaimedAttendanceDays.OrderBy(x => x))
        };
    }

    /// <summary>
    /// 달이 바뀌면 월간 출석 데이터 초기화
    /// </summary>
    private static void NormalizeMonth(Account acc, DateTime today)
    {
        string currentMonthKey = today.ToString("yyyy-MM");

        if (string.IsNullOrWhiteSpace(acc.AttendanceMonthKey))
        {
            acc.AttendanceMonthKey = currentMonthKey;
        }

        if (!string.Equals(acc.AttendanceMonthKey, currentMonthKey, StringComparison.Ordinal))
        {
            acc.AttendanceMonthKey = currentMonthKey;
            acc.AttendanceCountThisMonth = 0;
            acc.ClaimedAttendanceDays.Clear();
            acc.LastAttendanceDate = "";
        }

        // 방어: 중복 제거 + 정렬 보정
        acc.ClaimedAttendanceDays = acc.ClaimedAttendanceDays
            .Where(day => day >= 1 && day <= DateTime.DaysInMonth(today.Year, today.Month))
            .Distinct()
            .OrderBy(day => day)
            .ToList();

        acc.AttendanceCountThisMonth = acc.ClaimedAttendanceDays.Count;
    }

    /// <summary>
    /// 오늘 출석 여부
    /// </summary>
    private static bool IsClaimedToday(Account acc, DateTime today)
    {
        EnsureAttendanceCollections(acc);

        string todayStr = today.ToString("yyyy-MM-dd");
        if (!string.IsNullOrWhiteSpace(acc.LastAttendanceDate) &&
            acc.LastAttendanceDate == todayStr)
        {
            return true;
        }

        return acc.ClaimedAttendanceDays.Contains(today.Day);
    }

    /// <summary>
    /// 현재 달 기준 "오늘까지 몇 일 연속 출석했는지" 계산
    /// 예:
    /// claimed=[1,2,3,5,6,7], today=7 => 3
    /// claimed=[1,2,3,4,5,6,7], today=7 => 7
    /// </summary>
    private static int CalculateCurrentMonthStreak(Account acc, int todayDay)
    {
        EnsureAttendanceCollections(acc);

        var set = new HashSet<int>(acc.ClaimedAttendanceDays);
        int streak = 0;

        for (int day = todayDay; day >= 1; day--)
        {
            if (set.Contains(day))
                streak++;
            else
                break;
        }

        return streak;
    }

    /// <summary>
    /// 오늘 날짜를 출석 목록에 추가
    /// </summary>
    private static void AddClaimedDay(Account acc, int day)
    {
        EnsureAttendanceCollections(acc);

        if (!acc.ClaimedAttendanceDays.Contains(day))
            acc.ClaimedAttendanceDays.Add(day);

        acc.ClaimedAttendanceDays = acc.ClaimedAttendanceDays
            .Distinct()
            .OrderBy(x => x)
            .ToList();
    }

    /// <summary>
    /// 보상 주사위 지급
    /// - 같은 Grade+Star가 이미 있으면 Count +1
    /// - 없으면 새로 추가
    ///
    /// 현재 프로젝트 인벤토리 구조와 맞추기 위해
    /// Level=1, Count=1, Exp=0, Shard=0, PromoteExp=0 으로 생성
    /// </summary>
    private static void GrantRewardDice(Account acc, DiceGrade grade, int star, int level)
    {
        if (acc.DiceInventory == null)
            acc.DiceInventory = new List<OwnedDice>();

        var found = acc.DiceInventory.Find(d => d != null && d.Grade == grade && d.Star == star);

        if (found != null)
        {
            found.Count += 1;
            return;
        }

        var rewardDice = new OwnedDice
        {
            Grade = grade,
            Star = Mathf.Clamp(star, 1, 5),
            Level = Mathf.Clamp(level, 1, 10),
            Count = 1,
            Exp = 0,
            Shard = 0,
            PromoteExp = 0
        };

        acc.DiceInventory.Add(rewardDice);
    }

    private static void EnsureAttendanceCollections(Account acc)
    {
        acc.ClaimedAttendanceDays ??= new List<int>();
        acc.DiceInventory ??= new List<OwnedDice>();
    }
}
