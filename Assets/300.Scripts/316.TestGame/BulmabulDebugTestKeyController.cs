using Fusion;
using UnityEngine;

/// <summary>
/// 부루마불 개발 테스트 전용 키 컨트롤러.
/// 
/// 이 스크립트는 실제 게임 로직과 테스트 입력을 분리하기 위해 만든다.
/// BulmabulGameState 안에 테스트 키를 직접 넣지 않는다.
/// 
/// 사용 키:
/// - 8번: 현재 로컬 플레이어 파산 1회 누적
/// - 9번: 현재 서 있는 40칸 구간의 라인 하나를 테스트로 전부 획득
/// 
/// 9번 규칙:
/// - 현재 위치가 0~39면 0~19 라인을 먼저 처리
/// - 0~19 라인을 이미 전부 소유 중이면 20~39 라인 처리
/// - 40~79, 80~119, 120~159도 동일하게 절반씩 처리
/// - 빈 땅은 재화 소모 없이 무료 소유
/// - 적 소유 땅은 인수 비용을 내고 소유권 변경
/// - 내 땅은 건드리지 않음
/// - 같은 팀 땅은 건드리지 않음
/// - 특수 칸은 무시하고 Land 칸만 처리
/// </summary>
public class BulmabulDebugTestKeyController : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private BulmabulGameState gameState;
    [SerializeField] private BulmabulBoard board;

    [Header("Debug Key Settings")]
    [SerializeField] private bool enableDebugKeys = true;

    [Tooltip("8번 키 사용")]
    [SerializeField] private bool enableBankruptcyKey = true;

    [Tooltip("9번 키 사용")]
    [SerializeField] private bool enableLineAcquireKey = true;

    [Header("Line Monopoly Test")]
    [Tooltip("라인 몇 개 이상 독점하면 테스트 승리 처리할지")]
    [SerializeField] private int lineMonopolyWinCount = 2;

    [Tooltip("true면 같은 팀 땅은 인수하지 않는다.")]
    [SerializeField] private bool ignoreSameTeamLand = true;

    [Tooltip("true면 호텔이 있어도 테스트 인수한다. false면 기존 인수 규칙대로 호텔 땅은 인수하지 않는다.")]
    [SerializeField] private bool forceTakeOverEvenHotel = false;

    private const int MaxPlayers = BulmabulGameState.MaxPlayers;
    private const int MaxCells = BulmabulGameState.MaxCells;

    private void Awake()
    {
        if (gameState == null)
            gameState = BulmabulGameState.Instance;

        if (board == null)
            board = FindObjectOfType<BulmabulBoard>();
    }

    private void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!enableDebugKeys)
            return;

        if (gameState == null)
            gameState = BulmabulGameState.Instance;

        if (gameState == null)
            return;

        if (Runner == null)
            return;

        if (Runner.LocalPlayer == PlayerRef.None)
            return;

        if (enableBankruptcyKey && (Input.GetKeyDown(KeyCode.Alpha8) || Input.GetKeyDown(KeyCode.Keypad8)))
        {
            RPC_RequestDebugAddBankruptcy();
            return;
        }

        if (enableLineAcquireKey && (Input.GetKeyDown(KeyCode.Alpha9) || Input.GetKeyDown(KeyCode.Keypad9)))
        {
            RPC_RequestDebugAcquireCurrentLine();
            return;
        }
#endif
    }

    /// <summary>
    /// 8번 테스트 키.
    /// 현재 로컬 플레이어에게 파산을 1회 누적한다.
    /// 2회가 되면 실제 패배 처리처럼 소유 땅을 해제한다.
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestDebugAddBankruptcy(RpcInfo info = default)
    {
        if (!CanRunDebugCommand(info.Source, out int playerIndex))
            return;

        BulmabulGameState.PlayerGameSlot slot = gameState.Players.Get(playerIndex);

        slot.cash = 0;
        slot.bankruptcyCount = Mathf.Max(0, slot.bankruptcyCount) + 1;

        int bankruptcyCount = slot.bankruptcyCount;

        if (bankruptcyCount < 2)
        {
            slot.bankrupt = false;
            gameState.Players.Set(playerIndex, slot);

            gameState.LastLogMessage =
                $"{GetPlayerName(playerIndex)}님이 테스트 키 8번으로 파산 {bankruptcyCount}/2회 상태가 되었습니다.";

            BumpGameStateRevision();

            Debug.Log(
                $"[BulmabulDebugTestKeyController] Player {playerIndex} bankruptcy test count = {bankruptcyCount}/2"
            );

            return;
        }

        /*
         * 2회 파산부터 실제 패배 처리.
         */
        slot.bankrupt = true;
        slot.cash = 0;
        slot.hasTravelDestinationReady = false;
        slot.hasAngelCard = false;
        slot.hasJailEscapeCard = false;
        slot.hasTravelCard = false;
        slot.travelCost = 0;
        slot.isInJail = false;
        slot.jailTryCount = 0;
        slot.consecutiveDoubleCount = 0;

        gameState.Players.Set(playerIndex, slot);

        ReleaseAllOwnedLands(playerIndex);

        gameState.LastLogMessage =
            $"{GetPlayerName(playerIndex)}님이 테스트 키 8번으로 파산 2회가 되어 패배 처리되었습니다.";

        CheckWinnerByRemainingPlayersForDebug();

        BumpGameStateRevision();

        Debug.Log(
            $"[BulmabulDebugTestKeyController] Player {playerIndex} bankruptcy defeat by debug key 8"
        );
    }

    /// <summary>
    /// 9번 테스트 키.
    /// 현재 플레이어가 서 있는 40칸 구간에서 라인 하나를 전부 획득한다.
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestDebugAcquireCurrentLine(RpcInfo info = default)
    {
        if (!CanRunDebugCommand(info.Source, out int playerIndex))
            return;

        if (gameState.IsPaused || gameState.TurnBusy)
            return;

        /*
         * 테스트 키도 자기 턴에만 사용하게 막는다.
         * 다른 사람 턴에 로컬 키를 눌러서 땅을 먹는 문제 방지.
         */
        if (gameState.CurrentTurnIndex != playerIndex)
        {
            gameState.LastLogMessage =
                $"테스트 키 9번은 자기 턴에만 사용할 수 있습니다. 현재 턴: {gameState.CurrentTurnIndex}, 요청자: {playerIndex}";

            BumpGameStateRevision();
            return;
        }

        if (board == null)
            board = FindObjectOfType<BulmabulBoard>();

        if (board == null || board.CellCount <= 0)
            return;

        BulmabulGameState.PlayerGameSlot player = gameState.Players.Get(playerIndex);

        int currentTileIndex = Mathf.Clamp(player.tileIndex, 0, Mathf.Min(board.CellCount, MaxCells) - 1);

        GetDebugLineRangeByCurrentTile(
            currentTileIndex,
            playerIndex,
            out int lineStartIndex,
            out int lineEndIndex
        );

        int freeBuyCount = 0;
        int takeOverCount = 0;
        int skipCount = 0;
        int lackMoneyCount = 0;

        for (int cellIndex = lineStartIndex; cellIndex <= lineEndIndex; cellIndex++)
        {
            if (cellIndex < 0 || cellIndex >= board.CellCount || cellIndex >= MaxCells)
                continue;

            BulmabulCellData cell = board.GetCell(cellIndex);

            if (cell == null)
                continue;

            if (cell.cellType != BulmabulCellType.Land)
                continue;

            int ownerIndex = gameState.LandOwnerByCell.Get(cellIndex);

            /*
             * 빈 땅은 테스트 구매이므로 재화 소모 없이 소유권만 변경한다.
             */
            if (ownerIndex < 0)
            {
                gameState.LandOwnerByCell.Set(cellIndex, playerIndex);

                /*
                 * 빈 땅이므로 건물 상태는 없음으로 보정.
                 */
                gameState.LandBuildingFlagsByCell.Set(cellIndex, 0);

                freeBuyCount++;
                continue;
            }

            /*
             * 이미 내 땅이면 아무것도 하지 않는다.
             */
            if (ownerIndex == playerIndex)
            {
                skipCount++;
                continue;
            }

            /*
             * 같은 팀 땅이면 인수하지 않는다.
             */
            if (ignoreSameTeamLand && IsSameTeam(playerIndex, ownerIndex))
            {
                skipCount++;
                continue;
            }

            /*
             * 적 땅이면 인수 처리.
             * 기본은 기존 인수 규칙을 따른다.
             * forceTakeOverEvenHotel = true면 호텔이 있어도 테스트 인수한다.
             */
            int flags = gameState.LandBuildingFlagsByCell.Get(cellIndex);

            if (!forceTakeOverEvenHotel && !BulmabulLandSystem.CanTakeOver(flags))
            {
                skipCount++;
                continue;
            }

            int takeOverCost = Mathf.Max(0, BulmabulLandSystem.CalculateTakeOverCost(cell, flags));

            player = gameState.Players.Get(playerIndex);

            if (player.cash < takeOverCost)
            {
                lackMoneyCount++;
                continue;
            }

            /*
             * 중요:
             * 인수 비용은 은행으로 사라지는 돈이 아니라
             * 기존 땅 소유자에게 지급되는 돈이다.
             *
             * 기존 문제:
             * - 인수자 돈만 차감했다.
             * - 기존 소유자 cash를 증가시키지 않았다.
             */
            BulmabulGameState.PlayerGameSlot seller = gameState.Players.Get(ownerIndex);

            player.cash -= takeOverCost;
            seller.cash = SafeAddCash(seller.cash, takeOverCost);

            gameState.Players.Set(playerIndex, player);
            gameState.Players.Set(ownerIndex, seller);

            gameState.LandOwnerByCell.Set(cellIndex, playerIndex);

            /*
             * 인수는 기존 건물을 유지한다.
             * 따라서 LandBuildingFlagsByCell은 건드리지 않는다.
             */

            // 인수자에게 차감 플로팅 텍스트 표시
            gameState.RPC_ShowPawnFloatingText(
                playerIndex,
                $"-{takeOverCost:N0}",
                $"-{takeOverCost:N0}",
                2
            );

            // 기존 소유자에게 획득 플로팅 텍스트 표시
            gameState.RPC_ShowPawnFloatingText(
                ownerIndex,
                $"+{takeOverCost:N0}",
                $"+{takeOverCost:N0}",
                1
            );

            Debug.Log(
                $"[BulmabulDebugTestKeyController] 9번 인수 처리 완료: " +
                $"buyer={playerIndex}, seller={ownerIndex}, cell={cellIndex}, cost={takeOverCost:N0}, " +
                $"buyerCash={player.cash:N0}, sellerCash={seller.cash:N0}"
            );

            takeOverCount++;
        }

        CheckWinnerByLineMonopolyForDebug(playerIndex);

        if (!gameState.GameFinished)
        {
            gameState.LastLogMessage =
                $"{GetPlayerName(playerIndex)}님이 테스트 키 9번으로 {lineStartIndex}~{lineEndIndex} 라인을 획득했습니다. " +
                $"무료 구매 {freeBuyCount}개, 인수 {takeOverCount}개, 스킵 {skipCount}개, 비용 부족 {lackMoneyCount}개";
        }

        BumpGameStateRevision();

        Debug.Log(
            $"[BulmabulDebugTestKeyController] Player {playerIndex} acquire line {lineStartIndex}~{lineEndIndex}. " +
            $"Free={freeBuyCount}, TakeOver={takeOverCount}, Skip={skipCount}, Lack={lackMoneyCount}"
        );
    }

    /// <summary>
    /// 테스트 명령을 실행할 수 있는지 검사한다.
    /// </summary>
    private bool CanRunDebugCommand(PlayerRef source, out int playerIndex)
    {
        playerIndex = -1;

        if (gameState == null)
            gameState = BulmabulGameState.Instance;

        if (gameState == null)
            return false;

        if (!Object.HasStateAuthority)
            return false;

        if (gameState.GameFinished)
            return false;

        playerIndex = FindPlayerIndex(source);

        if (!IsValidAlivePlayer(playerIndex))
            return false;

        return true;
    }

    /// <summary>
    /// 현재 위치 기준으로 테스트 라인 범위를 구한다.
    /// 
    /// 예:
    /// 현재 위치가 0~39면
    /// - 0~19 라인을 아직 전부 소유하지 않았으면 0~19
    /// - 0~19를 이미 전부 소유했으면 20~39
    /// </summary>
    private void GetDebugLineRangeByCurrentTile(
        int currentTileIndex,
        int playerIndex,
        out int startIndex,
        out int endIndex
    )
    {
        int segmentStart = Mathf.Clamp((currentTileIndex / 40) * 40, 0, 120);

        int firstStart = segmentStart;
        int firstEnd = segmentStart + 19;

        int secondStart = segmentStart + 20;
        int secondEnd = segmentStart + 39;

        /*
         * 앞 라인을 아직 전부 소유하지 않았으면 앞 라인을 처리.
         * 앞 라인을 이미 전부 소유했다면 뒤 라인을 처리.
         */
        if (!IsEntireLandLineOwnedByPlayer(playerIndex, firstStart, firstEnd))
        {
            startIndex = firstStart;
            endIndex = firstEnd;
            return;
        }

        startIndex = secondStart;
        endIndex = secondEnd;
    }

    /// <summary>
    /// 지정 범위의 Land 칸을 모두 해당 플레이어가 소유 중인지 확인한다.
    /// 특수 칸은 무시한다.
    /// </summary>
    private bool IsEntireLandLineOwnedByPlayer(int playerIndex, int startIndex, int endIndex)
    {
        if (board == null || board.CellCount <= 0)
            return false;

        bool hasLandCell = false;

        for (int i = startIndex; i <= endIndex; i++)
        {
            if (i < 0 || i >= board.CellCount || i >= MaxCells)
                continue;

            BulmabulCellData cell = board.GetCell(i);

            if (cell == null)
                continue;

            if (cell.cellType != BulmabulCellType.Land)
                continue;

            hasLandCell = true;

            if (gameState.LandOwnerByCell.Get(i) != playerIndex)
                return false;
        }

        return hasLandCell;
    }

    /// <summary>
    /// 특정 플레이어가 소유한 모든 땅과 건물을 해제한다.
    /// 파산 패배 테스트용.
    /// </summary>
    private void ReleaseAllOwnedLands(int playerIndex)
    {
        if (board == null)
            board = FindObjectOfType<BulmabulBoard>();

        int count = board != null && board.CellCount > 0
            ? Mathf.Min(board.CellCount, MaxCells)
            : MaxCells;

        for (int i = 0; i < count; i++)
        {
            if (gameState.LandOwnerByCell.Get(i) != playerIndex)
                continue;

            gameState.LandOwnerByCell.Set(i, -1);
            gameState.LandBuildingFlagsByCell.Set(i, 0);
        }
    }

    /// <summary>
    /// 라인 독점 승리 조건을 테스트 스크립트에서 직접 검사한다.
    /// lineId 기준으로 같은 라인에 있는 모든 Land 칸을 소유하면 라인 1개 독점.
    /// </summary>
    private void CheckWinnerByLineMonopolyForDebug(int playerIndex)
    {
        if (gameState.GameFinished)
            return;

        if (board == null || board.CellCount <= 0)
            return;

        int needCount = Mathf.Max(1, lineMonopolyWinCount);

        int monopolyCount = 0;

        /*
         * lineId는 현재 맵 생성기 기준으로 0~7 정도를 사용한다.
         * 혹시 더 늘어나도 0~31까지는 검사 가능하게 둔다.
         */
        for (int lineId = 0; lineId < 32; lineId++)
        {
            bool hasLineLand = false;
            bool allOwned = true;

            int count = Mathf.Min(board.CellCount, MaxCells);

            for (int i = 0; i < count; i++)
            {
                BulmabulCellData cell = board.GetCell(i);

                if (cell == null)
                    continue;

                if (cell.cellType != BulmabulCellType.Land)
                    continue;

                if (cell.isLandmark)
                    continue;

                if (cell.lineId != lineId)
                    continue;

                hasLineLand = true;

                if (gameState.LandOwnerByCell.Get(i) != playerIndex)
                {
                    allOwned = false;
                    break;
                }
            }

            if (hasLineLand && allOwned)
                monopolyCount++;
        }

        if (monopolyCount < needCount)
            return;

        FinishGameByWinnerForDebug(
            playerIndex,
            $"{GetPlayerName(playerIndex)}님이 테스트 키 9번으로 라인 {monopolyCount}개를 독점하여 승리했습니다."
        );
    }

    /// <summary>
    /// 파산 후 남은 플레이어가 1명인지 확인하고 승리 처리한다.
    /// </summary>
    private void CheckWinnerByRemainingPlayersForDebug()
    {
        if (gameState.GameFinished)
            return;

        int aliveCount = 0;
        int lastAlivePlayerIndex = -1;

        for (int i = 0; i < MaxPlayers; i++)
        {
            if (!IsValidAlivePlayer(i))
                continue;

            aliveCount++;
            lastAlivePlayerIndex = i;
        }

        if (aliveCount == 1 && lastAlivePlayerIndex >= 0)
        {
            FinishGameByWinnerForDebug(
                lastAlivePlayerIndex,
                $"{GetPlayerName(lastAlivePlayerIndex)}님이 마지막 생존자로 승리했습니다."
            );
        }
    }

    /// <summary>
    /// 테스트 스크립트에서 직접 게임 종료 상태를 설정한다.
    /// </summary>
    private void FinishGameByWinnerForDebug(int winnerIndex, string reason)
    {
        if (gameState.GameFinished)
            return;

        gameState.GameFinished = true;
        gameState.WinnerIndex = winnerIndex;
        gameState.TurnBusy = true;

        gameState.PendingActionInt = 0;
        gameState.PendingPlayerIndex = -1;
        gameState.PendingCellIndex = -1;
        gameState.PendingWasDouble = false;
        gameState.PendingChanceCardId = "";
        gameState.PendingTaxAmount = 0;
        gameState.PendingTaxSourceInt = 0;

        gameState.IsPaused = false;
        gameState.PauseOwner = PlayerRef.None;
        gameState.PausedRemainSeconds = 0f;

        gameState.LastLogMessage = reason;

        BumpGameStateRevision();

        Debug.Log($"[BulmabulDebugTestKeyController] Game finished by debug. Winner={winnerIndex}, Reason={reason}");
    }

    private int FindPlayerIndex(PlayerRef playerRef)
    {
        if (playerRef == PlayerRef.None)
            return -1;

        for (int i = 0; i < MaxPlayers; i++)
        {
            BulmabulGameState.PlayerGameSlot slot = gameState.Players.Get(i);

            if (slot.occupied == 0)
                continue;

            if (slot.player == playerRef)
                return i;
        }

        return -1;
    }

    private bool IsValidAlivePlayer(int playerIndex)
    {
        if (playerIndex < 0 || playerIndex >= MaxPlayers)
            return false;

        BulmabulGameState.PlayerGameSlot slot = gameState.Players.Get(playerIndex);

        if (slot.occupied == 0)
            return false;

        if (slot.player == PlayerRef.None)
            return false;

        if (slot.bankrupt)
            return false;

        if (slot.leftGame)
            return false;

        return true;
    }

    private bool IsSameTeam(int a, int b)
    {
        if (a < 0 || a >= MaxPlayers)
            return false;

        if (b < 0 || b >= MaxPlayers)
            return false;

        BulmabulGameState.PlayerGameSlot aSlot = gameState.Players.Get(a);
        BulmabulGameState.PlayerGameSlot bSlot = gameState.Players.Get(b);

        if (aSlot.teamSideInt <= 0)
            return false;

        if (bSlot.teamSideInt <= 0)
            return false;

        return aSlot.teamSideInt == bSlot.teamSideInt;
    }

    private string GetPlayerName(int playerIndex)
    {
        if (playerIndex < 0 || playerIndex >= MaxPlayers)
            return $"Player {playerIndex + 1}";

        BulmabulGameState.PlayerGameSlot slot = gameState.Players.Get(playerIndex);

        string nick = slot.nickname.ToString();

        if (string.IsNullOrWhiteSpace(nick))
            nick = $"Player {playerIndex + 1}";

        return nick;
    }

    /// <summary>
    /// int 범위를 넘지 않게 재화를 더한다.
    /// 9번 테스트키 인수 비용을 기존 소유자에게 지급할 때 사용한다.
    /// </summary>
    private int SafeAddCash(int currentCash, int addAmount)
    {
        long result = (long)currentCash + addAmount;

        if (result > int.MaxValue)
            return int.MaxValue;

        if (result < int.MinValue)
            return int.MinValue;

        return (int)result;
    }

    private void BumpGameStateRevision()
    {
        gameState.Revision = gameState.Revision + 1;
    }
}