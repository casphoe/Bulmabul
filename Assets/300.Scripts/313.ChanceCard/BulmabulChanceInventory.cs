using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 로컬 플레이어가 보관 중인 찬스 카드 목록.
/// 
/// 보관 제한:
/// - 천사 카드: 1장
/// - 감옥 탈출 카드: 1장
/// - 여행 카드 : 1장
/// </summary>
public class BulmabulChanceInventory : MonoBehaviour
{
    public static BulmabulChanceInventory Instance { get; private set; }

    [Header("Keep Card Limit")]
    [SerializeField] private int maxKeepCardCount = 3;

    private readonly List<BulmabulChanceCardData> keptCards = new List<BulmabulChanceCardData>();

    public IReadOnlyList<BulmabulChanceCardData> KeptCards => keptCards;

    private void Awake()
    {
        Instance = this;
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

    public bool RemoveCard(BulmabulChanceCardData card)
    {
        if (card == null)
            return false;

        return keptCards.Remove(card);
    }

    public bool HasCardType(BulmabulChanceCardType type)
    {
        for (int i = 0; i < keptCards.Count; i++)
        {
            if (keptCards[i] != null && keptCards[i].cardType == type)
                return true;
        }

        return false;
    }

    public BulmabulChanceCardData GetFirstCardByType(BulmabulChanceCardType type)
    {
        for (int i = 0; i < keptCards.Count; i++)
        {
            if (keptCards[i] != null && keptCards[i].cardType == type)
                return keptCards[i];
        }

        return null;
    }

    public bool ConsumeFirstCardByType(BulmabulChanceCardType type)
    {
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