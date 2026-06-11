using UnityEngine;

/// <summary>
/// 찬스 카드 팝업 표시 전용.
/// 
/// 카드 뽑기/카드 수 차감/카드 효과 실행은
/// BulmabulGameState의 StateAuthority에서 처리한다.
/// 
/// 이 스크립트는 실제 카드를 뽑은 로컬 플레이어에게만
/// 카드 내용을 보여주는 역할만 한다.
/// </summary>
public class BulmabulChanceCellHandler : MonoBehaviour
{
    public static BulmabulChanceCellHandler Instance { get; private set; }

    [Header("UI")]
    [SerializeField] private BulmabulChanceCardPopup popup;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void ShowDrawnCardOnly(BulmabulChanceCardData card, bool canConfirm)
    {
        ShowDrawnCardOnly(card, canConfirm, false);
    }

    public void ShowDrawnCardOnly(
        BulmabulChanceCardData card,
        bool canConfirm,
        bool requestServerOnConfirm
    )
    {
        if (card == null)
            return;

        if (popup != null)
            popup.Show(card, canConfirm, requestServerOnConfirm);
    }

    public void ShowDrawnCardOnly(BulmabulChanceCardData card)
    {
        ShowDrawnCardOnly(card, true, false);
    }

    public void HideDrawnCardOnly()
    {
        if (popup != null)
            popup.Close();
    }
}