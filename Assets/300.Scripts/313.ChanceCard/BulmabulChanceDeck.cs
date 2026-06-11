using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 찬스 카드 덱.
///
/// Photon Fusion 멀티플레이 기준:
/// - 이 덱은 StateAuthority에서만 실제 생성/셔플/뽑기를 한다.
/// - 클라이언트는 덱을 직접 섞거나 뽑지 않는다.
/// - 클라이언트는 서버가 보내준 남은 카드 수로 CardDeckUI만 갱신한다.
/// </summary>
public class BulmabulChanceDeck : MonoBehaviour
{
    public static BulmabulChanceDeck Instance { get; private set; }

    [Header("자동 덱 생성 사용")]
    [SerializeField] private bool autoBuildStartCards = true;

    [Header("단일 카드 데이터")]
    [Tooltip("천사 카드. 8장 들어감.")]
    [SerializeField] private BulmabulChanceCardData angelCard;

    [Tooltip("감옥 이동 카드. 6장 들어감.")]
    [SerializeField] private BulmabulChanceCardData jailCard;

    [Tooltip("감옥 탈출 카드. 12장 들어감.")]
    [SerializeField] private BulmabulChanceCardData jailEscapeCard;

    [Tooltip("여행 카드. 20장 들어감.")]
    [SerializeField] private BulmabulChanceCardData travelCard;

    [Tooltip("시작지점 이동 카드. 21장 들어감.")]
    [SerializeField] private BulmabulChanceCardData moveToStartCard;

    [Tooltip("현재 위치 기준 앞으로 가장 가까운 적 소유 땅으로 이동하는 카드. 23장 들어감.")]
    [SerializeField] private BulmabulChanceCardData moveToNearestEnemyLandCard;

    [Header("여러 값 카드 데이터")]
    [Tooltip("세금 카드 목록. 금액이 다른 PayMoney 카드들을 넣는다.")]
    [SerializeField] private List<BulmabulChanceCardData> taxCards = new List<BulmabulChanceCardData>();

    [Tooltip("축하 카드 목록. 금액이 다른 ReceiveMoney 카드들을 넣는다.")]
    [SerializeField] private List<BulmabulChanceCardData> celebrationCards = new List<BulmabulChanceCardData>();

    [Tooltip("앞으로 이동 카드 목록. moveStep이 다른 MoveForward 카드들을 넣는다.")]
    [SerializeField] private List<BulmabulChanceCardData> moveForwardCards = new List<BulmabulChanceCardData>();

    [Tooltip("뒤로 이동 카드 목록. moveStep이 다른 MoveBackward 카드들을 넣는다.")]
    [SerializeField] private List<BulmabulChanceCardData> moveBackwardCards = new List<BulmabulChanceCardData>();

    [Header("Card Count")]
    [SerializeField] private int angelCardCount = 8;
    [SerializeField] private int jailCardCount = 6;
    [SerializeField] private int jailEscapeCardCount = 12;
    [SerializeField] private int travelCardCount = 20;
    [SerializeField] private int taxCardCount = 90;
    [SerializeField] private int celebrationCardCount = 60;
    [SerializeField] private int moveForwardCardCount = 30;
    [SerializeField] private int moveBackwardCardCount = 30;
    [SerializeField] private int moveToStartCardCount = 21;
    [SerializeField] private int moveToNearestEnemyLandCardCount = 23;

    [Header("Card List")]
    [Tooltip("자동 생성된 카드 목록. StateAuthority가 덱 초기화할 때 사용.")]
    [SerializeField] private List<BulmabulChanceCardData> startCards = new List<BulmabulChanceCardData>();

    [Header("Deck UI")]
    [Tooltip("카드 더미 UI. 비워두면 씬에서 자동으로 찾음.")]
    [SerializeField] private BulmabulCardDeckUI cardDeckUI;

    [Header("Debug")]
    [SerializeField] private bool logDeckState = true;

    private readonly List<BulmabulChanceCardData> drawPile = new List<BulmabulChanceCardData>();
    private readonly List<BulmabulChanceCardData> discardPile = new List<BulmabulChanceCardData>();

    public int StartCardCount => startCards != null ? startCards.Count : 0;
    public int DrawPileCount => drawPile.Count;
    public int DiscardPileCount => discardPile.Count;
    public int TotalRemainCount => drawPile.Count + discardPile.Count;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (cardDeckUI == null)
            cardDeckUI = FindFirstObjectByType<BulmabulCardDeckUI>();

        /*
         * 중요:
         * Photon Fusion 멀티플레이에서는 여기서 ResetDeck 하면 안 된다.
         * 클라이언트마다 Awake가 실행되면 각자 다른 덱을 만들 수 있다.
         *
         * 덱 초기화는 BulmabulGameState의 StateAuthority에서만 호출한다.
         */
        SetCardCountFromServer(0);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        angelCardCount = Mathf.Max(0, angelCardCount);
        jailCardCount = Mathf.Max(0, jailCardCount);
        jailEscapeCardCount = Mathf.Max(0, jailEscapeCardCount);
        travelCardCount = Mathf.Max(0, travelCardCount);
        taxCardCount = Mathf.Max(0, taxCardCount);
        celebrationCardCount = Mathf.Max(0, celebrationCardCount);
        moveForwardCardCount = Mathf.Max(0, moveForwardCardCount);
        moveBackwardCardCount = Mathf.Max(0, moveBackwardCardCount);
        moveToStartCardCount = Mathf.Max(0, moveToStartCardCount);
        moveToNearestEnemyLandCardCount = Mathf.Max(0, moveToNearestEnemyLandCardCount);

        if (autoBuildStartCards)
            BuildStartCards();
    }
#endif

    /// <summary>
    /// StateAuthority 전용 덱 초기화.
    /// 게임 시작 시 서버/호스트에서만 호출한다.
    /// </summary>
    public int ResetDeckForAuthority(int seed)
    {
        if (autoBuildStartCards)
            BuildStartCards();

        drawPile.Clear();
        discardPile.Clear();

        if (startCards != null)
        {
            for (int i = 0; i < startCards.Count; i++)
            {
                if (startCards[i] != null)
                    drawPile.Add(startCards[i]);
            }
        }

        ShuffleWithSeed(drawPile, seed);
        RefreshDeckUI();

        if (logDeckState)
        {
            Debug.Log(
                $"[ChanceDeck] StateAuthority 덱 초기화 완료. " +
                $"seed={seed}, startCards={StartCardCount}, drawPile={drawPile.Count}"
            );
        }

        return drawPile.Count;
    }

    /// <summary>
    /// StateAuthority 전용 카드 뽑기.
    /// 찬스칸 도착 시 서버/호스트에서만 호출한다.
    /// </summary>
    public BulmabulChanceCardData DrawTopCardForAuthority()
    {
        if (drawPile.Count <= 0)
            RefillFromDiscardForAuthority();

        if (drawPile.Count <= 0)
        {
            Debug.LogWarning("[ChanceDeck] 뽑을 찬스 카드가 없습니다.");
            RefreshDeckUI();
            return null;
        }

        BulmabulChanceCardData card = drawPile[0];
        drawPile.RemoveAt(0);

        RefreshDeckUI();

        if (logDeckState)
        {
            string cardName = card != null ? card.GetName() : "NULL";
            Debug.Log($"[ChanceDeck] StateAuthority 카드 뽑기: {cardName}, 남은 카드={drawPile.Count}");
        }

        return card;
    }

    /// <summary>
    /// StateAuthority 전용 버린 카드 추가.
    /// 즉시 실행 카드 또는 보관 카드 사용 완료 후 호출한다.
    /// </summary>
    public void DiscardForAuthority(BulmabulChanceCardData card)
    {
        if (card == null)
            return;

        discardPile.Add(card);

        if (logDeckState)
            Debug.Log($"[ChanceDeck] DiscardForAuthority: {card.GetName()}, discardPile={discardPile.Count}");
    }

    /// <summary>
    /// 기존 코드 호환용.
    /// 멀티플레이에서는 StateAuthority에서만 호출되도록 해야 한다.
    /// </summary>
    public void Discard(BulmabulChanceCardData card)
    {
        DiscardForAuthority(card);
    }

    /// <summary>
    /// 서버에서 받은 남은 카드 수로 클라이언트 UI만 갱신한다.
    /// 클라이언트에서는 실제 drawPile을 건드리지 않는다.
    /// </summary>
    public void SetCardCountFromServer(int count)
    {
        if (cardDeckUI == null)
            cardDeckUI = FindFirstObjectByType<BulmabulCardDeckUI>();

        if (cardDeckUI != null)
            cardDeckUI.SetCardCount(count);
    }

    private void RefillFromDiscardForAuthority()
    {
        if (discardPile.Count <= 0)
            return;

        drawPile.AddRange(discardPile);
        discardPile.Clear();

        int seed = System.Guid.NewGuid().GetHashCode();
        ShuffleWithSeed(drawPile, seed);

        RefreshDeckUI();

        if (logDeckState)
            Debug.Log($"[ChanceDeck] StateAuthority discardPile 재셔플 완료. seed={seed}, drawPile={drawPile.Count}");
    }

    /// <summary>
    /// 카드 목록 자동 생성.
    /// </summary>
    private void BuildStartCards()
    {
        if (startCards == null)
            startCards = new List<BulmabulChanceCardData>();

        startCards.Clear();

        AddSingleCard(angelCard, angelCardCount, "천사 카드");
        AddSingleCard(jailCard, jailCardCount, "감옥 카드");
        AddSingleCard(jailEscapeCard, jailEscapeCardCount, "감옥 탈출 카드");
        AddSingleCard(travelCard, travelCardCount, "여행 카드");
        AddListCards(taxCards, taxCardCount, "세금 카드");
        AddListCards(celebrationCards, celebrationCardCount, "축하 카드");
        AddListCards(moveForwardCards, moveForwardCardCount, "앞으로 이동 카드");
        AddListCards(moveBackwardCards, moveBackwardCardCount, "뒤로 이동 카드");
        AddSingleCard(moveToStartCard, moveToStartCardCount, "시작지점 이동 카드");
        AddSingleCard(moveToNearestEnemyLandCard, moveToNearestEnemyLandCardCount, "적 소유 땅 이동 카드");

        if (logDeckState)
        {
            Debug.Log(
                $"[ChanceDeck] 자동 덱 생성 완료. " +
                $"천사={angelCardCount}, 감옥={jailCardCount}, 감옥탈출={jailEscapeCardCount}, " +
                $"여행={travelCardCount}, 세금={taxCardCount}, 축하={celebrationCardCount}, " +
                $"앞으로={moveForwardCardCount}, 뒤로={moveBackwardCardCount}, 시작={moveToStartCardCount}, " +
                $"적땅이동={moveToNearestEnemyLandCardCount}, 합계={startCards.Count}"
            );
        }

        if (startCards.Count != 300)
            Debug.LogWarning($"[ChanceDeck] 현재 자동 생성 카드 수가 300장이 아닙니다. 현재={startCards.Count}");
    }

    private void AddSingleCard(BulmabulChanceCardData card, int count, string label)
    {
        if (count <= 0)
            return;

        if (card == null)
        {
            Debug.LogWarning($"[ChanceDeck] {label} 데이터가 연결되지 않았습니다. {count}장을 추가하지 못했습니다.");
            return;
        }

        for (int i = 0; i < count; i++)
            startCards.Add(card);
    }

    private void AddListCards(List<BulmabulChanceCardData> cards, int count, string label)
    {
        if (count <= 0)
            return;

        if (cards == null || cards.Count <= 0)
        {
            Debug.LogWarning($"[ChanceDeck] {label} 목록이 비어 있습니다. {count}장을 추가하지 못했습니다.");
            return;
        }

        int added = 0;
        int safety = 0;

        while (added < count && safety < count * 20)
        {
            safety++;

            BulmabulChanceCardData card = cards[added % cards.Count];

            if (card == null)
                continue;

            startCards.Add(card);
            added++;
        }

        if (added < count)
            Debug.LogWarning($"[ChanceDeck] {label} {count}장 중 {added}장만 추가되었습니다.");
    }

    private void ShuffleWithSeed(List<BulmabulChanceCardData> list, int seed)
    {
        if (list == null || list.Count <= 1)
            return;

        System.Random random = new System.Random(seed);

        for (int i = list.Count - 1; i > 0; i--)
        {
            int randomIndex = random.Next(0, i + 1);

            BulmabulChanceCardData temp = list[i];
            list[i] = list[randomIndex];
            list[randomIndex] = temp;
        }
    }

    private void RefreshDeckUI()
    {
        if (cardDeckUI == null)
            cardDeckUI = FindFirstObjectByType<BulmabulCardDeckUI>();

        if (cardDeckUI == null)
            return;

        cardDeckUI.SetCardCount(drawPile.Count);
    }

    public BulmabulChanceCardData FindCardById(string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
            return null;

        if (startCards != null)
        {
            for (int i = 0; i < startCards.Count; i++)
            {
                BulmabulChanceCardData card = startCards[i];

                if (card == null)
                    continue;

                if (card.cardId == cardId)
                    return card;
            }
        }

        if (drawPile != null)
        {
            for (int i = 0; i < drawPile.Count; i++)
            {
                BulmabulChanceCardData card = drawPile[i];

                if (card == null)
                    continue;

                if (card.cardId == cardId)
                    return card;
            }
        }

        if (discardPile != null)
        {
            for (int i = 0; i < discardPile.Count; i++)
            {
                BulmabulChanceCardData card = discardPile[i];

                if (card == null)
                    continue;

                if (card.cardId == cardId)
                    return card;
            }
        }

        return null;
    }

    public BulmabulChanceCardData FindFirstCardByType(BulmabulChanceCardType type)
    {
        if (startCards != null)
        {
            for (int i = 0; i < startCards.Count; i++)
            {
                BulmabulChanceCardData card = startCards[i];

                if (card != null && card.cardType == type)
                    return card;
            }
        }

        if (drawPile != null)
        {
            for (int i = 0; i < drawPile.Count; i++)
            {
                BulmabulChanceCardData card = drawPile[i];

                if (card != null && card.cardType == type)
                    return card;
            }
        }

        if (discardPile != null)
        {
            for (int i = 0; i < discardPile.Count; i++)
            {
                BulmabulChanceCardData card = discardPile[i];

                if (card != null && card.cardType == type)
                    return card;
            }
        }

        return null;
    }

#if UNITY_EDITOR
    [ContextMenu("테스트 - 자동 덱 목록 만들기")]
    private void ContextBuildStartCards()
    {
        BuildStartCards();
    }

    [ContextMenu("테스트 - Authority 덱 리셋")]
    private void TestResetDeckForAuthority()
    {
        int seed = System.Guid.NewGuid().GetHashCode();
        ResetDeckForAuthority(seed);
    }

    [ContextMenu("테스트 - Authority 카드 1장 뽑기")]
    private void TestDrawOneForAuthority()
    {
        DrawTopCardForAuthority();
    }
#endif
}