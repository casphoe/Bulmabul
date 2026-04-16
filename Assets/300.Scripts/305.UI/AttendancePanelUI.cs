using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AttendancePanelUI : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject panelRoot;

    [Header("Texts")]
    [SerializeField] private TMP_Text txtMonth;
    [SerializeField] private TMP_Text txtAttendance;
    [SerializeField] private TMP_Text txtStreak;
    [SerializeField] private TMP_Text txtRewardInfo;

    [Header("Optional Close Button")]
    [SerializeField] private Button btnClose;

    [Header("Auto Close")]
    [SerializeField] private bool autoCloseOnAutoClaim = true;
    private float autoCloseDelay = 7.0f;

    private AttendanceService.AttendanceClaimResult _lastAutoClaimResult;
    private bool _toastShownForThisOpen;

    [Header("Day Slots (1~31)")]
    [SerializeField] private List<AttendanceDaySlotUI> daySlots = new List<AttendanceDaySlotUI>();

    [Header("Reward Sprites")]
    [SerializeField] private Sprite cashRewardSprite;
    [SerializeField] private Sprite diceRewardSprite;

    private Account _account;
    private Coroutine _autoCloseCoroutine;

    private void Awake()
    {
        if (btnClose != null)
            btnClose.onClick.AddListener(Close);

        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    private void Start()
    {
        if (FireBaseAuthManager.Instance == null || FireBaseAuthManager.Instance.CurrentAccount == null)
            return;

        _account = FireBaseAuthManager.Instance.CurrentAccount;

        RefreshUI();

        if (AttendanceLoginSessionBridge.TryConsumePendingResult(out var autoClaimResult))
        {
            _lastAutoClaimResult = autoClaimResult;
            _toastShownForThisOpen = false;

            RefreshUI(autoClaimResult);
            Open();

            StartCoroutine(CoShowToastNextFrame());

            if (autoCloseOnAutoClaim)
            {
                if (_autoCloseCoroutine != null)
                    StopCoroutine(_autoCloseCoroutine);

                _autoCloseCoroutine = StartCoroutine(CoCloseAfterToastAndDelay(autoCloseDelay));
            }
        }
    }

    private IEnumerator CoShowToastNextFrame()
    {
        yield return null;
        ShowAttendanceToastOnOpen();
    }

    private void ShowAttendanceToastOnOpen()
    {
        if (_toastShownForThisOpen)
            return;

        if (_lastAutoClaimResult == null)
            return;

        if (ToastMessageManager.instance == null)
            return;

        var result = _lastAutoClaimResult;
        _toastShownForThisOpen = true;

        if (result.AlreadyClaimedToday)
        {
            ToastMessageManager.instance.ShowToast(
                $"오늘은 이미 출석 완료! ({result.AttendanceCountThisMonth}/{result.DaysInMonth})",
                $"Today's attendance already claimed! ({result.AttendanceCountThisMonth}/{result.DaysInMonth})"
            );
            return;
        }

        if (result.Claimed)
        {
            string rewardTextKor = string.Join("\n", result.RewardMessages);
            string rewardTextEng = ConvertRewardMessages(result.RewardMessages, true);

            ToastMessageManager.instance.ShowToast(
                $"출석 완료!\n{result.CurrentStreak}일 차 출석\n{rewardTextKor}",
                $"Attendance complete!\nDay {result.CurrentStreak} check-in\n{rewardTextEng}"
            );
        }
    }

    public void RefreshUI()
    {
        if (_account == null) return;

        var status = AttendanceService.GetStatus(_account);
        if (status == null) return;

        bool isEng = IsEnglish();

        if (txtMonth != null)
            txtMonth.text = isEng
                ? $"{status.MonthKey} Attendance Check"
                : $"{status.MonthKey} 출석체크";

        if (txtAttendance != null)
            txtAttendance.text = isEng
                ? $"Attendance Count : {status.AttendanceCountThisMonth} / {status.DaysInMonth}"
                : $"출석 횟수 : {status.AttendanceCountThisMonth} / {status.DaysInMonth}";

        if (txtStreak != null)
            txtStreak.text = isEng
                ? $"Attendance Streak : {status.CurrentStreak} days"
                : $"연속 출석 : {status.CurrentStreak}일";

        if (txtRewardInfo != null)
        {
            txtRewardInfo.text = GetRewardGuideText(isEng);
            txtRewardInfo.enableAutoSizing = true;
            txtRewardInfo.fontSizeMin = 12;
            txtRewardInfo.fontSizeMax = 24;
            txtRewardInfo.overflowMode = TextOverflowModes.Overflow;
        }

        BindDaySlots(status);
    }

    public void RefreshUI(AttendanceService.AttendanceClaimResult result)
    {
        RefreshUI();

        if (result == null || txtRewardInfo == null) return;

        bool isEng = IsEnglish();
        string rewardLines = ConvertRewardMessages(result.RewardMessages, isEng);

        txtRewardInfo.text =
            (isEng ? "Attendance Rewards for This Login\n" : "이번 로그인 출석 보상\n") +
            rewardLines;
    }

    private void BindDaySlots(AttendanceService.AttendanceStatus status)
    {
        if (daySlots == null || daySlots.Count == 0) return;

        HashSet<int> claimedSet = new HashSet<int>(status.ClaimedDays);

        for (int i = 0; i < daySlots.Count; i++)
        {
            int day = i + 1;
            bool validDay = day <= status.DaysInMonth;

            if (daySlots[i] == null) continue;

            if (!validDay)
            {
                daySlots[i].gameObject.SetActive(false);
                continue;
            }

            daySlots[i].gameObject.SetActive(true);

            string rewardText = GetDayRewardText(day, status.DaysInMonth, IsEnglish());
            bool isDiceReward = TryGetDayDiceGrade(day, status.DaysInMonth, out DiceGrade diceGrade);
            Sprite rewardSprite = isDiceReward ? diceRewardSprite : cashRewardSprite;

            daySlots[i].SetDay(
                day,
                rewardText,
                rewardSprite,
                isDiceReward,
                diceGrade,
                claimedSet.Contains(day),
                day == status.TodayDay
            );
        }
    }

    private bool TryGetDayDiceGrade(int day, int daysInMonth, out DiceGrade grade)
    {
        if (day == daysInMonth)
        {
            grade = DiceGrade.Epic;
            return true;
        }

        if (day == 14)
        {
            grade = DiceGrade.Rare;
            return true;
        }

        grade = DiceGrade.Common;
        return false;
    }

    private string GetDayRewardText(int day, int daysInMonth, bool isEng)
    {
        if (day == daysInMonth)
            return isEng ? "Epic Dice x1" : "Epic 주사위 x1";

        if (day == 21)
            return "Cash 3000";

        if (day == 14)
            return isEng ? "Rare Dice x1" : "Rare 주사위 x1";

        if (day == 7)
            return "Cash 1000";

        return "Cash 100";
    }

    private IEnumerator CoCloseAfterToastAndDelay(float extraDelayAfterToast)
    {
        float toastTotal = 0f;

        if (ToastMessageManager.instance != null)
        {
            toastTotal =
                ToastMessageManager.instance.toastShowSeconds +
                ToastMessageManager.instance.toastFadeSeconds;
        }

        yield return new WaitForSecondsRealtime(toastTotal + extraDelayAfterToast);

        Close();
        _autoCloseCoroutine = null;
    }

    public void Open()
    {
        if (panelRoot != null)
            panelRoot.SetActive(true);
    }

    public void Close()
    {
        if (_autoCloseCoroutine != null)
        {
            StopCoroutine(_autoCloseCoroutine);
            _autoCloseCoroutine = null;
        }

        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    private bool IsEnglish()
    {
        if (LaguageManager.Instance == null)
            return false;

        return LaguageManager.Instance.currentLang == Lauaguage.Eng;
    }

    private string GetRewardGuideText(bool isEng)
    {
        if (isEng)
        {
            return
                "Daily Check-in : Cash 100\n" +
                "7-Day Streak : Cash 1000\n" +
                "14-Day Streak : 1 Rare Dice (Level 1, Star 1)\n" +
                "21-Day Streak : Cash 3000\n" +
                "Full-Month Streak : 1 Epic Dice (Level 1, Star 1)";
        }

        return
            "매일 출석 : Cash 100\n" +
            "7일 연속 : Cash 1000\n" +
            "14일 연속 : Rare 주사위 1개 (레벨 1, 별 1)\n" +
            "21일 연속 : Cash 3000\n" +
            "한 달 연속 : Epic 주사위 1개 (레벨 1, 별 1)";
    }

    private string ConvertRewardMessages(List<string> rewardMessages, bool isEng)
    {
        if (rewardMessages == null || rewardMessages.Count == 0)
            return string.Empty;

        StringBuilder sb = new StringBuilder();

        for (int i = 0; i < rewardMessages.Count; i++)
        {
            string line = rewardMessages[i];

            if (isEng)
            {
                if (line.Contains("일일 출석 보상"))
                    line = "Daily Check-in Reward: Cash +100";
                else if (line.Contains("7일 연속 출석 보상"))
                    line = "7-Day Streak Reward: Cash +1000";
                else if (line.Contains("14일 연속 출석 보상"))
                    line = "14-Day Streak Reward: 1 Rare Dice granted (Level 1, Star 1)";
                else if (line.Contains("21일 연속 출석 보상"))
                    line = "21-Day Streak Reward: Cash +3000";
                else if (line.Contains("한 달 연속 출석 보상"))
                    line = "Full-Month Streak Reward: 1 Epic Dice granted (Level 1, Star 1)";
            }

            sb.Append(line);

            if (i < rewardMessages.Count - 1)
                sb.Append("\n");
        }

        return sb.ToString();
    }
}