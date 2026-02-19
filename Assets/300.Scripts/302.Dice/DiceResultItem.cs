using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class DiceResultItem : MonoBehaviour
{
    [Header("UI")]
    public Image imgDice;        // 주사위 이미지(또는 배경 프레임)
    public Text txtLabel;        // "Epic ★3 Lv7" 이런 표시용(선택)

    RainbowImageEffect _rainbow;

    public void Set(Dice d)
    {
        if (txtLabel != null)
            txtLabel.text = $"{d.grade} ★{d.star} Lv{d.level}";

        ApplyGradeColor(d.grade);
    }

    void ApplyGradeColor(DiceGrade grade)
    {
        if (imgDice == null) return;

        // 레전드면 레인보우 애니메이션
        if (grade == DiceGrade.Legendary)
        {
            if (_rainbow == null) _rainbow = imgDice.gameObject.GetComponent<RainbowImageEffect>();
            if (_rainbow == null) _rainbow = imgDice.gameObject.AddComponent<RainbowImageEffect>();
            _rainbow.enabled = true;
            return;
        }

        if (_rainbow != null) _rainbow.enabled = false;

        // 색상 규칙: Common=흰, Rare=파, Epic=금, Legendary=무지개(위에서 처리)
        switch (grade)
        {
            case DiceGrade.Common: imgDice.color = Color.white; break;
            case DiceGrade.Rare: imgDice.color = new Color(0 / 255f, 120 / 255f, 1f, 1f); break;
            case DiceGrade.Epic: imgDice.color = new Color(1f, 220 / 255f, 0, 1f); break;
            default: imgDice.color = Color.white; break;
        }
    }
}
