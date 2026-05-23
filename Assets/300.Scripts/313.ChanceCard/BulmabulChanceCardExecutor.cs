using UnityEngine;

/// <summary>
/// 찬스 카드 효과 실행 담당.
/// 카드 효과 자체는 여기에서 처리한다.
/// 
/// 주의:
/// 네 프로젝트의 PlayerGameSlot 필드명 기준:
/// - 돈: cash
/// - 현재 칸: tileIndex
/// - 참여 여부: occupied == 1
/// </summary>
public class BulmabulChanceCardExecutor : MonoBehaviour
{
    public static BulmabulChanceCardExecutor Instance { get; private set; }

    [Header("References")]
    [SerializeField] private BulmabulBoard board;

    [Header("Cell Index")]
    [SerializeField] private int startCellIndex = 0;
    [SerializeField] private int jailCellIndex = 10;

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

        /*
         * 멀티플레이에서는 보관 카드를 로컬 Inventory 리스트에 넣지 않는다.
         * StateAuthority의 PlayerGameSlot Networked 상태에 저장해야 한다.
         */
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
        switch (card.cardType)
        {
            case BulmabulChanceCardType.ReceiveMoney:
                AddCash(playerIndex, card.moneyAmount);
                ShowToast($"{card.moneyAmount:N0}원을 받았습니다.", $"Received {card.moneyAmount:N0}.");
                break;

            case BulmabulChanceCardType.PayMoney:
                PayCash(playerIndex, card.moneyAmount);
                ShowToast($"{card.moneyAmount:N0}원을 지불했습니다.", $"Paid {card.moneyAmount:N0}.");
                break;

            case BulmabulChanceCardType.MoveToStart:
                MovePlayerToCell(playerIndex, startCellIndex);
                ShowToast("시작지점으로 이동합니다.", "Move to Start.");
                break;

            case BulmabulChanceCardType.MoveToJail:
                MovePlayerToCell(playerIndex, jailCellIndex);
                ShowToast("감옥으로 이동합니다.", "Move to Jail.");
                break;

            case BulmabulChanceCardType.MoveForward:
                MovePlayerByStep(playerIndex, card.moveStep);
                ShowToast($"{card.moveStep}칸 앞으로 이동합니다.", $"Move forward {card.moveStep} spaces.");
                break;

            case BulmabulChanceCardType.MoveBackward:
                MovePlayerByStep(playerIndex, -card.moveStep);
                ShowToast($"{card.moveStep}칸 뒤로 이동합니다.", $"Move backward {card.moveStep} spaces.");
                break;

            case BulmabulChanceCardType.MoveToNearestEnemyLand:
                {
                    BulmabulGameState state = BulmabulGameState.Instance;

                    if (state == null)
                        return false;

                    bool waitsForPlayerChoice = state.MoveToNearestEnemyOwnedLandByChanceCardForAuthority(playerIndex);

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
                break;

            case BulmabulChanceCardType.ReceiveFromAllPlayers:
                ReceiveFromAllPlayers(playerIndex, card.moneyAmount);
                ShowToast(
                    $"모든 플레이어에게 {card.moneyAmount:N0}원씩 받습니다.",
                    $"Receive {card.moneyAmount:N0} from all players."
                );
                break;

            default:
                Debug.LogWarning($"[ChanceExecutor] 즉시 실행할 수 없는 카드 타입: {card.cardType}");
                break;
        }

        return false;
    }

    /// <summary>
    /// 보관 중인 카드 사용.
    /// 천사 카드는 통행료 선택 팝업에서 사용된다.
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
                break;
        }
        /*
         * 멀티플레이 보관 카드의 실제 소비/제거는 반드시 BulmabulGameState의
         * StateAuthority RPC에서 처리한다.
         * 여기서 로컬 인벤토리를 먼저 Remove/Discard 하면 서버가 거절했을 때
         * 클라이언트 UI와 서버 상태가 어긋난다.
         */
        return used;
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

    /// <summary>
    /// 여행권 카드 사용.
    /// 
    /// 이 카드는 목적지로 바로 이동하는 카드가 아니다.
    /// 보관 중인 여행권 카드를 사용하면 내 말이 여행 칸으로 이동한다.
    /// 여행권 카드는 무료 이동권이다.
    /// 보관 중인 여행권 카드를 사용하면 내 말이 여행 칸으로 이동하고,
    /// 비용 없이 다음 자기 턴에 목적지를 선택할 수 있다.
    /// </summary>
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

        if (player.cash < 0)
            player.cash = 0;

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

    private void MovePlayerByStep(int playerIndex, int step)
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null)
            return;

        if (playerIndex < 0 || playerIndex >= BulmabulGameState.MaxPlayers)
            return;

        var player = state.Players.Get(playerIndex);

        if (player.occupied == 0 || player.bankrupt)
            return;

        int cellCount = board != null ? board.CellCount : BulmabulGameState.MaxCells;
        int nextCell = player.tileIndex + step;

        while (nextCell < 0)
            nextCell += cellCount;

        nextCell %= cellCount;

        player.tileIndex = nextCell;
        state.Players.Set(playerIndex, player);

        MovePawnVisual(playerIndex, nextCell);
    }

    private void MovePlayerToCell(int playerIndex, int targetCellIndex)
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null)
            return;

        if (playerIndex < 0 || playerIndex >= BulmabulGameState.MaxPlayers)
            return;

        int cellCount = board != null ? board.CellCount : BulmabulGameState.MaxCells;

        targetCellIndex = Mathf.Clamp(targetCellIndex, 0, cellCount - 1);

        var player = state.Players.Get(playerIndex);

        if (player.occupied == 0 || player.bankrupt)
            return;

        player.tileIndex = targetCellIndex;
        state.Players.Set(playerIndex, player);

        MovePawnVisual(playerIndex, targetCellIndex);
    }

    private void MovePawnVisual(int playerIndex, int cellIndex)
    {
        Debug.Log($"[ChanceExecutor] Player {playerIndex} move visual to cell {cellIndex}");
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