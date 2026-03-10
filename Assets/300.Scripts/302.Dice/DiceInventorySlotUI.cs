using System;
using UnityEngine;
using UnityEngine.UI;

public class DiceInventorySlotUI : MonoBehaviour
{
    public enum SlotMode
    {
        Equip,
        Promote
    }

    [Header("UI")]
    [SerializeField] private Image imgDice;
    [SerializeField] private Text txtGrade;      // 예: Legendary
    [SerializeField] private Text txtInfo;      // 예: ★5 / Lv10
    [SerializeField] private Text txtCount;     // 예: x3
    [SerializeField] private Text txtExtra;     // 예: PromoteExp 5/5 or Shard
    [SerializeField] private GameObject equippedMark; // "장착중" 뱃지
    [SerializeField] private GameObject selectableHighlight;

    RainbowImageEffect _rainbow;

    private OwnedDice _dice;
    private SlotMode _mode;

    public void Bind(OwnedDice dice, SlotMode mode, bool isEquipped, Action onClick)
    {
        _dice = dice;
        _mode = mode;

        bool isEng = false;
        if (LaguageManager.Instance != null)
            isEng = (LaguageManager.Instance.currentLang == Lauaguage.Eng);

        if (_dice != null)
        {

            DiceGradeColorUtil.Apply(imgDice, _dice.Grade, ref _rainbow);

            // 등급명 다국어
            string gradeText = GetGradeText(_dice.Grade, isEng);

            if (txtGrade != null)
                txtGrade.text = gradeText;

            // 정보 텍스트 다국어
            // 예: ★3 Lv.10 / ★3 레벨 10
            if (txtInfo != null)
            {
                txtInfo.text = isEng
                    ? $"★{_dice.Star}  Lv.{_dice.Level}"
                    : $"★{_dice.Star}  레벨 {_dice.Level}";
            }

            // 개수 텍스트 다국어
            if (txtCount != null)
            {
                int count = Mathf.Max(0, _dice.Count);
                txtCount.text = isEng ? $"x{count}" : $"보유 {count}개";
                // 한국어도 x3 스타일로 통일하고 싶으면 -> $"x{count}"
            }

            // 추가 정보(승급/조각) 다국어
            if (txtExtra != null)
            {
                if (_mode == SlotMode.Promote)
                {
                    int p = Mathf.Max(0, _dice.PromoteExp);
                    txtExtra.text = isEng
                        ? $"Promote {p}/{DiceProgression.GRADE_UP_COST}"
                        : $"승급치 {p}/{DiceProgression.GRADE_UP_COST}";
                }
                else
                {
                    int shard = Mathf.Max(0, _dice.Shard);
                    txtExtra.text = isEng
                        ? $"Shard {shard}"
                        : $"조각 {shard}";
                }
            }
        }
        else
        {
            DiceGradeColorUtil.Apply(imgDice, DiceGrade.Common, ref _rainbow);

            if (txtGrade != null) txtGrade.text = "-";
            if (txtInfo != null) txtInfo.text = "";
            if (txtCount != null) txtCount.text = "";
            if (txtExtra != null) txtExtra.text = "";
        }

        if (equippedMark != null) equippedMark.SetActive(isEquipped);
        if (selectableHighlight != null) selectableHighlight.SetActive(false);
    }

    private string GetGradeText(DiceGrade grade, bool isEng)
    {
        if (isEng)
        {
            // enum 그대로 써도 되지만 표시명 통일용으로 분리 추천
            return grade switch
            {
                DiceGrade.Common => "Common",
                DiceGrade.Rare => "Rare",
                DiceGrade.Epic => "Epic",
                DiceGrade.Legendary => "Legendary",
                _ => "Unknown"
            };
        }

        return grade switch
        {
            DiceGrade.Common => "일반",
            DiceGrade.Rare => "희귀",
            DiceGrade.Epic => "영웅",
            DiceGrade.Legendary => "전설",
            _ => "알 수 없음"
        };
    }

    public void SetSelected(bool selected)
    {
        if (selectableHighlight != null) selectableHighlight.SetActive(selected);
    }
}
