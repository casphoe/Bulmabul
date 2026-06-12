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
/// - 14일 연속 출석: Rare 주사위 1개 지급
/// - 21일 연속 출석: Cash +3000
/// - 해당 월의 모든 날짜를 연속 출석: Epic 주사위 1개 지급
///
/// 기준 시간:
/// - 한국 시간 UTC+9 기준 날짜 사용
/// - DateTime.UtcNow.AddHours(9).Date 사용
/// - PC 로컬 시간, 윈도우 시간대, Unity Editor 실행 환경의 영향을 줄이기 위함
///
/// 주의:
/// - 현재 코드는 "한국 시간 자정 00:00" 기준으로 날짜가 바뀜
/// - 예: 한국 시간 2026-04-29 23:59 => 4월 29일 출석
/// - 예: 한국 시간 2026-04-30 00:00 => 4월 30일 출석 가능
///
/// 참고:
/// - "아침 9시에 출석 날짜가 바뀌게" 하고 싶은 경우에는 GetTodayKst()가 아니라
///   별도의 오전 9시 기준 날짜 계산 함수가 필요함
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
        /// 중복 제거된 ClaimedAttendanceDays 개수와 동일한 의미
        /// </summary>
        public int AttendanceCountThisMonth;

        /// <summary>
        /// 오늘을 기준으로 역산했을 때의 연속 출석 일수
        /// 예:
        /// claimed=[1,2,3,4,5], today=5 => 5
        /// claimed=[1,2,3,5], today=5 => 1
        /// </summary>
        public int CurrentStreak;

        /// <summary>
        /// 이번 출석 처리로 지급된 보상 메시지 목록
        /// UI 팝업이나 토스트에 그대로 활용 가능
        /// </summary>
        public List<string> RewardMessages = new List<string>();

        /// <summary>
        /// 디버그 로그 출력용 요약 문자열
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
        /// 출석 보상 단계 표시용
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
    ///
    /// 처리 순서:
    /// 1. 한국 시간 기준 오늘 날짜 계산
    /// 2. 출석 데이터 컬렉션 보정
    /// 3. 월이 바뀌었으면 월간 출석 데이터 초기화
    /// 4. 오늘 이미 출석했는지 검사
    /// 5. 오늘 출석 등록
    /// 6. 일일 보상 지급
    /// 7. 연속 출석 보상 지급
    /// 8. saveImmediately가 true면 Firebase 전체 저장
    /// </summary>
    public static async Task<AttendanceClaimResult> TryClaimTodayAsync(Account acc, bool saveImmediately = true)
    {
        if (acc == null)
            throw new Exception("Account is null.");

        // 한국 시간 UTC+9 기준 오늘 날짜
        DateTime today = GetTodayKst();

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

        // 디버그용: 출석 처리 전 상태 확인
        Debug.Log(
            $"[AttendanceService] TryClaim Before / " +
            $"KST Today={todayStr}, " +
            $"MonthKey={monthKey}, " +
            $"LastAttendanceDate={acc.LastAttendanceDate}, " +
            $"AccountMonthKey={acc.AttendanceMonthKey}, " +
            $"ClaimedDays=[{string.Join(",", acc.ClaimedAttendanceDays)}], " +
            $"Cash={acc.Cash}"
        );

        // 이미 오늘 출석했으면 중복 보상 지급 없이 종료
        if (IsClaimedToday(acc, today))
        {
            result.Claimed = false;
            result.AlreadyClaimedToday = true;
            result.AttendanceCountThisMonth = acc.AttendanceCountThisMonth;
            result.CurrentStreak = CalculateCurrentMonthStreak(acc, todayDay);

            Debug.Log($"[AttendanceService] Already claimed today. {result.DebugSummary}");
            return result;
        }

        // 오늘 날짜를 출석 목록에 추가
        AddClaimedDay(acc, todayDay);

        // 마지막 출석일 및 월 정보 갱신
        // 날짜는 yyyy-MM-dd만 저장한다. 시간은 저장하지 않는다.
        acc.LastAttendanceDate = todayStr;
        acc.IsAttendanceCheckedToday = true;
        acc.AttendanceMonthKey = monthKey;
        acc.AttendanceCountThisMonth = acc.ClaimedAttendanceDays.Count;

        result.Claimed = true;
        result.AlreadyClaimedToday = false;
        result.AttendanceCountThisMonth = acc.AttendanceCountThisMonth;
        result.CurrentStreak = CalculateCurrentMonthStreak(acc, todayDay);

        // 1. 기본 일일 보상
        acc.Cash += DAILY_CASH_REWARD;
        result.RewardMessages.Add($"일일 출석 보상: Cash +{DAILY_CASH_REWARD:0}");

        // 2. 7일 연속 출석 보상
        if (result.CurrentStreak == 7)
        {
            acc.Cash += STREAK_7_CASH_REWARD;
            result.RewardMessages.Add($"7일 연속 출석 보상: Cash +{STREAK_7_CASH_REWARD:0}");
        }

        // 3. 14일 연속 출석 보상
        if (result.CurrentStreak == 14)
        {
            GrantRewardDice(acc, DiceGrade.Rare, 1, 1);
            result.RewardMessages.Add("14일 연속 출석 보상: Rare 주사위 1개 지급 (Lv1, Star1)");
        }

        // 4. 21일 연속 출석 보상
        if (result.CurrentStreak == 21)
        {
            acc.Cash += STREAK_21_CASH_REWARD;
            result.RewardMessages.Add($"21일 연속 출석 보상: Cash +{STREAK_21_CASH_REWARD:0}");
        }

        // 5. 한 달 전체 연속 출석 보상
        // 해당 달 마지막 날에, 1일부터 마지막 날까지 전부 출석했을 때 지급
        if (todayDay == daysInMonth && result.CurrentStreak == daysInMonth)
        {
            GrantRewardDice(acc, DiceGrade.Epic, 1, 1);
            result.RewardMessages.Add("한 달 연속 출석 보상: Epic 주사위 1개 지급 (Lv1, Star1)");
        }

        // Firebase 저장
        if (saveImmediately)
        {
            await AccountCloudStore.SaveFullAsync(acc);
        }

        Debug.Log(
            $"[AttendanceService] TryClaim Success / {result.DebugSummary}, " +
            $"RewardCount={result.RewardMessages.Count}, " +
            $"Cash={acc.Cash}"
        );

        return result;
    }

    /// <summary>
    /// 현재 출석 상태 조회용
    ///
    /// 중요:
    /// - 출석 처리와 동일하게 한국 시간 UTC+9 기준을 사용해야 함
    /// - 여기서 DateTime.Now.Date를 사용하면 TryClaimTodayAsync와 날짜 기준이 달라질 수 있음
    /// </summary>
    public static AttendanceStatus GetStatus(Account acc)
    {
        if (acc == null)
            return null;

        // 한국 시간 UTC+9 기준 오늘 날짜
        DateTime today = GetTodayKst();

        EnsureAttendanceCollections(acc);
        NormalizeMonth(acc, today);

        string todayStr = today.ToString("yyyy-MM-dd");
        string monthKey = today.ToString("yyyy-MM");
        int todayDay = today.Day;
        int daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);

        var status = new AttendanceStatus
        {
            TodayString = todayStr,
            MonthKey = monthKey,
            TodayDay = todayDay,
            DaysInMonth = daysInMonth,
            AttendanceCountThisMonth = acc.AttendanceCountThisMonth,
            CurrentStreak = CalculateCurrentMonthStreak(acc, todayDay),
            IsClaimedToday = IsClaimedToday(acc, today),
            ClaimedDays = new List<int>(acc.ClaimedAttendanceDays.OrderBy(x => x))
        };

        Debug.Log(
            $"[AttendanceService] GetStatus / " +
            $"KST Today={status.TodayString}, " +
            $"MonthKey={status.MonthKey}, " +
            $"TodayDay={status.TodayDay}, " +
            $"IsClaimedToday={status.IsClaimedToday}, " +
            $"Count={status.AttendanceCountThisMonth}, " +
            $"Streak={status.CurrentStreak}, " +
            $"ClaimedDays=[{string.Join(",", status.ClaimedDays)}]"
        );

        return status;
    }

    /// <summary>
    /// 달이 바뀌면 월간 출석 데이터 초기화
    ///
    /// 예:
    /// - acc.AttendanceMonthKey = "2026-04"
    /// - 오늘 한국 시간 기준 monthKey = "2026-05"
    /// => 5월이 되었으므로 4월 출석 기록 초기화
    /// </summary>
    private static void NormalizeMonth(Account acc, DateTime today)
    {
        EnsureAttendanceCollections(acc);

        string currentMonthKey = today.ToString("yyyy-MM");

        // 기존 월 키가 없으면 현재 월로 초기화
        if (string.IsNullOrWhiteSpace(acc.AttendanceMonthKey))
        {
            acc.AttendanceMonthKey = currentMonthKey;
        }

        // 저장된 월과 현재 월이 다르면 월간 출석 정보 초기화
        if (!string.Equals(acc.AttendanceMonthKey, currentMonthKey, StringComparison.Ordinal))
        {
            Debug.Log(
                $"[AttendanceService] Month changed. " +
                $"OldMonth={acc.AttendanceMonthKey}, NewMonth={currentMonthKey}. " +
                $"Reset attendance data."
            );

            acc.AttendanceMonthKey = currentMonthKey;
            acc.AttendanceCountThisMonth = 0;
            acc.ClaimedAttendanceDays.Clear();
            acc.LastAttendanceDate = "";
            acc.IsAttendanceCheckedToday = false;
        }

        // 현재 달의 실제 일수
        int daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);

        // 방어 처리:
        // - 1보다 작은 날짜 제거
        // - 현재 달의 마지막 일보다 큰 날짜 제거
        // - 중복 제거
        // - 오름차순 정렬
        acc.ClaimedAttendanceDays = acc.ClaimedAttendanceDays
            .Where(day => day >= 1 && day <= daysInMonth)
            .Distinct()
            .OrderBy(day => day)
            .ToList();

        // 출석 횟수는 실제 출석 날짜 목록 개수로 보정
        acc.AttendanceCountThisMonth = acc.ClaimedAttendanceDays.Count;
    }

    /// <summary>
    /// 오늘 이미 출석했는지 검사
    ///
    /// 핵심 규칙:
    /// - 시간은 비교하지 않고 yyyy-MM-dd 날짜만 비교한다.
    /// - 오늘 출석 완료로 인정하려면 아래 3개가 모두 맞아야 한다.
    ///   1. IsAttendanceCheckedToday == true
    ///   2. LastAttendanceDate == 오늘 날짜
    ///   3. LoginDate == 오늘 날짜
    ///
    /// 예외:
    /// - IsAttendanceCheckedToday가 true이고 LastAttendanceDate도 오늘인데,
    ///   LoginDate가 어제라면 오늘 출석한 상태로 보지 않는다.
    /// - 이 경우 꼬인 출석 상태로 판단하고 오늘 체크 상태를 false로 보정한다.
    /// </summary>
    private static bool IsClaimedToday(Account acc, DateTime today)
    {
        EnsureAttendanceCollections(acc);

        string todayStr = today.ToString("yyyy-MM-dd");
        string attendanceDateKey = GetDateKeyOnly(acc.LastAttendanceDate);
        string loginDateKey = GetDateKeyOnly(acc.LoginDate);

        bool checkedFlag = acc.IsAttendanceCheckedToday;
        bool attendanceDateIsToday = string.Equals(attendanceDateKey, todayStr, StringComparison.Ordinal);
        bool loginDateIsToday = string.Equals(loginDateKey, todayStr, StringComparison.Ordinal);

        Debug.Log(
            $"[AttendanceService] IsClaimedToday Check / " +
            $"Today={todayStr}, " +
            $"CheckedFlag={checkedFlag}, " +
            $"LastAttendanceDate={acc.LastAttendanceDate}, " +
            $"AttendanceDateKey={attendanceDateKey}, " +
            $"LoginDate={acc.LoginDate}, " +
            $"LoginDateKey={loginDateKey}, " +
            $"AttendanceDateIsToday={attendanceDateIsToday}, " +
            $"LoginDateIsToday={loginDateIsToday}"
        );

        // 정상적인 오늘 출석 완료 상태
        if (checkedFlag && attendanceDateIsToday && loginDateIsToday)
        {
            return true;
        }

        // bool은 true이고 출석 날짜도 오늘인데, 최근 로그인 날짜가 오늘이 아니면
        // 오늘 출석 완료 상태로 보지 않는다.
        if (checkedFlag && attendanceDateIsToday && !loginDateIsToday)
        {
            Debug.LogWarning(
                "[AttendanceService] Stale attendance state detected. " +
                "CheckedFlag=true and LastAttendanceDate=today, but LoginDate is not today. " +
                "Reset today's attendance flag."
            );

            acc.IsAttendanceCheckedToday = false;

            // 오늘 날짜가 출석 목록에 잘못 들어가 있으면 제거한다.
            // 그래야 다시 출석 처리할 때 오늘 날짜가 정상적으로 추가된다.
            acc.ClaimedAttendanceDays.Remove(today.Day);
            acc.AttendanceCountThisMonth = acc.ClaimedAttendanceDays.Count;

            return false;
        }

        // 그 외에는 오늘 출석하지 않은 상태
        return false;
    }

    /// <summary>
    /// 날짜 문자열에서 yyyy-MM-dd만 뽑아낸다.
    /// 
    /// 지원 예:
    /// - "2026-06-12"
    /// - "2026-06-12 13:45:20"
    /// - "2026-06-12T13:45:20"
    /// 
    /// 시간은 출석 비교에 사용하지 않는다.
    /// </summary>
    private static string GetDateKeyOnly(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        value = value.Trim();

        if (DateTime.TryParse(value, out DateTime parsed))
            return parsed.ToString("yyyy-MM-dd");

        if (value.Length >= 10)
            return value.Substring(0, 10);

        return value;
    }

    /// <summary>
    /// 현재 달 기준 "오늘까지 몇 일 연속 출석했는지" 계산
    ///
    /// 예:
    /// claimed=[1,2,3,5,6,7], today=7 => 3
    /// claimed=[1,2,3,4,5,6,7], today=7 => 7
    /// claimed=[1,3,4,5], today=5 => 3
    /// </summary>
    private static int CalculateCurrentMonthStreak(Account acc, int todayDay)
    {
        EnsureAttendanceCollections(acc);

        var set = new HashSet<int>(acc.ClaimedAttendanceDays);
        int streak = 0;

        for (int day = todayDay; day >= 1; day--)
        {
            if (set.Contains(day))
            {
                streak++;
            }
            else
            {
                break;
            }
        }

        return streak;
    }

    /// <summary>
    /// 오늘 날짜를 출석 목록에 추가
    ///
    /// 중복으로 같은 날짜가 들어가지 않도록 방어 처리
    /// </summary>
    private static void AddClaimedDay(Account acc, int day)
    {
        EnsureAttendanceCollections(acc);

        if (!acc.ClaimedAttendanceDays.Contains(day))
        {
            acc.ClaimedAttendanceDays.Add(day);
        }

        acc.ClaimedAttendanceDays = acc.ClaimedAttendanceDays
            .Distinct()
            .OrderBy(x => x)
            .ToList();
    }

    /// <summary>
    /// 보상 주사위 지급
    ///
    /// 처리 방식:
    /// - 같은 Grade + Star 주사위가 이미 있으면 Count +1
    /// - 없으면 새 OwnedDice 생성 후 인벤토리에 추가
    ///
    /// 현재 프로젝트 인벤토리 구조와 맞추기 위해:
    /// - Level=1
    /// - Count=1
    /// - Exp=0
    /// - Shard=0
    /// - PromoteExp=0
    /// 으로 생성
    /// </summary>
    private static void GrantRewardDice(Account acc, DiceGrade grade, int star, int level)
    {
        if (acc.DiceInventory == null)
        {
            acc.DiceInventory = new List<OwnedDice>();
        }

        var found = acc.DiceInventory.Find(d =>
            d != null &&
            d.Grade == grade &&
            d.Star == star
        );

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

    /// <summary>
    /// 출석 서비스에서 사용하는 컬렉션 null 방어
    ///
    /// Account를 새로 만들었거나,
    /// 예전 저장 데이터에서 리스트가 null로 로드되는 경우를 방지
    /// </summary>
    private static void EnsureAttendanceCollections(Account acc)
    {
        acc.ClaimedAttendanceDays ??= new List<int>();
        acc.DiceInventory ??= new List<OwnedDice>();
    }

    /// <summary>
    /// 한국 시간 UTC+9 기준 오늘 날짜를 반환
    ///
    /// 중요:
    /// - DateTime.Now를 직접 사용하지 않음
    /// - UTC 기준 시간에 9시간을 더해서 한국 날짜를 계산
    /// - 반환값은 시간 정보가 제거된 Date 값
    ///
    /// 예:
    /// - UTC 2026-04-28 15:00
    /// - KST 2026-04-29 00:00
    /// - 반환: 2026-04-29 00:00:00
    /// </summary>
    private static DateTime GetTodayKst()
    {
        return DateTime.UtcNow.AddHours(9).Date;
    }
}