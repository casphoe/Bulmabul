using UnityEngine;

/// <summary>
/// 찬스 카드 효과 실행 담당.
/// 카드 효과 자체는 여기에서 처리한다.
/// 
/// 중요:
/// 이동 카드에서는 절대 여기서 직접 tileIndex만 바꾸면 안 된다.
/// 반드시 BulmabulGameState의 카드 이동 공통 함수를 호출해야 한다.
/// 그래야 감옥 / 상대 땅 / 빈 땅 / 내 땅 / 여행칸 / 시작칸 / 보너스칸 / 세금칸 처리가 이어진다.
/// </summary>
public class BulmabulChanceCardExecutor : MonoBehaviour
{
    public static BulmabulChanceCardExecutor Instance { get; private set; }

    [Header("References")]
    [SerializeField] private BulmabulBoard board;

    [Header("Cell Index")]
    [SerializeField] private int startCellIndex = 0;

    private void Awake()
    {
        Instance = this;

        if (board == null)
            board = FindFirstObjectByType<BulmabulBoard>();
    }

    /// <summary>
    /// 찬스 카드 뽑은 직후 실행.
    /// 즉시 실행 카드는 바로 실행하고,
    /// 보관 카드는 인벤토리에 저장한다.
    /// </summary>
    public bool HandleDrawnCard(int playerIndex, BulmabulChanceCardData card)
    {
        if (card == null)
            return false;

        if (card.useType == BulmabulChanceCardUseType.Keep)
        {
            KeepCard(playerIndex, card);
            return false;
        }

        bool waitsForPlayerChoice = ExecuteImmediateCard(playerIndex, card);

        if (BulmabulChanceDeck.Instance != null)
            BulmabulChanceDeck.Instance.Discard(card);

        return waitsForPlayerChoice;
    }

    private void KeepCard(int playerIndex, BulmabulChanceCardData card)
    {
        if (card == null)
            return;

        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null)
        {
            ShowToast("게임 상태를 찾을 수 없습니다.", "Game state is missing.");
            DiscardCard(card);
            return;
        }

        bool kept = state.TryGiveKeptChanceCardForAuthority(playerIndex, card);

        if (kept)
        {
            ShowToast(
                $"{card.GetName()} 카드를 보관했습니다.",
                $"Kept {card.GetName()} card."
            );
        }
        else
        {
            ShowToast(
                $"{card.GetName()} 카드는 이미 보관 중이거나 보관할 수 없습니다. 새 카드는 버립니다.",
                $"{card.GetName()} is already kept or cannot be kept. The new card is discarded."
            );

            DiscardCard(card);
        }
    }

    private bool ExecuteImmediateCard(int playerIndex, BulmabulChanceCardData card)
    {
        if (card == null)
            return false;

        BulmabulGameState state = BulmabulGameState.Instance;

        switch (card.cardType)
        {
            case BulmabulChanceCardType.ReceiveMoney:
                AddCash(playerIndex, card.moneyAmount);
                ShowToast($"{card.moneyAmount:N0}원을 받았습니다.", $"Received {card.moneyAmount:N0}.");
                return false;

            case BulmabulChanceCardType.PayMoney:
                PayCash(playerIndex, card.moneyAmount);
                ShowToast($"{card.moneyAmount:N0}원을 지불했습니다.", $"Paid {card.moneyAmount:N0}.");
                return false;

            case BulmabulChanceCardType.MoveToStart:
                {
                    if (state == null)
                        return false;

                    bool waitsForPlayerChoice =
                        state.MovePlayerToCellByChanceCardForAuthority(playerIndex, startCellIndex, true);

                    ShowToast("시작지점으로 이동합니다.", "Move to Start.");
                    return waitsForPlayerChoice;
                }

            case BulmabulChanceCardType.MoveToJail:
                {
                    if (state == null)
                        return false;

                    bool waitsForPlayerChoice =
                        state.MovePlayerToJailByChanceCardForAuthority(playerIndex);

                    ShowToast("감옥으로 이동합니다.", "Move to Jail.");
                    return waitsForPlayerChoice;
                }

            case BulmabulChanceCardType.MoveForward:
                {
                    if (state == null)
                        return false;

                    int step = Mathf.Max(0, card.moveStep);

                    bool waitsForPlayerChoice =
                        state.MovePlayerByStepByChanceCardForAuthority(playerIndex, step);

                    ShowToast($"{step}칸 앞으로 이동합니다.", $"Move forward {step} spaces.");
                    return waitsForPlayerChoice;
                }

            case BulmabulChanceCardType.MoveBackward:
                {
                    if (state == null)
                        return false;

                    int step = Mathf.Max(0, card.moveStep);

                    bool waitsForPlayerChoice =
                        state.MovePlayerByStepByChanceCardForAuthority(playerIndex, -step);

                    ShowToast($"{step}칸 뒤로 이동합니다.", $"Move backward {step} spaces.");
                    return waitsForPlayerChoice;
                }

            case BulmabulChanceCardType.MoveToNearestEnemyLand:
                {
                    if (state == null)
                        return false;

                    bool waitsForPlayerChoice =
                        state.MoveToNearestEnemyOwnedLandByChanceCardForAuthority(playerIndex);

                    ShowToast(
                        "가장 가까운 적 소유 땅으로 이동합니다.",
                        "Move to the nearest enemy-owned land."
                    );

                    return waitsForPlayerChoice;
                }

            case BulmabulChanceCardType.PayToAllPlayers:
                PayToAllPlayers(playerIndex, card.moneyAmount);
                ShowToast(
                    $"모든 플레이어에게 {card.moneyAmount:N0}원씩 지급합니다.",
                    $"Pay {card.moneyAmount:N0} to all players."
                );
                return false;

            case BulmabulChanceCardType.ReceiveFromAllPlayers:
                ReceiveFromAllPlayers(playerIndex, card.moneyAmount);
                ShowToast(
                    $"모든 플레이어에게 {card.moneyAmount:N0}원씩 받습니다.",
                    $"Receive {card.moneyAmount:N0} from all players."
                );
                return false;

            default:
                Debug.LogWarning($"[ChanceExecutor] 즉시 실행할 수 없는 카드 타입: {card.cardType}");
                return false;
        }
    }

    /// <summary>
    /// 보관 중인 카드 사용.
    /// 천사 카드는 통행료 선택 팝업에서 사용된다.
    /// 감옥 탈출 카드는 감옥 팝업에서 사용된다.
    /// 여행권 카드는 여행 칸 이동용으로 사용된다.
    /// </summary>
    public bool UseKeptCard(int playerIndex, BulmabulChanceCardData card)
    {
        if (card == null)
            return false;

        switch (card.cardType)
        {
            case BulmabulChanceCardType.AngelCard:
                return UseAngelCard(playerIndex, card);

            case BulmabulChanceCardType.JailEscapeCard:
                return UseJailEscapeCard(playerIndex, card);

            case BulmabulChanceCardType.MoveToTravelCard:
                return UseMoveToTravelCard(playerIndex, card);

            default:
                Debug.LogWarning($"[ChanceExecutor] 보관 카드로 사용할 수 없는 타입: {card.cardType}");
                return false;
        }
    }

    private bool UseAngelCard(int playerIndex, BulmabulChanceCardData card)
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null || !state.LocalHasKeptChanceCard(BulmabulChanceCardType.AngelCard))
        {
            ShowToast("천사 카드가 없습니다.", "You do not have an Angel Card.");
            return false;
        }

        ShowToast(
            "천사 카드는 상대 땅 통행료 선택 팝업에서 사용할 수 있습니다.",
            "Use the Angel Card from the toll popup."
        );

        return false;
    }

    private bool UseJailEscapeCard(int playerIndex, BulmabulChanceCardData card)
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null)
            return false;

        bool requested = state.RequestUseJailEscapeCardLocal();

        if (!requested)
        {
            ShowToast(
                "지금은 감옥 탈출 카드를 사용할 수 없습니다.",
                "You cannot use the Jail Escape Card now."
            );

            return false;
        }

        ShowToast("감옥 탈출 카드를 사용했습니다.", "Jail Escape Card used.");
        return true;
    }

    private bool UseMoveToTravelCard(int playerIndex, BulmabulChanceCardData card)
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null)
            return false;

        bool requested = state.RequestUseTravelTicketCardLocal();

        if (!requested)
        {
            ShowToast(
                "지금은 여행권 카드를 사용할 수 없습니다.",
                "You cannot use the Travel Ticket now."
            );

            return false;
        }

        ShowToast(
            "여행권 카드를 사용했습니다. 여행 칸으로 이동합니다.",
            "Travel Ticket used. Move to the Travel cell."
        );

        return true;
    }

    private void AddCash(int playerIndex, int amount)
    {
        if (amount <= 0)
            return;

        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null)
            return;

        if (playerIndex < 0 || playerIndex >= BulmabulGameState.MaxPlayers)
            return;

        var player = state.Players.Get(playerIndex);

        if (player.occupied == 0 || player.bankrupt)
            return;

        player.cash = SafeAddCash(player.cash, amount);
        state.Players.Set(playerIndex, player);
    }

    private void PayCash(int playerIndex, int amount)
    {
        if (amount <= 0)
            return;

        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null)
            return;

        if (playerIndex < 0 || playerIndex >= BulmabulGameState.MaxPlayers)
            return;

        var player = state.Players.Get(playerIndex);

        if (player.occupied == 0 || player.bankrupt)
            return;

        player.cash -= amount;

        if (player.cash <= 0)
        {
            player.cash = 0;
            player.bankrupt = true;

            state.Players.Set(playerIndex, player);

            /*
             * ReleaseAllOwnedLands가 private이면 여기서 직접 호출 못 한다.
             * 이 경우 BulmabulGameState에 public 래퍼 함수를 하나 만들어야 한다.
             */
            state.ReleaseAllOwnedLandsByCardForAuthority(playerIndex);

            Debug.Log($"[ChanceExecutor] {playerIndex}번 플레이어가 카드 비용 {amount:N0} 지불 후 파산했습니다.");
            return;
        }

        state.Players.Set(playerIndex, player);
    }

    private void PayToAllPlayers(int playerIndex, int amount)
    {
        if (amount <= 0)
            return;

        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null)
            return;

        int totalPaid = 0;

        for (int i = 0; i < BulmabulGameState.MaxPlayers; i++)
        {
            if (i == playerIndex)
                continue;

            var other = state.Players.Get(i);

            if (other.occupied == 0 || other.bankrupt)
                continue;

            other.cash = SafeAddCash(other.cash, amount);
            state.Players.Set(i, other);

            totalPaid += amount;
        }

        PayCash(playerIndex, totalPaid);
    }

    private void ReceiveFromAllPlayers(int playerIndex, int amount)
    {
        if (amount <= 0)
            return;

        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null)
            return;

        int totalReceive = 0;

        for (int i = 0; i < BulmabulGameState.MaxPlayers; i++)
        {
            if (i == playerIndex)
                continue;

            var other = state.Players.Get(i);

            if (other.occupied == 0 || other.bankrupt)
                continue;

            int payAmount = Mathf.Min(other.cash, amount);

            other.cash -= payAmount;
            totalReceive += payAmount;

            state.Players.Set(i, other);
        }

        AddCash(playerIndex, totalReceive);
    }

    private int SafeAddCash(int currentCash, int addAmount)
    {
        long result = (long)currentCash + addAmount;

        if (result > int.MaxValue)
            return int.MaxValue;

        if (result < int.MinValue)
            return int.MinValue;

        return (int)result;
    }

    private void DiscardCard(BulmabulChanceCardData card)
    {
        if (card == null)
            return;

        if (BulmabulChanceDeck.Instance != null)
            BulmabulChanceDeck.Instance.Discard(card);
    }

    private void ShowToast(string kor, string eng)
    {
        if (ToastMessageManager.instance != null)
        {
            ToastMessageManager.instance.ShowToast(kor, eng);
            return;
        }

        Debug.Log(kor);
    }
}