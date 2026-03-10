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

        DiceGradeColorUtil.Apply(imgDice, d.grade, ref _rainbow);
    }
}
