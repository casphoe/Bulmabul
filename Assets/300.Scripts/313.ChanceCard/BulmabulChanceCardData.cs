using UnityEngine;

/// <summary>
/// 찬스 카드 하나의 데이터.
/// ScriptableObject로 만들어두면 카드 추가/수정이 편하다.
/// </summary>
[CreateAssetMenu(
    fileName = "ChanceCard_",
    menuName = "Bulmabul/Chance Card"
)]
public class BulmabulChanceCardData : ScriptableObject
{
    [Header("Basic")]
    public string cardId;

    [TextArea]
    public string cardNameKor;

    [TextArea]
    public string cardNameEng;

    [TextArea]
    public string descriptionKor;

    [TextArea]
    public string descriptionEng;

    [Header("Type")]
    public BulmabulChanceCardType cardType;
    public BulmabulChanceCardUseType useType;

    [Header("Value")]
    public int moneyAmount;
    public int moveStep;
    public int targetCellIndex = -1;

    [Header("Visual")]
    public Sprite cardImage;

    public string GetName()
    {
        bool eng = IsEnglish();
        return eng ? cardNameEng : cardNameKor;
    }

    public string GetDescription()
    {
        bool eng = IsEnglish();
        return eng ? descriptionEng : descriptionKor;
    }

    private bool IsEnglish()
    {
        var lang = (LaguageManager.Instance != null)
            ? LaguageManager.Instance.currentLang
            : Lauaguage.Kor;

        return lang == Lauaguage.Eng;
    }
}