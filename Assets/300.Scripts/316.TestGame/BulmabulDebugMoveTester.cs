using Fusion;
using UnityEngine;

/// <summary>
/// 부루마불 테스트용 강제 이동 스크립트.
/// BulmabulGameState가 붙어있는 같은 NetworkObject에 붙여서 사용한다.
/// 
/// 숫자키:
/// 1 = 가장 가까운 적 소유 땅으로 이동
/// 2 = 팀전일 때 가장 가까운 내 팀원 소유 땅으로 이동
/// 3 = 가장 가까운 찬스 칸으로 이동
/// 4 = 시작 지점으로 이동
/// 5 = 가장 가까운 감옥 칸으로 이동
/// 6 = 가장 가까운 여행 칸으로 이동
/// </summary>
public class BulmabulDebugMoveTester : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private BulmabulGameState gameState;
    [SerializeField] private BulmabulBoard board;

    [Header("Debug Options")]
    [SerializeField] private bool enableDebugMove = true;
    [SerializeField] private bool resolveLanding = true;

    private enum DebugMoveType
    {
        NearestEnemyLand = 1,
        NearestTeamLand = 2,
        NearestChance = 3,
        Start = 4,
        NearestJail = 5,
        NearestTravel = 6
    }

    private void Awake()
    {
        CacheReferences();
    }

    public override void Spawned()
    {
        CacheReferences();
    }

    private void Update()
    {
        if (!enableDebugMove)
            return;

        CacheReferences();

        if (Runner == null || gameState == null || board == null)
            return;

        if (!gameState.IsSpawnReady)
            return;

        if (Input.GetKeyDown(KeyCode.Alpha1))
            RequestDebugMove(DebugMoveType.NearestEnemyLand);
        else if (Input.GetKeyDown(KeyCode.Alpha2))
            RequestDebugMove(DebugMoveType.NearestTeamLand);
        else if (Input.GetKeyDown(KeyCode.Alpha3))
            RequestDebugMove(DebugMoveType.NearestChance);
        else if (Input.GetKeyDown(KeyCode.Alpha4))
            RequestDebugMove(DebugMoveType.Start);
        else if (Input.GetKeyDown(KeyCode.Alpha5))
            RequestDebugMove(DebugMoveType.NearestJail);
        else if (Input.GetKeyDown(KeyCode.Alpha6))
            RequestDebugMove(DebugMoveType.NearestTravel);
    }

    private void CacheReferences()
    {
        if (gameState == null)
            gameState = BulmabulGameState.Instance;

        if (gameState == null)
            gameState = FindFirstObjectByType<BulmabulGameState>();

        if (board == null)
            board = FindFirstObjectByType<BulmabulBoard>();
    }

    private void RequestDebugMove(DebugMoveType moveType)
    {
        if (gameState == null || board == null)
        {
            ShowToast("테스트 이동 참조를 찾을 수 없습니다.", "Debug move references are missing.");
            return;
        }

        if (!gameState.IsSpawnReady)
            return;

        if (gameState.GameFinished)
        {
            ShowToast("게임이 종료되어 테스트 이동할 수 없습니다.", "Game is already finished.");
            return;
        }

        if (gameState.IsPaused || gameState.TurnBusy)
        {
            ShowToast("현재는 테스트 이동할 수 없습니다.", "Cannot debug move now.");
            return;
        }

        if (!gameState.IsMyTurn())
        {
            ShowToast("테스트 이동은 내 턴에만 가능합니다.", "Debug move is only allowed on your turn.");
            return;
        }

        RPC_RequestDebugMove((int)moveType, resolveLanding);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestDebugMove(int moveTypeInt, bool shouldResolveLanding, RpcInfo info = default)
    {
        CacheReferences();

        if (gameState == null || board == null)
            return;

        if (!gameState.IsSpawnReady)
            return;

        if (gameState.GameFinished || gameState.IsPaused || gameState.TurnBusy)
            return;

        int playerIndex = gameState.FindPlayerIndex(info.Source);

        if (!IsValidAlivePlayer(playerIndex))
            return;

        if (gameState.CurrentTurnIndex != playerIndex)
        {
            Debug.Log("[BulmabulDebugMoveTester] 테스트 이동은 현재 턴 플레이어만 사용할 수 있습니다.");
            return;
        }

        DebugMoveType moveType = (DebugMoveType)moveTypeInt;
        int targetCellIndex = FindTargetCellIndex(playerIndex, moveType);

        if (targetCellIndex < 0)
        {
            Debug.Log($"[BulmabulDebugMoveTester] 이동 대상 칸을 찾지 못했습니다. Type: {moveType}");
            return;
        }

        gameState.MovePlayerToCellByChanceCardForAuthority(playerIndex, targetCellIndex, shouldResolveLanding);

        BulmabulCellData cell = board.GetCell(targetCellIndex);
        string cellName = cell != null ? cell.cellName : targetCellIndex.ToString();

        Debug.Log($"[BulmabulDebugMoveTester] Player {playerIndex} DebugMove {moveType} -> {targetCellIndex} / {cellName}");
    }

    private int FindTargetCellIndex(int playerIndex, DebugMoveType moveType)
    {
        switch (moveType)
        {
            case DebugMoveType.NearestEnemyLand:
                return FindNearestEnemyOwnedLandForward(playerIndex);

            case DebugMoveType.NearestTeamLand:
                return FindNearestTeamOwnedLandForward(playerIndex);

            case DebugMoveType.NearestChance:
                return FindNearestCellTypeForward(playerIndex, BulmabulCellType.Chance);

            case DebugMoveType.Start:
                return FindFirstCellType(BulmabulCellType.Start);

            case DebugMoveType.NearestJail:
                return FindNearestCellTypeForward(playerIndex, BulmabulCellType.Jail);

            case DebugMoveType.NearestTravel:
                return FindNearestCellTypeForward(playerIndex, BulmabulCellType.Travel);
        }

        return -1;
    }

    private int FindNearestEnemyOwnedLandForward(int playerIndex)
    {
        if (!IsValidAlivePlayer(playerIndex))
            return -1;

        var actor = gameState.Players.Get(playerIndex);
        int startIndex = actor.tileIndex;
        int cellCount = board.CellCount;

        for (int step = 1; step < cellCount; step++)
        {
            int checkIndex = (startIndex + step) % cellCount;
            BulmabulCellData cell = board.GetCell(checkIndex);

            if (cell == null || cell.cellType != BulmabulCellType.Land)
                continue;

            int ownerIndex = gameState.LandOwnerByCell.Get(checkIndex);

            if (IsEnemyOwner(playerIndex, ownerIndex))
                return checkIndex;
        }

        return -1;
    }

    private int FindNearestTeamOwnedLandForward(int playerIndex)
    {
        if (!IsTeamMode())
        {
            Debug.Log("[BulmabulDebugMoveTester] 2번 이동은 팀전에서만 사용합니다.");
            return -1;
        }

        if (!IsValidAlivePlayer(playerIndex))
            return -1;

        var actor = gameState.Players.Get(playerIndex);
        int startIndex = actor.tileIndex;
        int cellCount = board.CellCount;

        for (int step = 1; step < cellCount; step++)
        {
            int checkIndex = (startIndex + step) % cellCount;
            BulmabulCellData cell = board.GetCell(checkIndex);

            if (cell == null || cell.cellType != BulmabulCellType.Land)
                continue;

            int ownerIndex = gameState.LandOwnerByCell.Get(checkIndex);

            if (IsTeamOwner(playerIndex, ownerIndex))
                return checkIndex;
        }

        return -1;
    }

    private int FindNearestCellTypeForward(int playerIndex, BulmabulCellType cellType)
    {
        if (!IsValidAlivePlayer(playerIndex))
            return -1;

        var actor = gameState.Players.Get(playerIndex);
        int startIndex = actor.tileIndex;
        int cellCount = board.CellCount;

        for (int step = 1; step < cellCount; step++)
        {
            int checkIndex = (startIndex + step) % cellCount;
            BulmabulCellData cell = board.GetCell(checkIndex);

            if (cell != null && cell.cellType == cellType)
                return checkIndex;
        }

        return -1;
    }

    private int FindFirstCellType(BulmabulCellType cellType)
    {
        if (board == null || board.CellCount <= 0)
            return -1;

        for (int i = 0; i < board.CellCount; i++)
        {
            BulmabulCellData cell = board.GetCell(i);

            if (cell != null && cell.cellType == cellType)
                return i;
        }

        return -1;
    }

    private bool IsEnemyOwner(int playerIndex, int ownerIndex)
    {
        if (ownerIndex < 0)
            return false;

        if (ownerIndex == playerIndex)
            return false;

        if (!IsValidAlivePlayer(ownerIndex))
            return false;

        if (!IsTeamMode())
            return true;

        var actor = gameState.Players.Get(playerIndex);
        var owner = gameState.Players.Get(ownerIndex);

        if (actor.teamSideInt == (int)TeamSide.None)
            return true;

        if (owner.teamSideInt == (int)TeamSide.None)
            return true;

        return actor.teamSideInt != owner.teamSideInt;
    }

    private bool IsTeamOwner(int playerIndex, int ownerIndex)
    {
        if (ownerIndex < 0)
            return false;

        if (ownerIndex == playerIndex)
            return false;

        if (!IsValidAlivePlayer(ownerIndex))
            return false;

        if (!IsTeamMode())
            return false;

        var actor = gameState.Players.Get(playerIndex);
        var owner = gameState.Players.Get(ownerIndex);

        if (actor.teamSideInt == (int)TeamSide.None)
            return false;

        if (owner.teamSideInt == (int)TeamSide.None)
            return false;

        return actor.teamSideInt == owner.teamSideInt;
    }

    private bool IsTeamMode()
    {
        return BulmabulGameStartCache.ModeInt == (int)MatchMode.Team;
    }

    private bool IsValidAlivePlayer(int index)
    {
        if (gameState == null)
            return false;

        if (index < 0 || index >= BulmabulGameState.MaxPlayers)
            return false;

        var slot = gameState.Players.Get(index);

        if (slot.occupied == 0)
            return false;

        if (slot.bankrupt)
            return false;

        if (slot.leftGame)
            return false;

        return true;
    }

    private void ShowToast(string kor, string eng)
    {
        if (ToastMessageManager.instance != null)
        {
            ToastMessageManager.instance.ShowToast(kor, eng);
            return;
        }

        Debug.Log("[BulmabulDebugMoveTester] " + kor);
    }
}
