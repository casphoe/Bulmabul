using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 찬스 카드 뽑기 팝업.
/// 카드 이름, 설명, 이미지 표시 후 확인 버튼을 누르면
/// 로컬에서 효과를 직접 실행하지 않고 StateAuthority에 확인 요청을 보낸다.
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

    private void Awake()
    {
        if (root != null)
            root.SetActive(false);

        if (btnConfirm != null)
        {
            btnConfirm.onClick.RemoveListener(OnClickConfirm);
            btnConfirm.onClick.AddListener(OnClickConfirm);
        }
    }

    private void OnDestroy()
    {
        if (btnConfirm != null)
            btnConfirm.onClick.RemoveListener(OnClickConfirm);
    }

    /// <summary>
    /// 멀티플레이용 찬스 카드 표시.
    /// 모든 클라이언트에서 팝업은 보이지만,
    /// 확인 버튼은 실제 카드를 뽑은 플레이어만 누를 수 있다.
    /// </summary>
    public void Show(BulmabulChanceCardData card)
    {
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

        RefreshConfirmButton();
    }

    /// <summary>
    /// 기존 코드 호환용.
    /// 다른 곳에서 Show(card, callback)을 호출하고 있어도 컴파일 에러가 나지 않게 유지한다.
    /// 단, 멀티플레이에서는 callback을 직접 실행하지 않고 서버 확인 요청 방식으로 처리한다.
    /// </summary>
    public void Show(BulmabulChanceCardData card, System.Action onConfirm)
    {
        Show(card);
    }

    public void Close()
    {
        if (root != null)
            root.SetActive(false);
    }

    private void RefreshConfirmButton()
    {
        if (btnConfirm == null)
            return;

        bool canConfirm = false;

        if (BulmabulGameState.Instance != null)
            canConfirm = BulmabulGameState.Instance.CanLocalConfirmDrawnChanceCard();

        btnConfirm.interactable = canConfirm;
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
        if (BulmabulGameState.Instance == null)
            return;

        if (!BulmabulGameState.Instance.CanLocalConfirmDrawnChanceCard())
            return;

        Close();

        BulmabulGameState.Instance.RequestConfirmDrawnChanceCardLocal();
    }

    private bool IsEnglish()
    {
        var lang = LaguageManager.Instance != null
            ? LaguageManager.Instance.currentLang
            : Lauaguage.Kor;

        return lang == Lauaguage.Eng;
    }
}