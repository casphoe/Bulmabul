using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 천사 카드 사용 여부를 묻는 공용 팝업.
///
/// 사용 가능한 상황:
/// 1. 상대 땅 통행료 납부 전
/// 2. 세금 칸 세금 납부 전
/// 3. 찬스 카드 세금 납부 전
///
/// 체크 버튼:
/// - 천사 카드 사용
/// - 통행료 또는 세금 면제
///
/// 취소 버튼:
/// - 천사 카드 사용 안 함
/// - 기존 통행료 또는 세금 지불
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
        {
            btnCheck.onClick.RemoveListener(OnClickUseAngelCard);
            btnCheck.onClick.AddListener(OnClickUseAngelCard);
        }

        if (btnCancel != null)
        {
            btnCancel.onClick.RemoveListener(OnClickCancelAngelCard);
            btnCancel.onClick.AddListener(OnClickCancelAngelCard);
        }

        Hide();
    }

    private void OnDestroy()
    {
        if (btnCheck != null)
            btnCheck.onClick.RemoveListener(OnClickUseAngelCard);

        if (btnCancel != null)
            btnCancel.onClick.RemoveListener(OnClickCancelAngelCard);
    }

    private void Update()
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null || !state.IsSpawnReady)
        {
            Hide();
            return;
        }

        bool showToll = state.ShouldShowAngelCardTollPopupForLocalPlayer();
        bool showTax = state.ShouldShowAngelCardTaxPopupForLocalPlayer();

        if (!showToll && !showTax)
        {
            Hide();
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

        GameObject visibleRoot = root != null ? root : gameObject;
        visibleRoot.SetActive(true);

        RefreshText(state);
    }

    private void Hide()
    {
        _visible = false;

        GameObject visibleRoot = root != null ? root : gameObject;
        visibleRoot.SetActive(false);
    }

    private void RefreshText(BulmabulGameState state)
    {
        bool eng = IsEnglish();

        bool isTaxChoice =
            state != null &&
            state.ShouldShowAngelCardTaxPopupForLocalPlayer();

        if (txtTitle != null)
            txtTitle.text = eng ? "Angel Card" : "천사 카드";

        if (txtMessage != null)
        {
            if (state == null)
            {
                txtMessage.text = "";
            }
            else if (isTaxChoice)
            {
                txtMessage.text = state.GetPendingAngelCardTaxInfoText();
            }
            else
            {
                txtMessage.text = state.GetPendingAngelCardTollInfoText();
            }
        }

        if (txtCheckButton != null)
            txtCheckButton.text = eng ? "Use" : "사용";

        if (txtCancelButton != null)
        {
            if (isTaxChoice)
                txtCancelButton.text = eng ? "Pay Tax" : "세금 납부";
            else
                txtCancelButton.text = eng ? "Pay Toll" : "통행료 지불";
        }
    }

    private void OnClickUseAngelCard()
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null || !state.IsSpawnReady)
            return;

        bool isTaxChoice = state.ShouldShowAngelCardTaxPopupForLocalPlayer();
        bool isTollChoice = state.ShouldShowAngelCardTollPopupForLocalPlayer();

        if (!isTaxChoice && !isTollChoice)
        {
            Hide();
            return;
        }

        if (!state.LocalHasKeptChanceCard(BulmabulChanceCardType.AngelCard))
        {
            if (isTaxChoice)
                state.RequestResolveAngelCardTaxLocal(false);
            else
                state.RequestResolveAngelCardTollLocal(false);

            Hide();
            return;
        }

        if (ToastMessageManager.instance != null)
        {
            if (isTaxChoice)
            {
                ToastMessageManager.instance.ShowToast(
                    "천사 카드를 사용했습니다. 세금을 내지 않습니다.",
                    "Angel Card used. Tax has been blocked."
                );
            }
            else
            {
                ToastMessageManager.instance.ShowToast(
                    "천사 카드를 사용했습니다. 통행료를 내지 않습니다.",
                    "Angel Card used. Toll has been blocked."
                );
            }
        }

        /*
         * 실제 천사 카드 소비는 StateAuthority의 RPC에서만 처리한다.
         */
        if (isTaxChoice)
            state.RequestResolveAngelCardTaxLocal(true);
        else
            state.RequestResolveAngelCardTollLocal(true);

        Hide();
    }

    private void OnClickCancelAngelCard()
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null || !state.IsSpawnReady)
            return;

        bool isTaxChoice = state.ShouldShowAngelCardTaxPopupForLocalPlayer();
        bool isTollChoice = state.ShouldShowAngelCardTollPopupForLocalPlayer();

        if (isTaxChoice)
        {
            state.RequestResolveAngelCardTaxLocal(false);
            Hide();
            return;
        }

        if (isTollChoice)
        {
            state.RequestResolveAngelCardTollLocal(false);
            Hide();
            return;
        }

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