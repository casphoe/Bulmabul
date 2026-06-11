using UnityEngine;

/// <summary>
/// 찬스 카드 팝업 표시 전용.
/// 
/// 주의:
/// 이 스크립트는 더 이상 카드를 뽑거나 효과를 실행하지 않는다.
/// Photon Fusion 멀티플레이에서는 카드 뽑기/효과 실행은
/// 반드시 BulmabulGameState의 StateAuthority에서 처리해야 한다.
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

    /// <summary>
    /// 서버에서 이미 뽑힌 카드를 화면에 보여주기만 한다.
    /// 카드 뽑기, 카드 실행은 여기서 하지 않는다.
    /// </summary>
    public void ShowDrawnCardOnly(BulmabulChanceCardData card)
    {
        if (card == null)
            return;

        if (popup != null)
            popup.Show(card);
    }
}