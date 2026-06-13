using UnityEngine;

/// <summary>
/// 찬스 카드 효과 실행 담당.
/// 
/// 역할:
/// 1. 보관 카드 지급
/// 2. 즉시 실행 카드 효과 적용
/// 3. 보상/세금/이동/감옥/시작 이동 처리
/// 4. 카드 사용 후 버림 카드 더미 처리
/// 
/// 중요:
/// 이동 카드는 절대 tileIndex만 직접 바꾸면 안 된다.
/// 반드시 BulmabulGameState의 카드 이동 공통 함수를 호출해야 한다.
/// 그래야 도착 칸의 구매/통행료/여행/감옥/찬스 처리가 이어진다.
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

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// 찬스 카드 뽑은 직후 실행.
    /// 보관 카드는 인벤토리에 저장하고,
    /// 즉시 실행 카드는 바로 효과를 처리한다.
    /// </summary>
    public bool HandleDrawnCard(int playerIndex, BulmabulChanceCardData card)
    {
        if (card == null)
            return false;

        /*
         * 중요:
         * Inspector에서 useType을 실수로 Immediate로 둬도
         * Angel/JailEscape/Travel 타입은 무조건 보관 카드로 처리한다.
         */
        if (IsKeepCardType(card.cardType) || card.useType == BulmabulChanceCardUseType.Keep)
        {
            KeepCard(playerIndex, card);
            return false;
        }

        bool waitsForPlayerChoice = ExecuteImmediateCard(playerIndex, card);

        /*
         * 즉시 실행 카드는 실행 후 버림 카드 더미로 보낸다.
         * 보관 카드는 실제 사용 완료 시점에 버림 처리한다.
         */
        DiscardCard(card);

        return waitsForPlayerChoice;
    }

    private bool IsKeepCardType(BulmabulChanceCardType type)
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
            state.RPC_ShowPawnFloatingText(
                playerIndex,
                $"+{card.GetName()}",
                $"+{card.GetName()}",
                1
            );

            ShowToast(
                $"{card.GetName()} 카드를 보관했습니다.",
                $"Kept {card.GetName()} card."
            );

            state.RequestCardStateRefreshForAuthority();

            if (BulmabulChanceInventory.Instance != null)
                BulmabulChanceInventory.Instance.ForceRefreshFromNetworkState();

            return;
        }

        /*
         * 이미 같은 보관 카드가 있으면 새로 뽑은 카드는 버림.
         * 플레이어당 천사/감옥탈출/여행권은 각각 1장만 보관.
         */
        ShowToast(
            $"{card.GetName()} 카드는 이미 보관 중입니다. 새 카드는 버립니다.",
            $"{card.GetName()} is already kept. The new card is discarded."
        );

        DiscardCard(card);
        state.RequestCardStateRefreshForAuthority();
    }

    private bool ExecuteImmediateCard(int playerIndex, BulmabulChanceCardData card)
    {
        if (card == null)
            return false;

        Debug.Log(
        $"[ChanceExecutor] 즉시 실행 카드 확인 " +
        $"player={playerIndex}, " +
        $"name={card.GetName()}, " +
        $"type={card.cardType}, " +
        $"useType={card.useType}, " +
        $"moneyAmount={card.moneyAmount}, " +
        $"moveStep={card.moveStep}, " +
        $"cardId={card.cardId}"
    );


        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null)
            return false;

        switch (card.cardType)
        {
            case BulmabulChanceCardType.ReceiveMoney:
                {
                    int amount = Mathf.Max(0, card.moneyAmount);

                    state.ApplyChanceMoneyForAuthority(
                        playerIndex,
                        amount,
                        true,
                        "보상 카드로",
                        "reward card"
                    );

                    ShowToast(
                        $"보상 {amount:N0}원을 받았습니다.",
                        $"Received {amount:N0} reward."
                    );

                    return false;
                }

            case BulmabulChanceCardType.PayMoney:
                {
                    int amount = Mathf.Max(0, card.moneyAmount);

                    /*
                     * PayChanceTaxForAuthority가 true를 반환하면
                     * 천사 카드 사용 여부 선택 Pending이 열린 상태다.
                     * 이 경우 턴을 끝내면 안 되므로 true를 그대로 반환한다.
                     */
                    bool waitsForAngelChoice =
                        state.PayChanceTaxForAuthority(playerIndex, amount);

                    if (waitsForAngelChoice)
                    {
                        ShowToast(
                            $"세금 {amount:N0}원 납부 전에 천사 카드 사용 여부를 선택하세요.",
                            $"Choose whether to use Angel Card before paying {amount:N0} tax."
                        );

                        return true;
                    }

                    ShowToast(
                        $"세금 {amount:N0}원을 납부했습니다.",
                        $"Paid {amount:N0} tax."
                    );

                    return false;
                }

            case BulmabulChanceCardType.MoveToStart:
                {
                    bool waitsForPlayerChoice =
                        state.MovePlayerToCellByChanceCardForAuthority(playerIndex, startCellIndex, true);

                    ShowToast(
                        "시작지점으로 이동합니다.",
                        "Move to Start."
                    );

                    return waitsForPlayerChoice;
                }

            case BulmabulChanceCardType.MoveToJail:
                {
                    bool waitsForPlayerChoice =
                        state.MovePlayerToJailByChanceCardForAuthority(playerIndex);

                    ShowToast(
                        "감옥으로 이동합니다.",
                        "Move to Jail."
                    );

                    return waitsForPlayerChoice;
                }

            case BulmabulChanceCardType.MoveForward:
                {
                    int step = Mathf.Max(0, card.moveStep);

                    bool waitsForPlayerChoice =
                        state.MovePlayerByStepByChanceCardForAuthority(playerIndex, step);

                    ShowToast(
                        $"{step}칸 앞으로 이동합니다.",
                        $"Move forward {step} spaces."
                    );

                    return waitsForPlayerChoice;
                }

            case BulmabulChanceCardType.MoveBackward:
                {
                    int step = Mathf.Max(0, card.moveStep);

                    bool waitsForPlayerChoice =
                        state.MovePlayerByStepByChanceCardForAuthority(playerIndex, -step);

                    ShowToast(
                        $"{step}칸 뒤로 이동합니다.",
                        $"Move backward {step} spaces."
                    );

                    return waitsForPlayerChoice;
                }

            case BulmabulChanceCardType.MoveToNearestEnemyLand:
                {
                    bool waitsForPlayerChoice =
                        state.MoveToNearestEnemyOwnedLandByChanceCardForAuthority(playerIndex);

                    ShowToast(
                        "가장 가까운 적 소유 땅으로 이동합니다.",
                        "Move to the nearest enemy-owned land."
                    );

                    return waitsForPlayerChoice;
                }

            case BulmabulChanceCardType.PayToAllPlayers:
                {
                    int amount = Mathf.Max(0, card.moneyAmount);

                    PayToAllPlayers(playerIndex, amount);

                    ShowToast(
                        $"모든 플레이어에게 {amount:N0}원씩 지급했습니다.",
                        $"Paid {amount:N0} to all players."
                    );

                    return false;
                }

            case BulmabulChanceCardType.ReceiveFromAllPlayers:
                {
                    int amount = Mathf.Max(0, card.moneyAmount);

                    ReceiveFromAllPlayers(playerIndex, amount);

                    ShowToast(
                        $"모든 플레이어에게 {amount:N0}원씩 받았습니다.",
                        $"Received {amount:N0} from all players."
                    );

                    return false;
                }

            default:
                {
                    Debug.LogError(
                        $"[ChanceExecutor] 처리되지 않은 찬스 카드입니다. " +
                        $"name={card.GetName()}, " +
                        $"type={card.cardType}, " +
                        $"useType={card.useType}, " +
                        $"moneyAmount={card.moneyAmount}, " +
                        $"moveStep={card.moveStep}, " +
                        $"cardId={card.cardId}"
                    );

                    ShowToast(
                        $"처리되지 않은 찬스 카드입니다: {card.GetName()}",
                        $"Unhandled chance card: {card.GetName()}"
                    );

                    return false;
                }
        }
    }

    /// <summary>
    /// 보관 중인 카드 사용.
    /// 천사 카드는 통행료 선택 팝업에서 사용.
    /// 감옥 탈출 카드는 감옥 팝업에서 사용.
    /// 여행권 카드는 카드 인벤토리에서 사용.
    /// </summary>
    public bool UseKeptCard(int playerIndex, BulmabulChanceCardData card)
    {
        if (card == null)
            return false;

        bool used = false;

        switch (card.cardType)
        {
            case BulmabulChanceCardType.AngelCard:
                used = UseAngelCard(playerIndex, card);
                break;

            case BulmabulChanceCardType.JailEscapeCard:
                used = UseJailEscapeCard(playerIndex, card);
                break;

            case BulmabulChanceCardType.MoveToTravelCard:
                used = UseMoveToTravelCard(playerIndex, card);
                break;

            default:
                Debug.LogWarning($"[ChanceExecutor] 보관 카드로 사용할 수 없는 타입: {card.cardType}");
                return false;
        }

        /*
         * 보관 카드는 실제 사용 성공 시점에 버림 카드 더미로 보낸다.
         */
        if (used)
            DiscardCard(card);

        return used;
    }

    private bool UseAngelCard(int playerIndex, BulmabulChanceCardData card)
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null || !state.LocalHasKeptChanceCard(BulmabulChanceCardType.AngelCard))
        {
            ShowToast(
                "천사 카드가 없습니다.",
                "You do not have an Angel Card."
            );

            return false;
        }

        /*
         * 천사 카드는 인벤토리에서 직접 쓰는 카드가 아니다.
         * 상대 땅 통행료 선택 팝업에서 사용한다.
         */
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

        ShowToast(
            "감옥 탈출 카드를 사용했습니다.",
            "Jail Escape Card used."
        );

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

        int beforeCash = player.cash;

        player.cash = SafeAddCash(player.cash, amount);
        state.Players.Set(playerIndex, player);

        state.RPC_ShowPawnFloatingText(
            playerIndex,
            $"+{amount:N0}",
            $"+{amount:N0}",
            1
        );

        state.RequestCardStateRefreshForAuthority();

        Debug.Log(
            $"[ChanceExecutor] 보상 카드 처리 완료. " +
            $"player={playerIndex}, before={beforeCash}, add={amount}, after={player.cash}"
        );
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

        int beforeCash = player.cash;
        int payAmount = Mathf.Min(player.cash, amount);

        player.cash -= payAmount;

        if (player.cash <= 0)
        {
            player.cash = 0;
            player.bankrupt = true;

            state.Players.Set(playerIndex, player);

            state.ReleaseAllOwnedLandsByCardForAuthority(playerIndex);

            state.RPC_ShowPawnFloatingText(
                playerIndex,
                $"-{payAmount:N0}",
                $"-{payAmount:N0}",
                2
            );

            state.RequestCardStateRefreshForAuthority();

            Debug.Log(
                $"[ChanceExecutor] 세금 카드 납부 후 파산. " +
                $"player={playerIndex}, before={beforeCash}, pay={payAmount}, after=0"
            );

            return;
        }

        state.Players.Set(playerIndex, player);

        state.RPC_ShowPawnFloatingText(
            playerIndex,
            $"-{payAmount:N0}",
            $"-{payAmount:N0}",
            2
        );

        state.RequestCardStateRefreshForAuthority();

        Debug.Log(
            $"[ChanceExecutor] 세금 카드 처리 완료. " +
            $"player={playerIndex}, before={beforeCash}, pay={payAmount}, after={player.cash}"
        );
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

            state.RPC_ShowPawnFloatingText(
                i,
                $"+{amount:N0}",
                $"+{amount:N0}",
                1
            );

            totalPaid += amount;
        }

        PayCash(playerIndex, totalPaid);

        state.RequestCardStateRefreshForAuthority();

        Debug.Log(
            $"[ChanceExecutor] 전체 지급 카드 처리 완료. " +
            $"payer={playerIndex}, each={amount}, totalPaid={totalPaid}"
        );
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

            if (other.cash <= 0)
            {
                other.cash = 0;
                other.bankrupt = true;

                state.Players.Set(i, other);
                state.ReleaseAllOwnedLandsByCardForAuthority(i);
            }
            else
            {
                state.Players.Set(i, other);
            }

            state.RPC_ShowPawnFloatingText(
                i,
                $"-{payAmount:N0}",
                $"-{payAmount:N0}",
                2
            );

            totalReceive += payAmount;
        }

        AddCash(playerIndex, totalReceive);

        state.RequestCardStateRefreshForAuthority();

        Debug.Log(
            $"[ChanceExecutor] 전체 수금 카드 처리 완료. " +
            $"receiver={playerIndex}, each={amount}, totalReceive={totalReceive}"
        );
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
            BulmabulChanceDeck.Instance.DiscardForAuthority(card);
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