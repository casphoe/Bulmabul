using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 감옥 탈출 선택 팝업.
/// 
/// 표시 조건:
/// - 내 턴
/// - PendingAction == JailChoice
/// - 내가 감옥 상태
/// 
/// 버튼:
/// - 주사위 굴리기: 더블이면 탈출
/// - 재화 지불 탈출: 돈 내고 탈출
/// - 감옥 탈출 카드: 카드 있으면 사용 가능
/// 5회 실패 후 자동 탈출은 BulmabulGameState 턴 시작 처리에서 처리한다.
/// </summary>
public class BulmabulJailChoicePopup : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject rootPanel;

    [Header("Texts")]
    [SerializeField] private TMP_Text txtTitle;
    [SerializeField] private TMP_Text txtInfo;
    [SerializeField] private TMP_Text txtRollDiceButton;
    [SerializeField] private TMP_Text txtPayEscapeCostButton;
    [SerializeField] private TMP_Text txtUseJailEscapeCardButton;

    [Header("Buttons")]
    [SerializeField] private Button btnRollDice;
    [SerializeField] private Button btnPayEscapeCost;
    [SerializeField] private Button btnUseJailEscapeCard;

    [Header("Game UI")]
    [SerializeField] private BulmabulGameUI gameUI;

    private void Awake()
    {
        if (rootPanel == null)
            rootPanel = gameObject;

        if (gameUI == null)
            gameUI = FindFirstObjectByType<BulmabulGameUI>();

        if (btnRollDice != null)
        {
            btnRollDice.onClick.RemoveListener(OnClickRollDice);
            btnRollDice.onClick.AddListener(OnClickRollDice);
        }

        if (btnPayEscapeCost != null)
        {
            btnPayEscapeCost.onClick.RemoveListener(OnClickPayEscapeCost);
            btnPayEscapeCost.onClick.AddListener(OnClickPayEscapeCost);
        }

        if (btnUseJailEscapeCard != null)
        {
            btnUseJailEscapeCard.onClick.RemoveListener(OnClickUseJailEscapeCard);
            btnUseJailEscapeCard.onClick.AddListener(OnClickUseJailEscapeCard);
        }

        SetVisible(false);
    }

    private void Update()
    {
        Refresh();
    }

    private void Refresh()
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null || state.Runner == null || !state.IsSpawnReady)
        {
            SetVisible(false);
            return;
        }

        bool show = state.ShouldShowJailChoicePopupForLocalPlayer();

        SetVisible(show);

        if (!show)
            return;

        if (txtInfo != null)
            txtInfo.text = state.GetPendingJailInfoText();

        if (txtRollDiceButton != null)
            txtRollDiceButton.text = GetByLanguage("주사위 굴리기", "Roll Dice");

        if (txtPayEscapeCostButton != null)
            txtPayEscapeCostButton.text = GetByLanguage("재화 지불 탈출", "Pay to Escape");

        if (txtUseJailEscapeCardButton != null)
            txtUseJailEscapeCardButton.text = GetByLanguage("감옥 탈출 카드", "Jail Escape Card");

        if (btnRollDice != null)
            btnRollDice.interactable = state.CanLocalJailRollDice();

        if (btnPayEscapeCost != null)
            btnPayEscapeCost.interactable = state.CanLocalPayJailEscapeCost();

        if (btnUseJailEscapeCard != null)
            btnUseJailEscapeCard.gameObject.SetActive(state.CanLocalUseJailEscapeCard());
    }

    private void SetVisible(bool visible)
    {
        if (rootPanel != null && rootPanel.activeSelf != visible)
            rootPanel.SetActive(visible);
    }

    private void OnClickRollDice()
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null)
            return;

        if (!state.CanLocalJailRollDice())
        {
            if (ToastMessageManager.instance != null)
            {
                ToastMessageManager.instance.ShowToast(
                    "지금은 감옥 탈출 주사위를 굴릴 수 없습니다.",
                    "You cannot roll for jail escape now."
                );
            }

            return;
        }

        SetVisible(false);

        if (gameUI == null)
            gameUI = FindFirstObjectByType<BulmabulGameUI>();

        if (gameUI != null)
        {
            gameUI.OpenDiceControlPanelForJailEscape();
        }
        else if (ToastMessageManager.instance != null)
        {
            ToastMessageManager.instance.ShowToast(
                "주사위 조작 UI를 찾을 수 없습니다.",
                "Dice control UI was not found."
            );
        }
    }

    private void OnClickPayEscapeCost()
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null)
            return;

        bool requested = state.RequestPayJailEscapeCostLocal();

        if (!requested && ToastMessageManager.instance != null)
        {
            ToastMessageManager.instance.ShowToast(
                "감옥 탈출 비용이 부족하거나 지금은 탈출할 수 없습니다.",
                "Not enough cash or cannot escape now."
            );
        }
    }

    private void OnClickUseJailEscapeCard()
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null)
            return;

        bool requested = state.RequestUseJailEscapeCardLocal();

        if (!requested && ToastMessageManager.instance != null)
        {
            ToastMessageManager.instance.ShowToast(
                "감옥 탈출 카드를 사용할 수 없습니다.",
                "You cannot use Jail Escape Card now."
            );
        }
    }

    private string GetByLanguage(string kor, string eng)
    {
        if (LaguageManager.Instance == null)
            return kor;

        return LaguageManager.Instance.currentLang == Lauaguage.Eng ? eng : kor;
    }
}