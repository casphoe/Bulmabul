using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 상대 땅 도착 시 천사 카드를 사용할지 묻는 팝업.
/// 
/// 체크 버튼:
/// - 천사 카드 사용
/// - 통행료 면제
/// 
/// 취소 버튼:
/// - 천사 카드 사용 안 함
/// - 기존 통행료 지불
/// </summary>
public class BulmabulAngelCardUsePopup : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject root;

    [Header("Texts")]
    [SerializeField] private TMP_Text txtTitle;
    [SerializeField] private TMP_Text txtMessage;
    [SerializeField] private TMP_Text txtCheckButton;
    [SerializeField] private TMP_Text txtCancelButton;

    [Header("Buttons")]
    [SerializeField] private Button btnCheck;
    [SerializeField] private Button btnCancel;

    private bool _visible;

    private void Awake()
    {
        if (btnCheck != null)
            btnCheck.onClick.AddListener(OnClickUseAngelCard);

        if (btnCancel != null)
            btnCancel.onClick.AddListener(OnClickCancelAngelCard);

        Hide();
    }

    private void Update()
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null)
        {
            Hide();
            return;
        }

        /*
         * 중요:
         * Spawned()가 끝나기 전에는 PendingAction, Players 같은
         * Networked Property를 읽으면 Fusion 오류가 난다.
         */
        if (!state.IsSpawnReady)
        {
            Hide();
            return;
        }

        if (!state.ShouldShowAngelCardTollPopupForLocalPlayer())
        {
            Hide();
            return;
        }

        if (!state.LocalHasKeptChanceCard(BulmabulChanceCardType.AngelCard))
        {
            Hide();
            state.RequestResolveAngelCardTollLocal(false);
            return;
        }

        if (!_visible)
            Show(state);
        else
            RefreshText(state);
    }

    private void Show(BulmabulGameState state)
    {
        _visible = true;

        if (root != null)
            root.SetActive(true);

        RefreshText(state);
    }

    private void Hide()
    {
        _visible = false;

        if (root != null)
            root.SetActive(false);
    }

    private void RefreshText(BulmabulGameState state)
    {
        bool eng = IsEnglish();

        if (txtTitle != null)
            txtTitle.text = eng ? "Angel Card" : "천사 카드";

        if (txtMessage != null)
            txtMessage.text = state != null ? state.GetPendingAngelCardTollInfoText() : "";

        if (txtCheckButton != null)
            txtCheckButton.text = eng ? "Use" : "사용";

        if (txtCancelButton != null)
            txtCancelButton.text = eng ? "Pay Toll" : "통행료 지불";
    }

    private void OnClickUseAngelCard()
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null)
            return;

        if (!state.LocalHasKeptChanceCard(BulmabulChanceCardType.AngelCard))
        {
            state.RequestResolveAngelCardTollLocal(false);
            Hide();
            return;
        }

        if (ToastMessageManager.instance != null)
        {
            ToastMessageManager.instance.ShowToast(
                "천사 카드를 사용했습니다. 통행료를 내지 않습니다.",
                "Angel Card used. Toll has been blocked."
            );
        }

        /*
         * 실제 천사 카드 소비는 StateAuthority의 RPC_RequestResolveAngelCardToll에서 한다.
         */
        state.RequestResolveAngelCardTollLocal(true);
        Hide();
    }

    private void OnClickCancelAngelCard()
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null)
            return;

        state.RequestResolveAngelCardTollLocal(false);
        Hide();
    }

    private bool IsEnglish()
    {
        var lang = (LaguageManager.Instance != null)
            ? LaguageManager.Instance.currentLang
            : Lauaguage.Kor;

        return lang == Lauaguage.Eng;
    }
}