using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 여행 칸 도착 시 비용을 지불할지 묻는 팝업.
/// 
/// Yes:
/// - 여행 비용 차감
/// - 다음 자기 턴에 목적지 선택 가능
/// - 더블이어도 추가 주사위 없이 턴 종료
/// 
/// No:
/// - 비용 차감 없음
/// - 더블이면 다시 주사위 가능
/// - 더블이 아니면 턴 종료
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