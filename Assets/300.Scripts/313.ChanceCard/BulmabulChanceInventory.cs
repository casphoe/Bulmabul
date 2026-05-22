using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 로컬 플레이어가 보관 중인 찬스 카드 목록을 UI용으로 보여주는 인벤토리.
///
/// 중요:
/// - Photon/Fusion 멀티플레이에서 실제 보유 여부는 BulmabulGameState.PlayerGameSlot의
///   hasAngelCard / hasJailEscapeCard / hasTravelCard 네트워크 값이 원본이다.
/// - 이 클래스의 keptCards 리스트는 UI 표시용 미러다.
/// - 카드 사용/소비는 StateAuthority RPC에서만 처리해야 한다.
/// </summary>
public class BulmabulChanceInventory : MonoBehaviour
{
    public static BulmabulChanceInventory Instance { get; private set; }

    [Header("Keep Card Limit")]
    [SerializeField] private int maxKeepCardCount = 3;

    [Header("UI 표시용 보관 카드 데이터")]
    [Tooltip("천사 카드 ScriptableObject를 연결하세요.")]
    [SerializeField] private BulmabulChanceCardData angelCardData;

    [Tooltip("감옥 탈출 카드 ScriptableObject를 연결하세요.")]
    [SerializeField] private BulmabulChanceCardData jailEscapeCardData;

    [Tooltip("여행 카드 ScriptableObject를 연결하세요.")]
    [SerializeField] private BulmabulChanceCardData travelCardData;

    private readonly List<BulmabulChanceCardData> keptCards = new List<BulmabulChanceCardData>();

    private int lastSyncedRevision = int.MinValue;

    public IReadOnlyList<BulmabulChanceCardData> KeptCards => keptCards;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        SyncFromNetworkStateIfNeeded();
    }

    /// <summary>
    /// 네트워크 상태 변경 시 UI 인벤토리 목록을 다시 만든다.
    /// </summary>
    public void SyncFromNetworkStateIfNeeded()
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null || state.Runner == null)
            return;

        if (lastSyncedRevision == state.Revision)
            return;

        lastSyncedRevision = state.Revision;
        RebuildFromNetworkState(state);
    }

    /// <summary>
    /// 즉시 UI 인벤토리를 네트워크 상태 기준으로 갱신한다.
    /// </summary>
    public void ForceRefreshFromNetworkState()
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null || state.Runner == null)
            return;

        lastSyncedRevision = state.Revision;
        RebuildFromNetworkState(state);
    }

    private void RebuildFromNetworkState(BulmabulGameState state)
    {
        keptCards.Clear();

        if (state.LocalHasKeptChanceCard(BulmabulChanceCardType.AngelCard) && angelCardData != null)
            keptCards.Add(angelCardData);

        if (state.LocalHasKeptChanceCard(BulmabulChanceCardType.JailEscapeCard) && jailEscapeCardData != null)
            keptCards.Add(jailEscapeCardData);

        if (state.LocalHasKeptChanceCard(BulmabulChanceCardType.MoveToTravelCard) && travelCardData != null)
            keptCards.Add(travelCardData);
    }

    public bool CanKeepCard()
    {
        return keptCards.Count < maxKeepCardCount;
    }

    public bool CanKeepCard(BulmabulChanceCardData card)
    {
        if (card == null)
            return false;

        if (card.useType != BulmabulChanceCardUseType.Keep)
            return false;

        if (!CanKeepCard())
            return false;

        if (!IsLimitedKeepCardType(card.cardType))
            return false;

        if (HasCardType(card.cardType))
            return false;

        return true;
    }

    /// <summary>
    /// 싱글플레이/테스트용 로컬 추가 함수.
    /// 멀티플레이 실제 지급은 BulmabulGameState.TryGiveKeptChanceCardForAuthority를 사용한다.
    /// </summary>
    public bool AddCard(BulmabulChanceCardData card)
    {
        if (card == null)
            return false;

        if (!CanKeepCard(card))
        {
            Debug.LogWarning($"[ChanceInventory] 보관 불가 카드: {card.cardType}");
            return false;
        }

        keptCards.Add(card);
        Debug.Log($"[ChanceInventory] 카드 보관: {card.GetName()}");

        return true;
    }

    /// <summary>
    /// UI 미러에서만 제거한다.
    /// 멀티플레이 실제 소비는 반드시 서버 RPC에서 처리한다.
    /// </summary>
    public bool RemoveCard(BulmabulChanceCardData card)
    {
        if (card == null)
            return false;

        return keptCards.Remove(card);
    }

    public bool HasCardType(BulmabulChanceCardType type)
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        if (state != null && state.Runner != null)
            return state.LocalHasKeptChanceCard(type);

        for (int i = 0; i < keptCards.Count; i++)
        {
            if (keptCards[i] != null && keptCards[i].cardType == type)
                return true;
        }

        return false;
    }

    public BulmabulChanceCardData GetFirstCardByType(BulmabulChanceCardType type)
    {
        SyncFromNetworkStateIfNeeded();

        for (int i = 0; i < keptCards.Count; i++)
        {
            if (keptCards[i] != null && keptCards[i].cardType == type)
                return keptCards[i];
        }

        return null;
    }

    /// <summary>
    /// 싱글플레이/테스트용 소비 함수.
    /// 멀티플레이에서는 서버 RPC를 통해 소비해야 한다.
    /// </summary>
    public bool ConsumeFirstCardByType(BulmabulChanceCardType type)
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        if (state != null && state.Runner != null)
        {
            Debug.LogWarning("[ChanceInventory] 멀티플레이에서는 ConsumeFirstCardByType 대신 StateAuthority RPC를 사용해야 합니다.");
            return false;
        }

        BulmabulChanceCardData card = GetFirstCardByType(type);

        if (card == null)
            return false;

        keptCards.Remove(card);

        if (BulmabulChanceDeck.Instance != null)
            BulmabulChanceDeck.Instance.Discard(card);

        return true;
    }

    private bool IsLimitedKeepCardType(BulmabulChanceCardType type)
    {
        switch (type)
        {
            case BulmabulChanceCardType.AngelCard:
            case BulmabulChanceCardType.JailEscapeCard:
            case BulmabulChanceCardType.MoveToTravelCard:
                return true;

            default:
                return false;
        }
    }
}