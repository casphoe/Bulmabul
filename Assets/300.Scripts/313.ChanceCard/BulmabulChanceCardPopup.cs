using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 찬스 카드 뽑기 팝업.
/// 카드 이름, 설명, 이미지 표시 후 확인 버튼을 누르면 효과 실행.
/// </summary>
public class BulmabulChanceCardPopup : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject root;

    [Header("UI")]
    [SerializeField] private Image imgCard;
    [SerializeField] private TMP_Text txtName;
    [SerializeField] private TMP_Text txtDescription;
    [SerializeField] private TMP_Text txtUseType;
    [SerializeField] private Button btnConfirm;

    private Action _onConfirm;

    private void Awake()
    {
        if (root != null)
            root.SetActive(false);

        if (btnConfirm != null)
            btnConfirm.onClick.AddListener(OnClickConfirm);
    }

    public void Show(BulmabulChanceCardData card, Action onConfirm)
    {
        _onConfirm = onConfirm;

        if (root != null)
            root.SetActive(true);

        if (imgCard != null)
        {
            imgCard.sprite = card != null ? card.cardImage : null;
            imgCard.gameObject.SetActive(card != null && card.cardImage != null);
        }

        if (txtName != null)
            txtName.text = card != null ? card.GetName() : "";

        if (txtDescription != null)
            txtDescription.text = card != null ? card.GetDescription() : "";

        if (txtUseType != null)
            txtUseType.text = GetUseTypeText(card);
    }

    private string GetUseTypeText(BulmabulChanceCardData card)
    {
        if (card == null)
            return "";

        bool eng = IsEnglish();

        if (card.useType == BulmabulChanceCardUseType.Immediate)
            return eng ? "Immediate Card" : "즉시 실행 카드";

        return eng ? "Keep Card" : "보관 카드";
    }

    private void OnClickConfirm()
    {
        if (root != null)
            root.SetActive(false);

        Action callback = _onConfirm;
        _onConfirm = null;

        callback?.Invoke();
    }

    private bool IsEnglish()
    {
        var lang = (LaguageManager.Instance != null)
            ? LaguageManager.Instance.currentLang
            : Lauaguage.Kor;

        return lang == Lauaguage.Eng;
    }
}