using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 찬스 카드 덱.
/// 맨 위 카드에서 하나씩 뽑고, 덱이 비면 사용된 카드 더미를 섞어서 다시 사용한다.
/// </summary>
public class BulmabulChanceDeck : MonoBehaviour
{
    public static BulmabulChanceDeck Instance { get; private set; }

    [Header("Card List")]
    [SerializeField] private List<BulmabulChanceCardData> startCards = new List<BulmabulChanceCardData>();

    private readonly List<BulmabulChanceCardData> drawPile = new List<BulmabulChanceCardData>();
    private readonly List<BulmabulChanceCardData> discardPile = new List<BulmabulChanceCardData>();

    private void Awake()
    {
        Instance = this;
        ResetDeck();
    }

    /// <summary>
    /// 덱 초기화.
    /// 게임 시작 시 카드 목록을 복사해서 섞는다.
    /// </summary>
    public void ResetDeck()
    {
        drawPile.Clear();
        discardPile.Clear();

        for (int i = 0; i < startCards.Count; i++)
        {
            if (startCards[i] != null)
                drawPile.Add(startCards[i]);
        }

        Shuffle(drawPile);
    }

    /// <summary>
    /// 맨 위 카드 한 장 뽑기.
    /// </summary>
    public BulmabulChanceCardData DrawTopCard()
    {
        if (drawPile.Count <= 0)
            RefillFromDiscard();

        if (drawPile.Count <= 0)
        {
            Debug.LogWarning("[ChanceDeck] 뽑을 찬스 카드가 없습니다.");
            return null;
        }

        BulmabulChanceCardData card = drawPile[0];
        drawPile.RemoveAt(0);

        return card;
    }

    /// <summary>
    /// 즉시 실행 카드나 사용 완료된 보관 카드를 버린 카드 더미로 이동.
    /// </summary>
    public void Discard(BulmabulChanceCardData card)
    {
        if (card == null)
            return;

        discardPile.Add(card);
    }

    private void RefillFromDiscard()
    {
        if (discardPile.Count <= 0)
            return;

        drawPile.AddRange(discardPile);
        discardPile.Clear();

        Shuffle(drawPile);
    }

    private void Shuffle(List<BulmabulChanceCardData> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int randomIndex = Random.Range(i, list.Count);
            (list[i], list[randomIndex]) = (list[randomIndex], list[i]);
        }
    }
}