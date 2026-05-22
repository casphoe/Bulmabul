using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 여행 칸 도착 시 여행 비용을 지불할지 묻는 팝업.
/// 
/// 확인:
/// - 여행 비용 차감
/// - 다음 턴 목적지 선택권 지급
/// 
/// 취소:
/// - 비용 차감 없음
/// - 목적지 선택권 없음
/// </summary>
public class BulmabulTravelCostPopup : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject root;

    [Header("Texts")]
    [SerializeField] private TMP_Text txtTitle;
    [SerializeField] private TMP_Text txtMessage;
    [SerializeField] private TMP_Text txtOkButton;
    [SerializeField] private TMP_Text txtCancelButton;

    [Header("Buttons")]
    [SerializeField] private Button btnOk;
    [SerializeField] private Button btnCancel;

    private bool _visible;

    private void Awake()
    {
        if (btnOk != null)
            btnOk.onClick.AddListener(OnClickOk);

        if (btnCancel != null)
            btnCancel.onClick.AddListener(OnClickCancel);

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

        if (!state.ShouldShowTravelCostPopupForLocalPlayer())
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
            txtTitle.text = eng ? "Travel" : "여행";

        if (txtMessage != null)
            txtMessage.text = state != null ? state.GetPendingTravelCostInfoText() : "";

        if (txtOkButton != null)
            txtOkButton.text = eng ? "OK" : "확인";

        if (txtCancelButton != null)
            txtCancelButton.text = eng ? "Cancel" : "취소";
    }

    private void OnClickOk()
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null)
            return;

        state.RequestResolveTravelCostLocal(true);
        Hide();
    }

    private void OnClickCancel()
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null)
            return;

        state.RequestResolveTravelCostLocal(false);
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