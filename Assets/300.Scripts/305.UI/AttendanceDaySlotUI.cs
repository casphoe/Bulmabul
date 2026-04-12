using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AttendanceDaySlotUI : MonoBehaviour
{
    [SerializeField] private TMP_Text txtDay;
    [SerializeField] private TMP_Text txtReward;
    [SerializeField] private Image bg;
    [SerializeField] private Image rewardIcon;
    [SerializeField] private GameObject checkMark;
    [SerializeField] private GameObject todayMark;

    [Header("Today Blink")]
    [SerializeField] private bool useTodayBlink = true;
    [SerializeField] private float blinkSpeed = 2.5f;
    [SerializeField] private float todayMinAlpha = 0.65f;
    [SerializeField] private float todayMaxAlpha = 1f;

    private RainbowImageEffect _rewardRainbow;

    private bool _isToday;
    private bool _isClaimed;

    private readonly Color _claimedColor = new Color(0.75f, 0.90f, 1f, 1f);
    private readonly Color _todayColor = new Color(1f, 0.95f, 0.70f, 1f);
    private readonly Color _normalColor = Color.white;

    public void SetDay(
        int day,
        string rewardText,
        Sprite rewardSprite,
        bool isDiceReward,
        DiceGrade rewardDiceGrade,
        bool isClaimed,
        bool isToday)
    {
        _isClaimed = isClaimed;
        _isToday = isToday;

        if (txtDay != null)
            txtDay.text = day.ToString();

        if (txtReward != null)
            txtReward.text = rewardText;

        if (rewardIcon != null)
        {
            rewardIcon.sprite = rewardSprite;
            rewardIcon.gameObject.SetActive(rewardSprite != null);

            if (rewardSprite != null)
            {
                if (isDiceReward)
                {
                    DiceGradeColorUtil.Apply(rewardIcon, rewardDiceGrade, ref _rewardRainbow);
                }
                else
                {
                    if (_rewardRainbow != null)
                        _rewardRainbow.enabled = false;

                    rewardIcon.color = Color.white;
                }
            }
        }

        if (checkMark != null)
            checkMark.SetActive(isClaimed);

        if (todayMark != null)
            todayMark.SetActive(isToday);

        ApplyBaseBgColor();
    }

    private void Update()
    {
        if (bg == null)
            return;

        if (!useTodayBlink || !_isToday || _isClaimed)
            return;

        Color c = _todayColor;
        float t = (Mathf.Sin(Time.unscaledTime * blinkSpeed) + 1f) * 0.5f;
        c.a = Mathf.Lerp(todayMinAlpha, todayMaxAlpha, t);
        bg.color = c;
    }

    private void ApplyBaseBgColor()
    {
        if (bg == null)
            return;

        if (_isClaimed)
        {
            bg.color = _claimedColor;
        }
        else if (_isToday)
        {
            bg.color = _todayColor;
        }
        else
        {
            bg.color = _normalColor;
        }
    }
}