using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 찬스 카드 뽑기 팝업.
/// 
/// 실제 카드를 뽑은 로컬 플레이어에게만 표시된다.
/// 확인 버튼을 누르면 로컬에서 효과를 직접 실행하지 않고,
/// StateAuthority에 카드 확인/실행 요청을 보낸다.
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

    private bool canConfirmThisPopup;
    private bool requestServerOnConfirm;

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

    public void Show(BulmabulChanceCardData card, bool canConfirm)
    {
        Show(card, canConfirm, false);
    }

    public void Show(
    BulmabulChanceCardData card,
    bool canConfirm,
    bool requestServerOnConfirm
)
    {
        canConfirmThisPopup = canConfirm;
        this.requestServerOnConfirm = requestServerOnConfirm;

        GameObject visibleRoot = root != null ? root : gameObject;
        visibleRoot.SetActive(true);

        if (imgCard != null)
        {
            imgCard.sprite = card != null ? card.cardImage : null;
            imgCard.gameObject.SetActive(card != null && card.cardImage != null);
        }

        if (txtName != null)
            txtName.text = card != null ? card.GetName() : "";

        if (txtDescription != null)
            txtDescription.text = GetCardDescriptionText(card);

        if (txtUseType != null)
            txtUseType.text = GetUseTypeText(card);

        if (btnConfirm != null)
            btnConfirm.interactable = canConfirmThisPopup;
    }

    public void Close()
    {
        canConfirmThisPopup = false;
        requestServerOnConfirm = false;

        GameObject visibleRoot = root != null ? root : gameObject;
        visibleRoot.SetActive(false);
    }

    private void OnClickConfirm()
    {
        if (!canConfirmThisPopup)
            return;

        /*
         * 중요:
         * Close()를 먼저 호출하면 requestServerOnConfirm 값이 false로 초기화된다.
         * 그래서 확인 버튼을 눌러도 서버 RPC가 호출되지 않는다.
         * 
         * 반드시 Close() 전에 값을 백업해 둔다.
         */
        bool shouldRequestServer = requestServerOnConfirm;

        Close();

        if (!shouldRequestServer)
            return;

        if (BulmabulGameState.Instance == null)
            return;

        BulmabulGameState.Instance.RequestConfirmDrawnChanceCardLocal();
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

    private string GetCardDescriptionText(BulmabulChanceCardData card)
    {
        if (card == null)
            return "";

        bool eng = IsEnglish();

        switch (card.cardType)
        {
            case BulmabulChanceCardType.ReceiveMoney:
                return eng
                    ? $"Receive {card.moneyAmount:N0} reward."
                    : $"보상 {card.moneyAmount:N0}원을 받습니다.";

            case BulmabulChanceCardType.PayMoney:
                return eng
                    ? $"Pay {card.moneyAmount:N0} tax."
                    : $"세금 {card.moneyAmount:N0}원을 납부합니다.";

            case BulmabulChanceCardType.MoveForward:
                return eng
                    ? $"Move forward {card.moveStep} spaces."
                    : $"{card.moveStep}칸 앞으로 이동합니다.";

            case BulmabulChanceCardType.MoveBackward:
                return eng
                    ? $"Move backward {card.moveStep} spaces."
                    : $"{card.moveStep}칸 뒤로 이동합니다.";

            case BulmabulChanceCardType.MoveToStart:
                return eng
                    ? "Move to Start."
                    : "시작지점으로 이동합니다.";

            case BulmabulChanceCardType.MoveToJail:
                return eng
                    ? "Move to Jail."
                    : "감옥으로 이동합니다.";

            case BulmabulChanceCardType.MoveToNearestEnemyLand:
                return eng
                    ? "Move to the nearest enemy-owned land."
                    : "가장 가까운 적 소유 땅으로 이동합니다.";

            case BulmabulChanceCardType.PayToAllPlayers:
                return eng
                    ? $"Pay {card.moneyAmount:N0} to each player."
                    : $"모든 플레이어에게 {card.moneyAmount:N0}원씩 지급합니다.";

            case BulmabulChanceCardType.ReceiveFromAllPlayers:
                return eng
                    ? $"Receive {card.moneyAmount:N0} from each player."
                    : $"모든 플레이어에게 {card.moneyAmount:N0}원씩 받습니다.";

            case BulmabulChanceCardType.AngelCard:
                return eng
                    ? "Keep this card. It can block toll payment once."
                    : "보관 카드입니다. 상대 땅 통행료를 한 번 막을 수 있습니다.";

            case BulmabulChanceCardType.JailEscapeCard:
                return eng
                    ? "Keep this card. It can escape jail."
                    : "보관 카드입니다. 감옥에서 탈출할 때 사용할 수 있습니다.";

            case BulmabulChanceCardType.MoveToTravelCard:
                return eng
                    ? "Keep this card. Use it to move to the travel cell."
                    : "보관 카드입니다. 사용하면 여행 칸으로 이동합니다.";
        }

        return card.GetDescription();
    }

    private bool IsEnglish()
    {
        var lang = LaguageManager.Instance != null
            ? LaguageManager.Instance.currentLang
            : Lauaguage.Kor;

        return lang == Lauaguage.Eng;
    }
}