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
    [SerializeField] private BulmabulCameraFollow cameraFollow;

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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!enableDebugMove)
            return;

        CacheReferences();

        if (Runner == null || gameState == null || board == null)
            return;

        if (!gameState.IsSpawnReady)
            return;

        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
        {
            ForceExitFullMapViewForDebugKey();
            RequestDebugMove(DebugMoveType.NearestEnemyLand);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
        {
            ForceExitFullMapViewForDebugKey();
            RequestDebugMove(DebugMoveType.NearestTeamLand);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
        {
            ForceExitFullMapViewForDebugKey();
            RequestDebugMove(DebugMoveType.NearestChance);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4))
        {
            ForceExitFullMapViewForDebugKey();
            RequestDebugMove(DebugMoveType.Start);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5))
        {
            ForceExitFullMapViewForDebugKey();
            RequestDebugMove(DebugMoveType.NearestJail);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha6) || Input.GetKeyDown(KeyCode.Keypad6))
        {
            ForceExitFullMapViewForDebugKey();
            RequestDebugMove(DebugMoveType.NearestTravel);
        }
#endif
    }

    private void CacheReferences()
    {
        if (gameState == null)
            gameState = BulmabulGameState.Instance;

        if (gameState == null)
            gameState = FindFirstObjectByType<BulmabulGameState>();

        if (board == null)
            board = FindFirstObjectByType<BulmabulBoard>();

        if (cameraFollow == null)
            cameraFollow = FindFirstObjectByType<BulmabulCameraFollow>();
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

        if (gameState.PendingActionInt != (int)BulmabulGameState.PendingActionType.None)
        {
            ShowToast("선택 대기 중에는 테스트 이동할 수 없습니다.", "Cannot debug move while waiting for a choice.");
            return;
        }

        if (!gameState.IsMyTurn())
        {
            ShowToast("테스트 이동은 내 턴에만 가능합니다.", "Debug move is only allowed on your turn.");
            return;
        }

        int localIndex = gameState.FindPlayerIndex(Runner.LocalPlayer);

        if (localIndex >= 0)
        {
            var localSlot = gameState.Players.Get(localIndex);

            if (localSlot.isInJail)
            {
                ShowToast("감옥 상태에서는 테스트 이동할 수 없습니다.", "Cannot debug move while in jail.");
                return;
            }

            if (localSlot.hasTravelDestinationReady)
            {
                ShowToast("여행 목적지를 먼저 선택해야 합니다.", "Choose your travel destination first.");
                return;
            }
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

        if (gameState.PendingActionInt != (int)BulmabulGameState.PendingActionType.None)
            return;

        int playerIndex = gameState.FindPlayerIndex(info.Source);

        if (!IsValidAlivePlayer(playerIndex))
            return;

        if (gameState.CurrentTurnIndex != playerIndex)
        {
            Debug.Log("[BulmabulDebugMoveTester] 테스트 이동은 현재 턴 플레이어만 사용할 수 있습니다.");
            return;
        }

        var currentSlot = gameState.Players.Get(playerIndex);

        if (currentSlot.isInJail)
        {
            Debug.Log("[BulmabulDebugMoveTester] 감옥 상태에서는 테스트 이동할 수 없습니다.");
            return;
        }

        if (currentSlot.hasTravelDestinationReady)
        {
            Debug.Log("[BulmabulDebugMoveTester] 여행 목적지 선택 대기 중에는 테스트 이동할 수 없습니다.");
            return;
        }

        DebugMoveType moveType = (DebugMoveType)moveTypeInt;
        int targetCellIndex = FindTargetCellIndex(playerIndex, moveType);

        if (targetCellIndex < 0)
        {
            Debug.Log($"[BulmabulDebugMoveTester] 이동 대상 칸을 찾지 못했습니다. Type: {moveType}");
            return;
        }

        bool started = gameState.MovePlayerToCellByDebugForAuthority(playerIndex, targetCellIndex, true);

        if (!started)
        {
            Debug.Log("[BulmabulDebugMoveTester] 테스트 이동 시작 실패");
            return;
        }

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

    /// <summary>
    /// 테스트 키 입력 시 전체맵 보기 상태라면 즉시 해제한다.
    /// 
    /// 전체맵은 로컬 카메라 상태이므로 RPC 안에서 처리하지 않고,
    /// 키 입력을 받은 클라이언트에서 먼저 해제한다.
    /// </summary>
    private void ForceExitFullMapViewForDebugKey()
    {
        if (cameraFollow == null)
            cameraFollow = FindFirstObjectByType<BulmabulCameraFollow>();

        if (cameraFollow == null)
            return;

        if (!cameraFollow.IsFullMapView)
            return;

        cameraFollow.ForceFollowView(false);
    }
}
