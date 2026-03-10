using UnityEngine;
using UnityEngine.UI;

public static class DiceGradeColorUtil
{
    /// <summary>
    /// DiceResultItem과 동일한 색 규칙 적용
    /// - Legendary: RainbowImageEffect ON
    /// - Common: white
    /// - Rare: (0,120,255)
    /// - Epic: (255,220,0)
    /// </summary>
    public static void Apply(Image img, DiceGrade grade, ref RainbowImageEffect rainbow)
    {
        if (img == null) return;

        if (grade == DiceGrade.Legendary)
        {
            if (rainbow == null) rainbow = img.GetComponent<RainbowImageEffect>();
            if (rainbow == null) rainbow = img.gameObject.AddComponent<RainbowImageEffect>();
            rainbow.enabled = true;
            return;
        }

        if (rainbow != null) rainbow.enabled = false;

        switch (grade)
        {
            case DiceGrade.Common:
                img.color = Color.white;
                break;

            case DiceGrade.Rare:
                img.color = new Color(0f / 255f, 120f / 255f, 1f, 1f);
                break;

            case DiceGrade.Epic:
                img.color = new Color(1f, 220f / 255f, 0f, 1f);
                break;

            default:
                img.color = Color.white;
                break;
        }
    }
}