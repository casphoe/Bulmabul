using Fusion;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 실제 부루마불 게임 진행 전체를 담당하는 네트워크 상태 오브젝트.
///
/// 이 스크립트는 UI/주사위 연출/말 이동 연출을 직접 처리하지 않는다.
/// - UI 표시: BulmabulGameUI
/// - 주사위 화면 연출: BulmabulDiceVisual
/// - 말 이동 연출: BulmabulPawnMover
/// - 주사위 결과 계산: BulmabulDiceRoller
/// - 땅/건물 계산: BulmabulLandSystem
///
/// 담당 기능:
/// 1. 게임 참여자 초기화
/// 2. 현재 턴 관리
/// 3. 턴 제한시간 60초 관리
/// 4. 장착 주사위 기준으로 주사위 2개 굴림
/// 5. 말 위치 네트워크 동기화
/// 6. 빈 땅 구매
/// 7. 땅 구매 직후 작은집 / 집 중 하나 건설
/// 8. 시작지점 도착 시 보유 땅에 작은집 / 집 / 큰집 / 호텔 건설
/// 9. 관광지/랜드마크 건설 불가
/// 10. 적 플레이어 땅 도착 시 건물 포함 통행료 지급
/// 11. 호텔은 통행료 지급 후에도 제거되지 않음
/// 12. 세금 / 보너스 / 여행 칸 처리
/// 13. 더블이면 같은 플레이어가 한 번 더 진행
/// 14. 더블이 아니면 다음 플레이어 턴
/// 15. 한 유저가 일시정지하면 전체 일시정지
/// 16. 유저마다 한 게임당 일시정지 5회 제한
/// </summary>
public class BulmabulGameState : NetworkBehaviour
{
    public static BulmabulGameState Instance { get; private set; }

    public const int MaxPlayers = 4;
    public const int MaxCells = 160;

    /// <summary>
    /// 게임에 참여한 플레이어 1명의 네트워크 데이터.
    /// </summary>
    [Serializable]
    public struct PlayerGameSlot : INetworkStruct
    {
        /// <summary>Fusion 플레이어 식별자</summary>
        public PlayerRef player;

        /// <summary>1이면 사용 중인 슬롯, 0이면 빈 슬롯</summary>
        public byte occupied;

        /// <summary>현재 말이 서 있는 보드 칸 인덱스</summary>
        public int tileIndex;

        /// <summary>현재 보유 재화</summary>
        public int cash;

        /// <summary>이번 게임에서 사용한 일시정지 횟수</summary>
        public int pauseUsed;

        /// <summary>장착 주사위 등급</summary>
        public int diceGrade;

        /// <summary>장착 주사위 별</summary>
        public int diceStar;

        /// <summary>장착 주사위 레벨</summary>
        public int diceLevel;

        /// <summary>파산 여부</summary>
        public NetworkBool bankrupt;

        /// <summary>여행 이동 비용</summary>
        public int travelCost;

        /// <summary>플레이어 닉네임</summary>
        public NetworkString<_32> nickname;

        /// <summary>프로필 이미지 URL</summary>
        public NetworkString<_256> photoUrl;

        /// <summary>계정 레벨</summary>
        public int level;

        /// <summary>방장 여부</summary>
        public NetworkBool isLeader;

        /// <summary>0=None, 1=Red, 2=Blue</summary>
        public int teamSideInt;

        /// <summary>이번 게임의 턴 순서</summary>
        public int turnOrder;

        /// <summary>RoomScene 슬롯 번호</summary>
        public int roomSlotIndex;

        /// <summary>시작지점을 통과한 횟수</summary>
        public int lapCount;

        /// <summary>게임 도중 나갔는지 여부. 일반 파산과 구분하기 위한 값</summary>
        public NetworkBool leftGame;

        /// <summary>나간 사유. 0=None, 1=ExitButton, 2=Disconnected</summary>
        public int leaveReasonInt;

        // 추가: Firebase UID
        public NetworkString<_128> uid;

        /// <summary>
        /// 여행 칸에서 비용을 지불한 뒤 다음 턴에 목적지를 선택할 수 있는 상태.
        /// true면 여행 목적지 선택 UI를 열 수 있다.
        /// </summary>
        public NetworkBool hasTravelDestinationReady;

        /// <summary>
        /// 보관 중인 천사 카드.
        /// 플레이어당 최대 1장.
        /// </summary>
        public NetworkBool hasAngelCard;

        /// <summary>
        /// 보관 중인 감옥 탈출 카드.
        /// 플레이어당 최대 1장.
        /// </summary>
        public NetworkBool hasJailEscapeCard;

        /// <summary>
        /// 보관 중인 여행 카드.
        /// 플레이어당 최대 1장.
        /// </summary>
        public NetworkBool hasTravelCard;

    }

    /// <summary>
    /// 플레이어 입력을 기다리는 상태.
    /// </summary>
    public enum PendingActionType
    {
        None = 0,
        BuyLand = 1,
        InitialBuildAfterBuy = 2,
        BuildFromStart = 3,
        TakeOverLand = 4,

        /// <summary>
        /// 상대 땅 도착 후 천사 카드를 사용할지 선택하는 상태.
        /// 사용하면 통행료 면제, 취소하면 통행료 지불.
        /// </summary>
        AngelCardTollChoice = 5,

        /// <summary>
        /// 여행 칸 도착 후 여행 비용을 지불할지 선택하는 상태.
        /// 확인하면 비용 차감 + 다음 턴 목적지 선택권 획득.
        /// 취소하면 여행하지 않음.
        /// </summary>
        TravelCostChoice = 6
    }

    [Header("Rule")]
    [Tooltip("시작 재화, 시작지점 통과 보너스 등의 기본 룰")]
    [SerializeField] private BulmabulRule rule = new BulmabulRule();

    [Header("Board")]
    [SerializeField] private BulmabulBoard board;

    [Header("View Components")]
    [Tooltip("주사위 굴림 화면 연출 담당")]
    [SerializeField] private BulmabulDiceVisual diceVisual;

    [Tooltip("말 이동 연출 담당")]
    [SerializeField] private BulmabulPawnMover pawnMover;

    [Tooltip("주사위 굴림 UI. 결과 표시가 끝난 뒤 Pawn 이동을 시작하기 위해 연출 시간을 참조한다.")]
    [SerializeField] private DiceRollingUI diceRollingUI;

    [Header("Board Generator")]
    [SerializeField] private BulmabulBoardGenerator boardGenerator;

    [Header("Camera")]
    [Tooltip("Main Camera에 붙은 카메라 팔로우 스크립트")]
    [SerializeField] private BulmabulCameraFollow cameraFollow;

    [Header("Turn Settings")]
    [SerializeField] private float turnSeconds = 300f;

    [Header("Pause Settings")]
    [SerializeField] private int maxPauseCountPerPlayer = 5;

    [Header("Land Settings")]
    [Tooltip("true면 빈 땅 도착 시 구매 패널 없이 자동 구매. false면 구매/패스 UI를 기다림")]
    [SerializeField] private bool autoBuyLand = false;

    /// <summary>게임 참여자 슬롯</summary>
    [Networked, Capacity(MaxPlayers)]
    public NetworkArray<PlayerGameSlot> Players => default;

    /// <summary>
    /// 칸별 소유주.
    /// -1이면 소유주 없음.
    /// 0~3이면 Players 슬롯 인덱스.
    /// </summary>
    [Networked, Capacity(MaxCells)]
    public NetworkArray<int> LandOwnerByCell => default;

    /// <summary>
    /// 칸별 건물 상태.
    /// 0 = 없음, 1 = 작은집, 2 = 집, 4 = 큰집, 8 = 호텔.
    /// </summary>
    [Networked, Capacity(MaxCells)]
    public NetworkArray<int> LandBuildingFlagsByCell => default;

    /// <summary>현재 턴인 플레이어 슬롯 인덱스</summary>
    [Networked] public int CurrentTurnIndex { get; set; }

    /// <summary>현재 턴 제한시간 타이머</summary>
    [Networked] public TickTimer TurnTimer { get; set; }

    /// <summary>현재 턴 처리 중인지</summary>
    [Networked] public NetworkBool TurnBusy { get; set; }

    /// <summary>전체 일시정지 여부</summary>
    [Networked] public NetworkBool IsPaused { get; set; }

    /// <summary>일시정지를 누른 플레이어</summary>
    [Networked] public PlayerRef PauseOwner { get; set; }

    /// <summary>일시정지 시점의 남은 턴 시간</summary>
    [Networked] public float PausedRemainSeconds { get; set; }

    /// <summary>마지막 왼쪽 주사위 값</summary>
    [Networked] public int LastDiceLeft { get; set; }

    /// <summary>마지막 오른쪽 주사위 값</summary>
    [Networked] public int LastDiceRight { get; set; }

    /// <summary>구매/건설 대기 상태 타입</summary>
    [Networked] public int PendingActionInt { get; set; }

    /// <summary>구매/건설 대기 중인 플레이어 슬롯</summary>
    [Networked] public int PendingPlayerIndex { get; set; }

    /// <summary>구매/건설 대기 중인 칸 인덱스</summary>
    [Networked] public int PendingCellIndex { get; set; }

    /// <summary>구매/건설 선택 대기 당시 주사위가 더블이었는지</summary>
    [Networked] public NetworkBool PendingWasDouble { get; set; }

    /// <summary>UI 갱신 감지용 버전</summary>
    [Networked] public int Revision { get; set; }

    /// <summary>모든 클라이언트에 표시할 마지막 로그 메시지</summary>
    [Networked] public NetworkString<_256> LastLogMessage { get; set; }


    /// <summary>게임 종료 여부</summary>
    [Networked] public NetworkBool GameFinished { get; set; }

    /// <summary>승리한 플레이어 인덱스</summary>
    [Networked] public int WinnerIndex { get; set; }

    [Networked] public int ChanceDeckSeed { get; set; }
    [Networked] public int ChanceDeckRemainCount { get; set; }
    [Networked] public NetworkBool ChanceDeckInitialized { get; set; }

    string controlText = "";

    private bool _submittedLocalDice;
    private bool _placedOnce;

    private int _lastRollVersion = 0;
    private int _lastDice1 = 0;
    private int _lastDice2 = 0;

    /// <summary>
    /// Fusion Spawned()가 끝나서 Networked Property 접근이 가능한 상태인지 여부.
    /// UI가 Players, CurrentTurnIndex 같은 Networked 값을 읽기 전에 확인한다.
    /// </summary>
    public bool IsSpawnReady { get; private set; }

    private PendingActionType PendingAction
    {
        get => (PendingActionType)PendingActionInt;
        set => PendingActionInt = (int)value;
    }

    public int MaxPauseCountPerPlayer => maxPauseCountPerPlayer;

    private void Awake()
    {
        Instance = this;
    }

    public override void Spawned()
    {
        Instance = this;
        // 아직 보드/말/카메라 초기화가 끝나지 않았으므로 false
        IsSpawnReady = false;
        _placedOnce = false;

        StartCoroutine(CoInitializeGameAfterBoardReady());

        if (Object.HasStateAuthority)
        {
            InitChanceDeckForFusion();
        }
        else
        {
            if (BulmabulChanceDeck.Instance != null)
                BulmabulChanceDeck.Instance.SetCardCountFromServer(ChanceDeckRemainCount);
        }
    }

    /// <summary>
    /// 보드 160칸이 생성된 뒤 게임 초기화를 시작한다.
    /// 
    /// 순서:
    /// 1. BoardGenerator가 160칸 생성
    /// 2. BulmabulBoard.CellCount 확인
    /// 3. PawnMover에 Board 연결
    /// 4. 말 프리팹 생성
    /// 5. 모든 말을 시작 지점에 배치
    /// 6. 카메라를 시작 지점으로 이동
    /// 7. StateAuthority가 게임 데이터 초기화
    /// 8. 로컬 주사위 정보 제출
    /// 9. 실제 참가자 말만 활성화
    /// 10. 카메라 타겟을 로컬 플레이어 말로 설정
    /// 11. UI 접근 허용
    /// </summary>
    private IEnumerator CoInitializeGameAfterBoardReady()
    {
        float timeout = 5f;

        while (timeout > 0f)
        {
            bool ready = EnsureBoardGenerated();

            if (ready)
                break;

            timeout -= Time.deltaTime;
            yield return null;
        }

        if (board == null || board.CellCount <= 0)
        {
            Debug.LogError("[BulmabulGameState] 보드 생성 실패. 게임을 시작할 수 없습니다.");
            yield break;
        }

        if (board.CellCount != MaxCells)
        {
            Debug.LogWarning(
                $"[BulmabulGameState] Board CellCount({board.CellCount})와 MaxCells({MaxCells})가 다릅니다."
            );
        }

        if (pawnMover != null)
        {
            pawnMover.SetBoard(board);

            // 말 프리팹 1~4번 생성
            pawnMover.EnsurePawnsCreated();

            // 모든 말을 시작 지점 Cell 0에 배치
            pawnMover.PlaceAllPawnsAtStart();
        }

        // 전체 맵 보기에서 보드 중심을 계산할 수 있도록 카메라에도 Board를 넘긴다.
        if (cameraFollow != null)
            cameraFollow.SetBoard(board);


        // 카메라를 우선 시작 지점으로 이동
        SnapCameraToStartCell();

        if (Object.HasStateAuthority)
            ServerInitializeGame();

        SubmitLocalEquippedDice();

        // 실제 참가자만 켜고, 없는 플레이어 말은 끔
        PlaceAllPawnsImmediate();

        // 카메라 최종 타겟은 자기 로컬 플레이어 말
        UpdateCameraTargetToLocalPlayer(true);

        IsSpawnReady = true;

        Debug.Log($"[BulmabulGameState] 게임 초기화 완료. Board CellCount = {board.CellCount}");
    }

    /// <summary>
    /// 게임 시작 시 카메라를 시작 지점 Cell 0으로 즉시 이동한다.
    /// </summary>
    private void SnapCameraToStartCell()
    {
        if (cameraFollow == null)
            return;

        if (board == null || board.CellCount <= 0)
            return;

        cameraFollow.SnapToStartCellPosition(board.GetCellPosition(0));
    }

    /// <summary>
    /// 로컬 플레이어의 말로 카메라 타겟을 설정한다.
    /// 카메라는 기본적으로 자기 플레이어 말을 따라간다.
    /// </summary>
    private void UpdateCameraTargetToLocalPlayer(bool snap)
    {
        if (cameraFollow == null)
            return;

        if (pawnMover == null)
            return;

        if (Runner == null)
            return;

        int localIndex = FindPlayerIndex(Runner.LocalPlayer);

        if (localIndex < 0)
        {
            Debug.LogWarning("[BulmabulGameState] 로컬 플레이어 인덱스를 찾지 못해 카메라 타겟 설정 실패");
            return;
        }

        Transform localPawn = pawnMover.GetPawnTransform(localIndex);

        if (localPawn == null)
        {
            Debug.LogWarning("[BulmabulGameState] 로컬 플레이어 말 Transform이 없어 카메라 타겟 설정 실패");
            return;
        }

        if (snap)
            cameraFollow.SetTargetAndSnap(localPawn);
        else
            cameraFollow.SetTarget(localPawn);
    }

    /// <summary>
    /// 맵이 생성되어 있는지 확인하고, 없으면 생성한다.
    /// </summary>
    private bool EnsureBoardGenerated()
    {
        if (boardGenerator != null)
        {
            bool generated = boardGenerator.GenerateIfNeeded();

            if (!generated)
                return false;
        }

        if (board == null)
        {
            Debug.LogError("[BulmabulGameState] Board가 연결되어 있지 않습니다.");
            return false;
        }

        if (board.CellCount <= 0)
        {
            Debug.LogWarning("[BulmabulGameState] Board CellCount가 0입니다. 맵 생성 대기 중...");
            return false;
        }

        return true;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        IsSpawnReady = false;

        if (Instance == this)
            Instance = null;
    }

    private void OnDestroy()
    {
        IsSpawnReady = false;

        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        if (!IsSpawnReady)
            return;

        if (!_placedOnce)
        {
            PlaceAllPawnsImmediate();
            _placedOnce = true;
        }
    }

    public override void FixedUpdateNetwork()
    {

        if (!Object.HasStateAuthority)
            return;

        if (Runner == null)
            return;

        if (GameFinished)
            return;

        if (IsPaused)
            return;

        if (TurnBusy)
            return;

        if (PendingAction != PendingActionType.None)
            return;

        if (!IsValidAlivePlayer(CurrentTurnIndex))
        {
            AdvanceTurn();
            return;
        }

        if (TurnTimer.Expired(Runner))
        {
            LogServer("턴 시간 초과. 다음 플레이어 턴으로 넘어갑니다.");
            AdvanceTurn();
        }
    }

    #region 초기화

    /// <summary>
    /// 게임 시작 시 서버/StateAuthority가 플레이어 슬롯, 땅 소유권, 건물 상태를 초기화한다.
    /// 
    /// 팀 모드 규칙:
    /// - 1번과 3번은 같은 팀
    /// - 2번과 4번은 반대 팀
    /// - Red팀이 1,3이 될지 Blue팀이 1,3이 될지는 랜덤
    /// </summary>
    private void ServerInitializeGame()
    {
        if (!Object.HasStateAuthority)
            return;

        List<PlayerRef> activePlayers = new List<PlayerRef>();

        foreach (var p in Runner.ActivePlayers)
        {
            if (p != PlayerRef.None)
                activePlayers.Add(p);
        }

        activePlayers.Sort((a, b) => a.PlayerId.CompareTo(b.PlayerId));

        int seed = Environment.TickCount;

        if (Runner != null)
        {
            int playerCount = 0;

            foreach (var p in Runner.ActivePlayers)
            {
                if (p != PlayerRef.None)
                    playerCount++;
            }

            seed ^= playerCount * 7919;
            seed ^= DateTime.Now.Millisecond * 104729;
        }

        System.Random random = new System.Random(seed);

        List<PlayerRef> orderedPlayers = BuildRandomTurnOrder(activePlayers, random);

        Debug.Log($"[BulmabulGameState] Random turn seed = {seed}");

        for (int i = 0; i < MaxPlayers; i++)
        {
            PlayerGameSlot slot = new PlayerGameSlot
            {
                player = PlayerRef.None,
                occupied = 0,
                tileIndex = 0,
                cash = ClampLongToInt(rule.StartCash),
                pauseUsed = 0,

                uid = "",
                nickname = "",
                photoUrl = "",
                level = 1,
                isLeader = false,
                teamSideInt = (int)TeamSide.None,
                turnOrder = 0,
                roomSlotIndex = -1,

                diceGrade = (int)DiceGrade.Common,
                diceStar = 1,
                diceLevel = 1,

                bankrupt = false,
                hasTravelDestinationReady = false,
                hasAngelCard = false,
                hasJailEscapeCard = false,
                hasTravelCard = false,
                travelCost = 0,
                leftGame = false,
                leaveReasonInt = 0
            };

            if (i < orderedPlayers.Count)
            {
                PlayerRef player = orderedPlayers[i];

                slot.player = player;
                slot.occupied = 1;
                slot.turnOrder = i + 1;

                if (BulmabulGameStartCache.TryGetByPlayer(player, out var cached))
                {
                    slot.uid = cached.uid;
                    slot.nickname = cached.nickname;
                    slot.photoUrl = cached.photoUrl;
                    slot.level = Mathf.Max(1, cached.level);
                    slot.isLeader = cached.isLeader;
                    slot.teamSideInt = cached.teamSideInt;
                    slot.roomSlotIndex = cached.roomSlotIndex;
                }
                else
                {
                    slot.nickname = $"Player {i + 1}";
                    slot.photoUrl = "";
                    slot.level = 1;
                    slot.isLeader = false;
                    slot.teamSideInt = (int)TeamSide.None;
                    slot.roomSlotIndex = -1;
                }

                string teamText = "None";

                if (slot.teamSideInt == (int)TeamSide.Red)
                    teamText = "Red";
                else if (slot.teamSideInt == (int)TeamSide.Blue)
                    teamText = "Blue";

                Debug.Log(
                    $"[BulmabulGameState] Turn {slot.turnOrder}: {slot.nickname} / Team={teamText} / Leader={slot.isLeader}"
                );
            }

            Players.Set(i, slot);
        }

        for (int i = 0; i < MaxCells; i++)
        {
            LandOwnerByCell.Set(i, -1);
            LandBuildingFlagsByCell.Set(i, BulmabulBuildFlags.None);
        }

        CurrentTurnIndex = FindFirstAlivePlayerIndex();

        PendingAction = PendingActionType.None;
        PendingPlayerIndex = -1;
        PendingCellIndex = -1;
        PendingWasDouble = false;

        IsPaused = false;
        PauseOwner = PlayerRef.None;
        PausedRemainSeconds = 0f;
        TurnBusy = false;
        GameFinished = false;
        WinnerIndex = -1;

        LastDiceLeft = 0;
        LastDiceRight = 0;

        LastLogMessage = "부루마불 게임이 시작되었습니다. 턴 순서는 팀 규칙에 맞게 랜덤으로 정해졌습니다.";

        StartTurnTimer();
        BumpRevision();
    }

    /// <summary>
    /// 게임 시작 시 사용할 플레이어 순서를 만든다.
    /// 
    /// 솔로 모드:
    /// - 전체 플레이어 완전 랜덤
    /// 
    /// 팀 모드:
    /// - 1번과 3번은 같은 팀
    /// - 2번과 4번은 반대 팀
    /// - Red팀이 1,3을 받을지 Blue팀이 1,3을 받을지는 랜덤
    /// </summary>
    private List<PlayerRef> BuildRandomTurnOrder(List<PlayerRef> activePlayers, System.Random random)
    {
        List<PlayerRef> result = new List<PlayerRef>();

        if (activePlayers == null || activePlayers.Count <= 0)
            return result;

        bool isTeamMode =
            BulmabulGameStartCache.HasCache &&
            BulmabulGameStartCache.ModeInt == (int)MatchMode.Team;

        if (!isTeamMode)
        {
            result.AddRange(activePlayers);
            ShuffleList(result, random);
            return result;
        }

        List<PlayerRef> redPlayers = new List<PlayerRef>();
        List<PlayerRef> bluePlayers = new List<PlayerRef>();

        for (int i = 0; i < activePlayers.Count; i++)
        {
            PlayerRef player = activePlayers[i];

            if (!BulmabulGameStartCache.TryGetByPlayer(player, out var cached))
                continue;

            if (cached.teamSideInt == (int)TeamSide.Red)
                redPlayers.Add(player);
            else if (cached.teamSideInt == (int)TeamSide.Blue)
                bluePlayers.Add(player);
        }

        ShuffleList(redPlayers, random);
        ShuffleList(bluePlayers, random);

        // 팀 모드는 정상적으로는 2:2여야 한다.
        // 혹시 캐시가 꼬였거나 인원이 부족하면 솔로 랜덤 방식으로 fallback.
        if (redPlayers.Count != 2 || bluePlayers.Count != 2)
        {
            Debug.LogWarning(
                $"[BulmabulGameState] Team order fallback. Red={redPlayers.Count}, Blue={bluePlayers.Count}"
            );

            result.AddRange(activePlayers);
            ShuffleList(result, random);
            return result;
        }

        bool redTeamFirst = random.Next(0, 2) == 0;

        if (redTeamFirst)
        {
            // 1,3 = Red / 2,4 = Blue
            result.Add(redPlayers[0]);   // 1번째
            result.Add(bluePlayers[0]);  // 2번째
            result.Add(redPlayers[1]);   // 3번째
            result.Add(bluePlayers[1]);  // 4번째

            Debug.Log("[BulmabulGameState] Team order: Red team first. Order = Red, Blue, Red, Blue");
        }
        else
        {
            // 1,3 = Blue / 2,4 = Red
            result.Add(bluePlayers[0]);  // 1번째
            result.Add(redPlayers[0]);   // 2번째
            result.Add(bluePlayers[1]);  // 3번째
            result.Add(redPlayers[1]);   // 4번째

            Debug.Log("[BulmabulGameState] Team order: Blue team first. Order = Blue, Red, Blue, Red");
        }

        return result;
    }

    /// <summary>
    /// 리스트 랜덤 섞기.
    /// </summary>
    private void ShuffleList<T>(List<T> list, System.Random random)
    {
        if (list == null || random == null)
            return;

        for (int i = 0; i < list.Count; i++)
        {
            int r = random.Next(i, list.Count);

            T temp = list[i];
            list[i] = list[r];
            list[r] = temp;
        }
    }

    private int ClampLongToInt(long value)
    {
        if (value > int.MaxValue)
            return int.MaxValue;

        if (value < int.MinValue)
            return int.MinValue;

        return (int)value;
    }

    /// <summary>
    /// 로컬 유저가 장착한 주사위 정보를 서버에 제출한다.
    /// Account.EquippedDice 1개를 기준으로 실제 게임에서는 주사위 2개를 굴린다.
    /// </summary>
    private void SubmitLocalEquippedDice()
    {
        if (_submittedLocalDice)
            return;

        if (Runner == null || Runner.LocalPlayer == PlayerRef.None)
            return;

        int grade = (int)DiceGrade.Common;
        int star = 1;
        int level = 1;
        string nickname = "Player";
        int accountLevel = 1;

        var fb = FireBaseAuthManager.Instance;

        if (fb != null && fb.IsReady && fb.CurrentAccount != null)
        {
            if (!string.IsNullOrWhiteSpace(fb.CurrentAccount.NickName))
                nickname = fb.CurrentAccount.NickName.Trim();

            accountLevel = Mathf.Max(1, fb.CurrentAccount.AccountLevel);

            if (fb.CurrentAccount.EquippedDice != null)
            {
                OwnedDice equipped = fb.CurrentAccount.EquippedDice;
                grade = (int)equipped.Grade;
                star = Mathf.Clamp(equipped.Star, 1, 5);
                level = Mathf.Clamp(equipped.Level, 1, 10);
            }
        }

        RPC_SubmitEquippedDice(grade, star, level, nickname, accountLevel);
        _submittedLocalDice = true;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_SubmitEquippedDice(int grade, int star, int level, string nickname, int accountLevel, RpcInfo info = default)
    {
        if (!Object.HasStateAuthority)
            return;

        int idx = FindPlayerIndex(info.Source);

        if (idx < 0)
            return;

        PlayerGameSlot s = Players.Get(idx);

        s.diceGrade = Mathf.Clamp(grade, 0, Enum.GetValues(typeof(DiceGrade)).Length - 1);
        s.diceStar = Mathf.Clamp(star, 1, 5);
        s.diceLevel = Mathf.Clamp(level, 1, 10);

        // RoomScene 캐시에서 닉네임이 이미 들어왔다면 유지.
        // 캐시가 없을 때만 로컬 제출 닉네임 사용.
        if (string.IsNullOrWhiteSpace(s.nickname.ToString()))
        {
            if (string.IsNullOrWhiteSpace(nickname))
                nickname = $"Player {idx + 1}";

            s.nickname = nickname.Trim();
        }

        // RoomScene에서 레벨이 안 넘어온 경우 보정
        if (s.level <= 1)
            s.level = Mathf.Max(1, accountLevel);

        Players.Set(idx, s);
        BumpRevision();
    }

    #endregion

    #region 로컬 요청 API

    /// <summary>
    /// 로컬 플레이어가 주사위 굴림을 요청한다.
    /// parityChoiceInt:
    /// 0 = 선택 없음
    /// 1 = 홀수
    /// 2 = 짝수
    /// 
    /// gaugePermille:
    /// 0~1000
    /// </summary>
    public void RequestRollDiceLocal(int parityChoiceInt = 0, int gaugePermille = 0)
    {
        if (!CanLocalRollDice())
            return;

        gaugePermille = Mathf.Clamp(gaugePermille, 0, 1000);

        RPC_RequestRollDice(parityChoiceInt, gaugePermille);
    }

    public void RequestBuyLandLocal()
    {
        if (!ShouldShowBuyPanelForLocalPlayer())
            return;

        // 클라이언트에서 한 번 막고,
        // 서버 RPC 안의 TryBuyLand에서도 다시 막는다.
        if (TryGetPendingBuyLackAmount(out _))
            return;

        RPC_RequestBuyLand();
    }

    public void RequestSkipBuyLandLocal()
    {
        if (!ShouldShowBuyPanelForLocalPlayer())
            return;

        RPC_RequestSkipBuyLand();
    }

    public void RequestSelectBuildTargetLocal(int cellIndex)
    {
        if (!ShouldShowBuildPanelForLocalPlayer())
            return;

        RPC_RequestSelectBuildTarget(cellIndex);
    }

    public void RequestBuildLocal(BulmabulBuildPart part)
    {
        if (!CanLocalBuild(part))
            return;

        if (TryGetPendingBuildLackAmount(part, out _))
            return;

        RPC_RequestBuild((int)part);
    }

    public void RequestSkipBuildLocal()
    {
        if (!ShouldShowBuildPanelForLocalPlayer())
            return;

        RPC_RequestSkipBuild();
    }

    /// <summary>
    /// 보관 중인 여행권 카드를 사용한다.
    /// 
    /// 목적지로 바로 이동하지 않는다.
    /// 내 말을 여행 칸으로 이동시키는 요청이다.
    /// 실제 카드 제거는 BulmabulChanceInventory에서 처리된다.
    /// </summary>
    public bool RequestUseTravelTicketCardLocal()
    {
        if (!CanLocalUseMoveToTravelCard())
            return false;

        RPC_RequestUseMoveToTravelCard();
        return true;
    }

    /// <summary>
    /// 보관 중인 감옥 탈출 카드를 사용한다.
    /// 실제 카드 소비는 StateAuthority의 RPC_RequestUseJailEscapeCard에서만 처리한다.
    /// </summary>
    public bool RequestUseJailEscapeCardLocal()
    {
        if (!CanLocalUseJailEscapeCard())
            return false;

        RPC_RequestUseJailEscapeCard();
        return true;
    }

    /// <summary>
    /// 로컬 플레이어가 여행 비용 결제 여부를 선택한다.
    /// payTravelCost = true  : 여행 비용 지불, 다음 턴 목적지 선택권 획득
    /// payTravelCost = false : 여행하지 않음
    /// </summary>
    public void RequestResolveTravelCostLocal(bool payTravelCost)
    {
        if (!ShouldShowTravelCostPopupForLocalPlayer())
            return;

        RPC_RequestResolveTravelCost(payTravelCost);
    }

    public void RequestPauseResumeLocal()
    {
        if (Runner == null)
            return;

        if (IsPaused)
        {
            if (IsLocalPauseOwner())
                RPC_RequestResume();
        }
        else
        {
            if (CanLocalPause())
                RPC_RequestPause();
        }
    }

    #endregion

    #region 턴 / 주사위

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestRollDice(int parityChoiceInt, int gaugePermille, RpcInfo info = default)
    {
        if (!Object.HasStateAuthority)
            return;

        if (IsPaused || TurnBusy)
            return;

        if (PendingAction != PendingActionType.None)
            return;

        if (!IsValidAlivePlayer(CurrentTurnIndex))
            return;

        PlayerGameSlot current = Players.Get(CurrentTurnIndex);

        if (info.Source != current.player)
        {
            Debug.LogWarning($"[BulmabulGameState] 주사위 요청 거부. requester={info.Source}, current={current.player}");
            return;
        }

        parityChoiceInt = Mathf.Clamp(parityChoiceInt, 0, 2);
        gaugePermille = Mathf.Clamp(gaugePermille, 0, 1000);

        StartCoroutine(CoResolveRollTurn(CurrentTurnIndex, parityChoiceInt, gaugePermille));
    }

    private IEnumerator CoResolveRollTurn(int turnIndex, int parityChoiceInt, int gaugePermille)
    {
        TurnBusy = true;
        BumpRevision();

        PlayerGameSlot actor = Players.Get(turnIndex);

        int fromIndex = actor.tileIndex;

        BulmabulDiceRollResult roll = BulmabulDiceRoller.RollTwoDiceControlled(
            actor.diceGrade,
            actor.diceStar,
            actor.diceLevel,
            parityChoiceInt,
            gaugePermille
        );

        LastDiceLeft = roll.left;
        LastDiceRight = roll.right;
        PendingWasDouble = roll.isDouble;

        // UI가 최근 주사위 결과를 받을 수 있도록 버전을 올린다.
        OnDiceRollResolved(roll.left, roll.right);

        string actorName = GetPlayerDisplayName(turnIndex);

        LogServer($"{actorName}님 주사위: {roll.left}, {roll.right} / 이동 {roll.sum}칸{controlText}");

        // 모든 클라이언트에 주사위 결과를 전달한다.
        // 각 클라이언트의 DiceRollingUI가 이 결과를 받아 최종 값으로 멈춘다.
        RPC_PlayDiceVisual(roll.left, roll.right);

        // 주사위 UI가 완전히 끝날 때까지 기다린 뒤 Pawn 이동 시작.
        // 즉, 결과 표시 → UI 꺼짐 → Pawn 이동 순서가 된다.
        float diceWait = GetDicePresentationWaitSeconds();
        yield return new WaitForSeconds(diceWait);

        int targetIndex = CalculateTargetIndexAndPaySalary(turnIndex, fromIndex, roll.sum);

        RPC_PlayPawnMoveVisual(turnIndex, fromIndex, roll.sum);

        float moveWait = pawnMover != null ? pawnMover.MoveStepSeconds * roll.sum + 0.1f : 0.22f * roll.sum + 0.1f;
        yield return new WaitForSeconds(moveWait);

        actor = Players.Get(turnIndex);
        actor.tileIndex = targetIndex;
        Players.Set(turnIndex, actor);

        BumpRevision();

        bool waitsForPlayerChoice = ResolveLanding(turnIndex, targetIndex);

        if (waitsForPlayerChoice)
        {
            TurnBusy = false;
            BumpRevision();
            yield break;
        }

        FinishTurnAfterAction(turnIndex, roll.isDouble);

        TurnBusy = false;
        BumpRevision();
    }

    private int CalculateTargetIndexAndPaySalary(int playerIndex, int fromIndex, int moveCount)
    {
        if (board == null || board.CellCount <= 0)
            return 0;

        int boardCount = board.CellCount;
        int rawTarget = fromIndex + moveCount;
        int passStartCount = rawTarget / boardCount;
        int targetIndex = rawTarget % boardCount;

        if (passStartCount > 0)
        {
            PlayerGameSlot actor = Players.Get(playerIndex);
            int salary = ClampLongToInt(rule.SalaryOnStart) * passStartCount;
            actor.cash += salary;
            actor.lapCount += passStartCount;
            Players.Set(playerIndex, actor);

            LogServer($"{playerIndex}번 플레이어가 시작 지점을 {passStartCount}회 통과하여 {salary:N0} 획득");
        }

        return targetIndex;
    }

    private void FinishTurnAfterAction(int playerIndex, bool isDouble)
    {
        if (!Object.HasStateAuthority)
            return;

        if (GetAlivePlayerCount() <= 1)
        {
            LogServer("게임 종료 조건 도달");
            return;
        }

        PlayerGameSlot actor = Players.Get(playerIndex);

        if (!actor.bankrupt && isDouble)
        {
            StartTurnTimer();
            LogServer($"{playerIndex}번 플레이어 더블! 한 번 더 진행합니다.");
        }
        else
        {
            AdvanceTurn();
        }
    }

    private void AdvanceTurn()
    {
        if (!Object.HasStateAuthority)
            return;

        PendingAction = PendingActionType.None;
        PendingPlayerIndex = -1;
        PendingCellIndex = -1;
        PendingWasDouble = false;

        int next = CurrentTurnIndex;

        for (int i = 0; i < MaxPlayers; i++)
        {
            next++;

            if (next >= MaxPlayers)
                next = 0;

            if (IsValidAlivePlayer(next))
            {
                CurrentTurnIndex = next;
                StartTurnTimer();

                LogServer($"{CurrentTurnIndex}번 플레이어 턴입니다.");

                BumpRevision();
                return;
            }
        }
    }

    private void StartTurnTimer()
    {
        TurnTimer = TickTimer.CreateFromSeconds(Runner, turnSeconds);

        // 카메라는 항상 자기 로컬 플레이어 말 기준
        if (IsSpawnReady)
            UpdateCameraTargetToLocalPlayer(false);
    }

    #endregion

    #region 칸 도착 처리

    private bool ResolveLanding(int playerIndex, int cellIndex)
    {
        if (board == null)
            return false;

        BulmabulCellData cell = board.GetCell(cellIndex);

        if (cell == null)
            return false;

        switch (cell.cellType)
        {
            case BulmabulCellType.Start:
                LogServer($"{playerIndex}번 플레이어가 시작 칸에 도착했습니다.");
                return TryOpenStartBuildPending(playerIndex);

            case BulmabulCellType.Land:
                return ApplyLand(playerIndex, cellIndex, cell);

            case BulmabulCellType.Tax:
                ApplyTax(playerIndex, cell);
                return false;

            case BulmabulCellType.Bonus:
                ApplyBonus(playerIndex, cell);
                return false;

            case BulmabulCellType.Chance:
                return ApplyChance(playerIndex, cell);

            case BulmabulCellType.Jail:
                ApplyJail(playerIndex, cell);
                return false;

            case BulmabulCellType.Travel:
                return ApplyTravel(playerIndex, cellIndex, cell);
        }

        return false;
    }

    private bool ApplyLand(int playerIndex, int cellIndex, BulmabulCellData cell)
    {
        int ownerIndex = LandOwnerByCell.Get(cellIndex);

        if (ownerIndex < 0)
        {
            if (autoBuyLand)
            {
                bool bought = TryBuyLand(playerIndex, cellIndex, cell);

                if (bought && CanOpenInitialBuild(playerIndex, cellIndex))
                {
                    OpenInitialBuildPending(playerIndex, cellIndex);
                    return true;
                }

                return false;
            }

            // 재화가 부족해도 구매 패널은 열어준다.
            // 실제 구매 버튼 클릭 시 부족 금액을 토스트로 안내한다.
            PendingAction = PendingActionType.BuyLand;
            PendingPlayerIndex = playerIndex;
            PendingCellIndex = cellIndex;

            LogServer($"{playerIndex}번 플레이어가 {cell.cellName} 구매 선택 대기 중");

            BumpRevision();
            return true;
        }

        if (ownerIndex == playerIndex)
        {
            LogServer($"{playerIndex}번 플레이어가 자기 땅 {cell.cellName}에 도착했습니다.");
            return false;
        }

        if (!IsValidAlivePlayer(ownerIndex))
        {
            LandOwnerByCell.Set(cellIndex, -1);
            LandBuildingFlagsByCell.Set(cellIndex, BulmabulBuildFlags.None);

            LogServer($"{cell.cellName}의 소유자가 유효하지 않아 빈 땅으로 변경했습니다.");
            return false;
        }

        /*
         * 중요:
         * 상대 땅에 도착하면 바로 PayToll 하지 않는다.
         * 먼저 천사 카드 사용 여부 선택 상태로 넘긴다.
         *
         * 로컬 플레이어가 천사 카드를 가지고 있으면 UI 팝업이 뜨고,
         * 없으면 UI 쪽에서 자동으로 '사용 안 함' 요청을 보내게 만들 수 있다.
         */
        OpenAngelCardTollChoicePending(playerIndex, cellIndex);

        return true;
    }

    /// <summary>
    /// 상대 땅 도착 후 천사 카드 사용 여부 선택 상태를 연다.
    /// 체크 버튼을 누르면 통행료 면제,
    /// 취소 버튼을 누르면 기존처럼 통행료를 낸다.
    /// </summary>
    private void OpenAngelCardTollChoicePending(int payerIndex, int cellIndex)
    {
        PendingAction = PendingActionType.AngelCardTollChoice;
        PendingPlayerIndex = payerIndex;
        PendingCellIndex = cellIndex;

        BulmabulCellData cell = board != null ? board.GetCell(cellIndex) : null;
        int ownerIndex = LandOwnerByCell.Get(cellIndex);

        int toll = 0;

        if (cell != null)
        {
            int flags = LandBuildingFlagsByCell.Get(cellIndex);
            toll = BulmabulLandSystem.CalculateToll(cell, flags);
        }

        LogServer($"{payerIndex}번 플레이어가 {ownerIndex}번 플레이어의 땅에 도착했습니다. 천사 카드 사용 선택 대기 중. 통행료 {toll:N0}");

        BumpRevision();
    }

    private bool TryBuyLand(int playerIndex, int cellIndex, BulmabulCellData cell)
    {
        PlayerGameSlot actor = Players.Get(playerIndex);
        int cost = Mathf.Max(0, cell.buyCost);

        if (actor.cash < cost)
        {
            LogServer($"{playerIndex}번 플레이어가 {cell.cellName} 구매 실패. 재화 부족");
            return false;
        }

        actor.cash -= cost;

        Players.Set(playerIndex, actor);
        LandOwnerByCell.Set(cellIndex, playerIndex);
        LandBuildingFlagsByCell.Set(cellIndex, BulmabulBuildFlags.None);

        LogServer($"{playerIndex}번 플레이어가 {cell.cellName} 구매. 비용 {cost:N0}");

        BumpRevision();
        return true;
    }

    private bool PayToll(int payerIndex, int ownerIndex, int cellIndex, BulmabulCellData cell)
    {
        PlayerGameSlot payer = Players.Get(payerIndex);
        PlayerGameSlot owner = Players.Get(ownerIndex);

        int flags = LandBuildingFlagsByCell.Get(cellIndex);
        int toll = BulmabulLandSystem.CalculateToll(cell, flags);

        payer.cash -= toll;
        owner.cash += toll;

        if (payer.cash <= 0)
        {
            payer.cash = 0;
            payer.bankrupt = true;

            Players.Set(payerIndex, payer);
            Players.Set(ownerIndex, owner);

            ReleaseAllOwnedLands(payerIndex);

            LogServer($"{payerIndex}번 플레이어가 {ownerIndex}번 플레이어에게 통행료 {toll:N0} 지불 후 파산했습니다.");

            BumpRevision();
            return false;
        }

        Players.Set(payerIndex, payer);
        Players.Set(ownerIndex, owner);

        LogServer($"{payerIndex}번 플레이어가 {ownerIndex}번 플레이어에게 통행료 {toll:N0} 지불");

        BumpRevision();
        return true;
    }

    private bool CanOpenTakeOverPending(int buyerIndex, int ownerIndex, int cellIndex, BulmabulCellData cell)
    {
        if (!IsValidAlivePlayer(buyerIndex))
            return false;

        if (!IsValidAlivePlayer(ownerIndex))
            return false;

        if (cell == null || cell.cellType != BulmabulCellType.Land)
            return false;

        if (buyerIndex == ownerIndex)
            return false;

        int flags = LandBuildingFlagsByCell.Get(cellIndex);

        // 호텔이 있으면 인수 불가
        if (!BulmabulLandSystem.CanTakeOver(flags))
            return false;

        int cost = BulmabulLandSystem.CalculateTakeOverCost(cell, flags);

        return cost > 0;
    }

    private void OpenTakeOverPending(int buyerIndex, int cellIndex)
    {
        PendingAction = PendingActionType.TakeOverLand;
        PendingPlayerIndex = buyerIndex;
        PendingCellIndex = cellIndex;

        BulmabulCellData cell = board.GetCell(cellIndex);
        int flags = LandBuildingFlagsByCell.Get(cellIndex);
        int cost = BulmabulLandSystem.CalculateTakeOverCost(cell, flags);

        LogServer($"{buyerIndex}번 플레이어가 {cell.cellName} 인수를 선택할 수 있습니다. 인수 비용 {cost:N0}");

        BumpRevision();
    }

    private void ApplyTax(int playerIndex, BulmabulCellData cell)
    {
        PlayerGameSlot actor = Players.Get(playerIndex);
        int cost = Mathf.Max(0, cell.taxCost);

        actor.cash -= cost;

        if (actor.cash <= 0)
        {
            actor.cash = 0;
            actor.bankrupt = true;
            ReleaseAllOwnedLands(playerIndex);

            LogServer($"{playerIndex}번 플레이어가 세금 {cost:N0} 지불 후 파산했습니다.");
        }
        else
        {
            LogServer($"{playerIndex}번 플레이어가 세금 {cost:N0} 지불");
        }

        Players.Set(playerIndex, actor);
        BumpRevision();
    }

    /// <summary>
    /// 보너스 칸 도착 처리.
    /// 고정 금액이 아니라, 칸 데이터의 min~max 범위에서 랜덤으로 소액 지급한다.
    /// StateAuthority에서만 실행되어야 한다.
    /// </summary>
    private void ApplyBonus(int playerIndex, BulmabulCellData cell)
    {
        if (!Object.HasStateAuthority)
            return;

        if (cell == null)
            return;

        if (playerIndex < 0 || playerIndex >= MaxPlayers)
            return;

        PlayerGameSlot slot = Players.Get(playerIndex);

        if (slot.occupied == 0)
            return;

        int min = Mathf.Max(0, cell.bonusMinAmount);
        int max = Mathf.Max(min, cell.bonusMaxAmount);

        if (max <= 0)
        {
            LogServer($"{playerIndex + 1}번 플레이어가 보너스 칸에 도착했지만 지급액이 없습니다.");
            return;
        }

        // Random.Range(int, int)는 max가 exclusive라 +1 필요
        int amount = UnityEngine.Random.Range(min, max + 1);

        slot.cash = SafeAddCash(slot.cash, amount);
        Players.Set(playerIndex, slot);

        LogServer($"{playerIndex + 1}번 플레이어가 보너스 칸에서 {amount:N0}원을 받았습니다.");
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

    /// <summary>
    /// 찬스칸 도착 처리.
    /// 
    /// 찬스칸에 도착하면 덱의 맨 위 카드 1장을 자동으로 뽑는다.
    /// DrawTopCard() 내부에서 카드 더미 UI도 자동으로 줄어든다.
    /// </summary>
    private bool ApplyChance(int playerIndex, BulmabulCellData cell)
    {
        if (!Object.HasStateAuthority)
            return false;

        if (!IsValidAlivePlayer(playerIndex))
            return false;

        if (!ChanceDeckInitialized)
            InitChanceDeckForFusion();

        BulmabulChanceDeck deck = BulmabulChanceDeck.Instance;

        if (deck == null)
        {
            LogServer("찬스 카드 덱을 찾을 수 없습니다.");
            return false;
        }

        BulmabulChanceCardData card = deck.DrawTopCardForAuthority();

        if (card == null)
        {
            LogServer("뽑을 찬스 카드가 없습니다.");
            return false;
        }

        ChanceDeckRemainCount = deck.DrawPileCount;
        RPC_SyncChanceDeckUI(ChanceDeckRemainCount);

        LogServer($"{playerIndex}번 플레이어가 찬스 카드 [{card.GetName()}] 를 뽑았습니다.");

        BulmabulChanceCardExecutor executor = BulmabulChanceCardExecutor.Instance;

        if (executor == null)
        {
            LogServer("찬스 카드 실행기를 찾을 수 없습니다.");
            deck.DiscardForAuthority(card);
            return false;
        }

        bool waitsForPlayerChoice = executor.HandleDrawnCard(playerIndex, card);

        ChanceDeckRemainCount = deck.DrawPileCount;
        RPC_SyncChanceDeckUI(ChanceDeckRemainCount);

        return waitsForPlayerChoice;
    }

    private void ApplyJail(int playerIndex, BulmabulCellData cell)
    {
        LogServer($"{playerIndex}번 플레이어가 {cell.cellName} 칸에 도착했습니다.");
    }

    /// <summary>
    /// 여행 칸 도착 처리.
    /// 
    /// 주사위로 여행 칸에 도착해도 실행되고,
    /// MoveToTravelCard로 여행 칸에 이동해도 실행된다.
    /// 
    /// 여기서 바로 돈을 빼지 않는다.
    /// 먼저 여행 비용 결제 팝업을 열고,
    /// 확인을 누르면 그때 비용 차감 + 다음 턴 목적지 선택권 지급.
    /// </summary>
    private bool ApplyTravel(int playerIndex, int cellIndex, BulmabulCellData cell)
    {
        if (!Object.HasStateAuthority)
            return false;

        if (!IsValidAlivePlayer(playerIndex))
            return false;

        if (cell == null)
            return false;

        OpenTravelCostPending(playerIndex, cellIndex, cell);

        // 팝업 선택 대기 상태이므로 턴 진행을 멈춘다.
        return true;
    }

    /// <summary>
    /// 여행 칸 도착 후 여행 비용 결제 선택 상태를 연다.
    /// </summary>
    private void OpenTravelCostPending(int playerIndex, int cellIndex, BulmabulCellData cell)
    {
        PendingAction = PendingActionType.TravelCostChoice;
        PendingPlayerIndex = playerIndex;
        PendingCellIndex = cellIndex;

        int travelCost = cell != null ? Mathf.Max(0, cell.travelCost) : 0;

        LogServer($"{playerIndex}번 플레이어가 여행 칸에 도착했습니다. 여행 비용 {travelCost:N0} 결제 선택 대기 중.");

        BumpRevision();
    }

    private void ReleaseAllOwnedLands(int ownerIndex)
    {
        for (int i = 0; i < MaxCells; i++)
        {
            if (LandOwnerByCell.Get(i) == ownerIndex)
            {
                LandOwnerByCell.Set(i, -1);
                LandBuildingFlagsByCell.Set(i, BulmabulBuildFlags.None);
            }
        }
    }

    #endregion

    #region 땅 구매 선택

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestBuyLand(RpcInfo info = default)
    {
        if (!Object.HasStateAuthority)
            return;

        if (PendingAction != PendingActionType.BuyLand)
            return;

        if (!IsValidAlivePlayer(PendingPlayerIndex))
            return;

        PlayerGameSlot actor = Players.Get(PendingPlayerIndex);

        if (info.Source != actor.player)
            return;

        BulmabulCellData cell = board.GetCell(PendingCellIndex);

        if (cell == null)
            return;

        bool bought = TryBuyLand(PendingPlayerIndex, PendingCellIndex, cell);

        int finishedPlayer = PendingPlayerIndex;
        int boughtCell = PendingCellIndex;

        if (bought && CanOpenInitialBuild(finishedPlayer, boughtCell))
        {
            OpenInitialBuildPending(finishedPlayer, boughtCell);
            BumpRevision();
            return;
        }

        PendingAction = PendingActionType.None;
        PendingPlayerIndex = -1;
        PendingCellIndex = -1;

        FinishTurnAfterAction(finishedPlayer, PendingWasDouble);
        BumpRevision();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestSkipBuyLand(RpcInfo info = default)
    {
        if (!Object.HasStateAuthority)
            return;

        if (PendingAction != PendingActionType.BuyLand)
            return;

        if (!IsValidAlivePlayer(PendingPlayerIndex))
            return;

        PlayerGameSlot actor = Players.Get(PendingPlayerIndex);

        if (info.Source != actor.player)
            return;

        int finishedPlayer = PendingPlayerIndex;

        LogServer($"{finishedPlayer}번 플레이어가 땅 구매를 패스했습니다.");

        PendingAction = PendingActionType.None;
        PendingPlayerIndex = -1;
        PendingCellIndex = -1;

        FinishTurnAfterAction(finishedPlayer, PendingWasDouble);
        BumpRevision();
    }

    #endregion

    #region 건설 시스템

    private bool CanOpenInitialBuild(int playerIndex, int cellIndex)
    {
        if (!IsValidAlivePlayer(playerIndex))
            return false;

        if (LandOwnerByCell.Get(cellIndex) != playerIndex)
            return false;

        BulmabulCellData cell = board.GetCell(cellIndex);

        if (cell == null)
            return false;

        if (cell.cellType != BulmabulCellType.Land)
            return false;

        if (cell.isLandmark)
            return false;

        int flags = LandBuildingFlagsByCell.Get(cellIndex);

        if (flags != BulmabulBuildFlags.None)
            return false;

        PlayerGameSlot player = Players.Get(playerIndex);

        return BulmabulLandSystem.CanBuild(cell, flags, player.cash, BulmabulBuildPart.SmallHouse, true) ||
               BulmabulLandSystem.CanBuild(cell, flags, player.cash, BulmabulBuildPart.House, true);
    }

    private void OpenInitialBuildPending(int playerIndex, int cellIndex)
    {
        PendingAction = PendingActionType.InitialBuildAfterBuy;
        PendingPlayerIndex = playerIndex;
        PendingCellIndex = cellIndex;

        LogServer($"{playerIndex}번 플레이어가 구매한 땅에 건물을 지을 수 있습니다. 작은집/집 중 선택하세요.");
    }

    private bool TryOpenStartBuildPending(int playerIndex)
    {
        PlayerGameSlot player = Players.Get(playerIndex);

        if (player.lapCount <= 0)
            return false;

        if (!HasAnyBuildableOwnedLand(playerIndex))
            return false;

        PendingAction = PendingActionType.BuildFromStart;
        PendingPlayerIndex = playerIndex;
        PendingCellIndex = -1;

        LogServer($"{playerIndex}번 플레이어가 시작지점에 도착했습니다. 보유 땅 중 건설할 땅을 선택할 수 있습니다.");

        BumpRevision();
        return true;
    }

    private bool HasAnyBuildableOwnedLand(int playerIndex)
    {
        if (board == null)
            return false;

        for (int i = 0; i < board.CellCount && i < MaxCells; i++)
        {
            if (LandOwnerByCell.Get(i) != playerIndex)
                continue;

            if (CanBuildAnyOnCell(playerIndex, i))
                return true;
        }

        return false;
    }

    private bool CanBuildAnyOnCell(int playerIndex, int cellIndex)
    {
        if (!IsValidAlivePlayer(playerIndex))
            return false;

        if (board == null)
            return false;

        if (cellIndex < 0 || cellIndex >= board.CellCount || cellIndex >= MaxCells)
            return false;

        if (LandOwnerByCell.Get(cellIndex) != playerIndex)
            return false;

        BulmabulCellData cell = board.GetCell(cellIndex);
        PlayerGameSlot player = Players.Get(playerIndex);
        int flags = LandBuildingFlagsByCell.Get(cellIndex);

        bool initial = PendingAction == PendingActionType.InitialBuildAfterBuy;

        return BulmabulLandSystem.CanBuildAny(cell, flags, player.cash, initial);
    }

    private bool CanBuildPart(int playerIndex, int cellIndex, BulmabulBuildPart part)
    {
        if (!IsValidAlivePlayer(playerIndex))
            return false;

        if (board == null)
            return false;

        if (cellIndex < 0 || cellIndex >= board.CellCount || cellIndex >= MaxCells)
            return false;

        if (LandOwnerByCell.Get(cellIndex) != playerIndex)
            return false;

        BulmabulCellData cell = board.GetCell(cellIndex);
        PlayerGameSlot player = Players.Get(playerIndex);
        int flags = LandBuildingFlagsByCell.Get(cellIndex);

        bool initial = PendingAction == PendingActionType.InitialBuildAfterBuy;

        return BulmabulLandSystem.CanBuild(cell, flags, player.cash, part, initial);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestSelectBuildTarget(int cellIndex, RpcInfo info = default)
    {
        if (!Object.HasStateAuthority)
            return;

        if (PendingAction != PendingActionType.BuildFromStart)
            return;

        if (!IsValidAlivePlayer(PendingPlayerIndex))
            return;

        PlayerGameSlot player = Players.Get(PendingPlayerIndex);

        if (info.Source != player.player)
            return;

        if (!CanBuildAnyOnCell(PendingPlayerIndex, cellIndex))
            return;

        PendingCellIndex = cellIndex;

        BulmabulCellData cell = board.GetCell(cellIndex);
        LogServer($"{PendingPlayerIndex}번 플레이어가 건설 대상 땅으로 {cell.cellName} 선택");

        BumpRevision();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestBuild(int buildPartInt, RpcInfo info = default)
    {
        if (!Object.HasStateAuthority)
            return;

        if (PendingAction != PendingActionType.InitialBuildAfterBuy &&
            PendingAction != PendingActionType.BuildFromStart)
            return;

        if (!IsValidAlivePlayer(PendingPlayerIndex))
            return;

        PlayerGameSlot player = Players.Get(PendingPlayerIndex);

        if (info.Source != player.player)
            return;

        if (PendingCellIndex < 0)
            return;

        BulmabulBuildPart part = (BulmabulBuildPart)buildPartInt;

        if (!CanBuildPart(PendingPlayerIndex, PendingCellIndex, part))
            return;

        BulmabulCellData cell = board.GetCell(PendingCellIndex);

        if (cell == null)
            return;

        int cost = BulmabulLandSystem.GetBuildCost(cell, part);
        int flag = BulmabulLandSystem.GetBuildFlag(part);

        player.cash -= cost;

        int flags = LandBuildingFlagsByCell.Get(PendingCellIndex);
        flags = BulmabulBuildFlags.Add(flags, flag);

        Players.Set(PendingPlayerIndex, player);
        LandBuildingFlagsByCell.Set(PendingCellIndex, flags);

        LogServer($"{PendingPlayerIndex}번 플레이어가 {cell.cellName}에 {BulmabulLandSystem.GetBuildName(part)} 건설. 비용 {cost:N0}");

        int finishedPlayer = PendingPlayerIndex;
        int builtCellIndex = PendingCellIndex;

        /*
         * 중요:
         * 건물 하나 짓고 바로 턴을 넘기면 안 된다.
         *
         * 구매 직후:
         * - 작은집 / 집 둘 다 가능하면 계속 패널 유지
         *
         * 시작지점 건설:
         * - 작은집 / 집 / 큰집까지 가능하면 계속 패널 유지
         * - 작은집 + 집 + 큰집이 모두 있으면 호텔까지 가능
         */
        if (CanBuildAnyOnCell(finishedPlayer, builtCellIndex))
        {
            PendingPlayerIndex = finishedPlayer;
            PendingCellIndex = builtCellIndex;

            LogServer($"{finishedPlayer}번 플레이어가 {cell.cellName}에 추가 건설을 할 수 있습니다.");

            BumpRevision();
            return;
        }

        PendingAction = PendingActionType.None;
        PendingPlayerIndex = -1;
        PendingCellIndex = -1;

        FinishTurnAfterAction(finishedPlayer, PendingWasDouble);
        BumpRevision();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestSkipBuild(RpcInfo info = default)
    {
        if (!Object.HasStateAuthority)
            return;

        if (PendingAction != PendingActionType.InitialBuildAfterBuy &&
            PendingAction != PendingActionType.BuildFromStart)
            return;

        if (!IsValidAlivePlayer(PendingPlayerIndex))
            return;

        PlayerGameSlot player = Players.Get(PendingPlayerIndex);

        if (info.Source != player.player)
            return;

        int finishedPlayer = PendingPlayerIndex;

        LogServer($"{finishedPlayer}번 플레이어가 건설을 하지 않고 넘겼습니다.");

        PendingAction = PendingActionType.None;
        PendingPlayerIndex = -1;
        PendingCellIndex = -1;

        FinishTurnAfterAction(finishedPlayer, PendingWasDouble);
        BumpRevision();
    }

    #endregion

    #region 여행 이동


    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestResolveTravelCost(bool payTravelCost, RpcInfo info = default)
    {
        if (!Object.HasStateAuthority)
            return;

        if (PendingAction != PendingActionType.TravelCostChoice)
            return;

        if (!IsValidAlivePlayer(PendingPlayerIndex))
            return;

        PlayerGameSlot player = Players.Get(PendingPlayerIndex);

        if (info.Source != player.player)
            return;

        if (board == null)
            return;

        if (PendingCellIndex < 0 || PendingCellIndex >= board.CellCount)
            return;

        BulmabulCellData cell = board.GetCell(PendingCellIndex);

        if (cell == null || cell.cellType != BulmabulCellType.Travel)
            return;

        int playerIndex = PendingPlayerIndex;
        bool wasDouble = PendingWasDouble;

        int travelCost = Mathf.Max(0, cell.travelCost);

        if (payTravelCost)
        {
            player = Players.Get(playerIndex);

            if (player.cash < travelCost)
            {
                LogServer($"{playerIndex}번 플레이어는 여행 비용 {travelCost:N0}원이 부족해서 여행을 사용할 수 없습니다.");

                player.hasTravelDestinationReady = false;
                player.travelCost = 0;
                Players.Set(playerIndex, player);
            }
            else
            {
                player.cash -= travelCost;
                player.hasTravelDestinationReady = true;
                player.travelCost = 0;
                Players.Set(playerIndex, player);

                LogServer($"{playerIndex}번 플레이어가 여행 비용 {travelCost:N0}원을 지불했습니다. 다음 자기 턴에 목적지를 선택할 수 있습니다.");
            }
        }
        else
        {
            player = Players.Get(playerIndex);
            player.hasTravelDestinationReady = false;
            player.travelCost = 0;
            Players.Set(playerIndex, player);

            LogServer($"{playerIndex}번 플레이어가 여행을 취소했습니다.");
        }

        PendingAction = PendingActionType.None;
        PendingPlayerIndex = -1;
        PendingCellIndex = -1;

        /*
         * 주사위로 여행 칸에 도착한 경우:
         * 더블이면 FinishTurnAfterAction 내부에서 한 번 더 굴릴 수 있게 처리.
         *
         * 카드로 여행 칸에 이동한 경우:
         * CoResolveMoveToTravelByCard에서 PendingWasDouble = false로 세팅해야 함.
         */
        FinishTurnAfterAction(playerIndex, wasDouble);

        BumpRevision();
    }

    /// <summary>
    /// 로컬 플레이어가 여행 비용 결제 팝업을 봐야 하는지 확인.
    /// </summary>
    public bool ShouldShowTravelCostPopupForLocalPlayer()
    {
        if (Runner == null)
            return false;

        if (PendingAction != PendingActionType.TravelCostChoice)
            return false;

        if (!IsValidAlivePlayer(PendingPlayerIndex))
            return false;

        PlayerGameSlot slot = Players.Get(PendingPlayerIndex);

        return slot.player == Runner.LocalPlayer;
    }

    /// <summary>
    /// 여행 비용 팝업에 표시할 안내 문구.
    /// </summary>
    public string GetPendingTravelCostInfoText()
    {
        if (board == null)
            return GetTravelCostDefaultText();

        if (PendingCellIndex < 0 || PendingCellIndex >= board.CellCount)
            return GetTravelCostDefaultText();

        BulmabulCellData cell = board.GetCell(PendingCellIndex);

        if (cell == null)
            return GetTravelCostDefaultText();

        int travelCost = Mathf.Max(0, cell.travelCost);

        return GetByLanguageForState(
            $"{cell.cellName}\n여행 비용: {travelCost:N0}\n비용을 지불하고 다음 턴에 여행 목적지를 선택하시겠습니까?",
            $"{cell.cellName}\nTravel Cost: {travelCost:N0}\nPay now and choose a destination next turn?"
        );
    }

    private string GetTravelCostDefaultText()
    {
        return GetByLanguageForState(
            "여행 칸에 도착했습니다.\n비용을 지불하고 다음 턴에 여행하시겠습니까?",
            "You arrived at the Travel cell.\nPay now and travel next turn?"
        );
    }

    /// <summary>
    /// 여행 목적지 선택 후 호출된다.
    /// 
    /// 중요:
    /// 이 함수는 여행권을 사용하는 함수가 아니다.
    /// 여행 칸에서 비용을 지불해서 hasTravelDestinationReady == true가 된 뒤,
    /// 다음 자기 턴에 목적지를 선택할 때만 사용한다.
    /// </summary>
    public void RequestTravelMoveLocal(int targetCellIndex)
    {
        if (!CanLocalSelectTravelDestination())
            return;

        if (board == null || board.CellCount <= 0)
            return;

        targetCellIndex = board.ClampCellIndex(targetCellIndex);

        RPC_RequestTravelMove(targetCellIndex);
    }


    /// <summary>
    /// 보관 중인 여행 카드를 사용한다.
    /// 
    /// 목적지로 바로 이동하지 않는다.
    /// 내 말을 여행 칸으로 이동시키고,
    /// 여행 칸 효과 ApplyTravel()을 실행한다.
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestUseMoveToTravelCard(RpcInfo info = default)
    {
        if (!Object.HasStateAuthority)
            return;

        if (IsPaused || TurnBusy)
            return;

        if (PendingAction != PendingActionType.None)
            return;

        int playerIndex = FindPlayerIndex(info.Source);

        if (!IsValidAlivePlayer(playerIndex))
            return;

        /*
         * 자기 턴에만 여행 카드 사용 가능.
         * 보관 카드는 주사위 대신 사용하는 카드처럼 처리한다.
         */
        if (playerIndex != CurrentTurnIndex)
            return;

        if (board == null || board.CellCount <= 0)
            return;

        int travelCellIndex = FindTravelCellIndex();

        if (travelCellIndex < 0)
        {
            LogServer("여행 칸을 찾을 수 없습니다.");
            return;
        }

        if (!TryConsumeKeptChanceCardForAuthority(playerIndex, BulmabulChanceCardType.MoveToTravelCard))
        {
            LogServer($"{playerIndex}번 플레이어는 여행 카드를 가지고 있지 않습니다.");
            return;
        }

        StartCoroutine(CoResolveMoveToTravelByCard(playerIndex, travelCellIndex));
    }

    /// <summary>
    /// 감옥 탈출 카드 사용 요청.
    /// 카드 보유 여부와 현재 감옥 칸 여부를 서버(StateAuthority)가 최종 검증한 뒤 소비한다.
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestUseJailEscapeCard(RpcInfo info = default)
    {
        if (!Object.HasStateAuthority)
            return;

        if (IsPaused || TurnBusy)
            return;

        if (PendingAction != PendingActionType.None)
            return;

        int playerIndex = FindPlayerIndex(info.Source);

        if (!IsValidAlivePlayer(playerIndex))
            return;

        if (board == null || board.CellCount <= 0)
            return;

        PlayerGameSlot slot = Players.Get(playerIndex);

        if (slot.tileIndex < 0 || slot.tileIndex >= board.CellCount)
            return;

        BulmabulCellData cell = board.GetCell(slot.tileIndex);

        if (cell == null || cell.cellType != BulmabulCellType.Jail)
        {
            LogServer($"{playerIndex}번 플레이어는 감옥 칸에 있지 않아 감옥 탈출 카드를 사용할 수 없습니다.");
            return;
        }

        if (!TryConsumeKeptChanceCardForAuthority(playerIndex, BulmabulChanceCardType.JailEscapeCard))
        {
            LogServer($"{playerIndex}번 플레이어는 감옥 탈출 카드를 가지고 있지 않습니다.");
            return;
        }

        /*
         * 현재 프로젝트의 감옥 칸은 ApplyJail()에서 구속 턴을 저장하지 않는다.
         * 그래서 여기서는 서버 검증 후 카드만 소비한다.
         *
         * 나중에 감옥 턴 시스템이 생기면 여기에서
         * jailRemainTurn = 0;
         * 같은 처리를 넣으면 된다.
         */
        LogServer($"{playerIndex}번 플레이어가 감옥 탈출 카드를 사용했습니다.");

        BumpRevision();
    }

    /// <summary>
    /// MoveToTravelCard 사용 처리.
    /// 
    /// 찬스 카드로 얻은 여행 티켓은 무료 여행권이다.
    /// 내 말을 여행 칸으로 이동시킨 뒤,
    /// 여행 비용 없이 다음 자기 턴에 목적지를 선택할 수 있게 한다.
    /// </summary>
    private IEnumerator CoResolveMoveToTravelByCard(int playerIndex, int travelCellIndex)
    {
        TurnBusy = true;
        BumpRevision();

        PlayerGameSlot actor = Players.Get(playerIndex);
        int fromIndex = actor.tileIndex;

        LogServer($"{playerIndex}번 플레이어가 여행 티켓을 사용하여 여행 칸으로 이동합니다.");

        RPC_PlayDirectMoveVisual(playerIndex, fromIndex, travelCellIndex);

        float moveWait = pawnMover != null ? pawnMover.DirectMoveSeconds + 0.1f : 0.9f;
        yield return new WaitForSeconds(moveWait);

        actor = Players.Get(playerIndex);
        actor.tileIndex = travelCellIndex;

        /*
         * 핵심:
         * 여행 티켓은 무료이므로 ApplyTravel()을 호출하지 않는다.
         * ApplyTravel()은 일반 주사위 여행 칸 도착용이며,
         * 여행 비용 결제 팝업을 여는 함수다.
         */
        actor.hasTravelDestinationReady = true;
        actor.travelCost = 0;

        Players.Set(playerIndex, actor);

        LogServer($"{playerIndex}번 플레이어가 여행 티켓으로 무료 여행 선택권을 얻었습니다. 다음 자기 턴에 목적지를 선택할 수 있습니다.");

        /*
         * 여행 티켓 사용은 주사위 행동 대신 사용한 것으로 보고 턴 종료.
         */
        AdvanceTurn();

        TurnBusy = false;
        BumpRevision();
    }

    /// <summary>
    /// 여행 목적지 선택권을 사용해서 선택한 목적지로 이동한다.
    /// 
    /// 비용은 여행 칸에서 이미 지불했으므로 여기서 돈을 다시 빼지 않는다.
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestTravelMove(int targetCellIndex, RpcInfo info = default)
    {
        if (!Object.HasStateAuthority)
            return;

        if (IsPaused || TurnBusy)
            return;

        if (PendingAction != PendingActionType.None)
            return;

        if (!IsValidAlivePlayer(CurrentTurnIndex))
            return;

        PlayerGameSlot actor = Players.Get(CurrentTurnIndex);

        if (info.Source != actor.player)
            return;

        if (!actor.hasTravelDestinationReady)
            return;

        if (board == null || board.CellCount <= 0)
            return;

        targetCellIndex = board.ClampCellIndex(targetCellIndex);

        StartCoroutine(CoResolveTravelMove(CurrentTurnIndex, targetCellIndex));
    }

    /// <summary>
    /// 여행 목적지 선택권을 소비해서 선택한 목적지로 이동한다.
    /// </summary>
    private IEnumerator CoResolveTravelMove(int playerIndex, int targetCellIndex)
    {
        TurnBusy = true;
        BumpRevision();

        PlayerGameSlot actor = Players.Get(playerIndex);

        int fromIndex = actor.tileIndex;

        // 목적지 선택권 소비
        actor.hasTravelDestinationReady = false;
        actor.travelCost = 0;

        Players.Set(playerIndex, actor);

        LogServer($"{playerIndex}번 플레이어가 여행 목적지를 선택하여 {targetCellIndex}번 칸으로 이동합니다.");

        RPC_PlayDirectMoveVisual(playerIndex, fromIndex, targetCellIndex);

        float moveWait = pawnMover != null ? pawnMover.DirectMoveSeconds + 0.1f : 0.9f;
        yield return new WaitForSeconds(moveWait);

        actor = Players.Get(playerIndex);
        actor.tileIndex = targetCellIndex;
        Players.Set(playerIndex, actor);

        PendingWasDouble = false;

        bool waitsForChoice = ResolveLanding(playerIndex, targetCellIndex);

        if (waitsForChoice)
        {
            TurnBusy = false;
            BumpRevision();
            yield break;
        }

        AdvanceTurn();

        TurnBusy = false;
        BumpRevision();
    }


    /// <summary>
    /// 보드에서 여행 칸을 찾는다.
    /// 여러 개 있으면 첫 번째 여행 칸을 사용한다.
    /// </summary>
    private int FindTravelCellIndex()
    {
        if (board == null)
            return -1;

        for (int i = 0; i < board.CellCount; i++)
        {
            BulmabulCellData cell = board.GetCell(i);

            if (cell == null)
                continue;

            if (cell.cellType == BulmabulCellType.Travel)
                return i;
        }

        return -1;
    }
    #endregion

    #region 적 소유 땅 이동 함수

    /// <summary>
    /// 찬스 카드 효과:
    /// 현재 위치 기준으로 앞으로 가장 가까운 적 소유 땅으로 이동한다.
    ///
    /// 개인전:
    /// - 내 땅이 아닌 다른 생존 플레이어의 땅으로 이동한다.
    /// - 3명 이상 플레이 중이면 여러 상대 중 가장 가까운 상대 땅을 찾는다.
    ///
    /// 팀전:
    /// - 같은 팀의 땅은 제외한다.
    /// - 상대 팀 플레이어가 소유한 땅 중 가장 가까운 땅을 찾는다.
    ///
    /// 반환값:
    /// - true  = 이동 후 구매/통행료/천사카드 등 플레이어 선택 대기 발생
    /// - false = 추가 선택 없이 카드 효과 종료
    /// </summary>
    public bool MoveToNearestEnemyOwnedLandByChanceCardForAuthority(int playerIndex)
    {
        if (!Object.HasStateAuthority)
            return false;

        if (!IsValidAlivePlayer(playerIndex))
            return false;

        if (board == null || board.CellCount <= 0)
            return false;

        int targetCellIndex = FindNearestEnemyOwnedLandForward(playerIndex);

        if (targetCellIndex < 0)
        {
            LogServer($"{playerIndex}번 플레이어 기준으로 이동 가능한 적 소유 땅이 없습니다.");
            return false;
        }

        PlayerGameSlot actor = Players.Get(playerIndex);

        int fromIndex = actor.tileIndex;

        actor.tileIndex = targetCellIndex;
        Players.Set(playerIndex, actor);

        RPC_PlayDirectMoveVisual(playerIndex, fromIndex, targetCellIndex);

        BulmabulCellData targetCell = board.GetCell(targetCellIndex);
        string cellName = targetCell != null ? targetCell.cellName : $"{targetCellIndex}번 칸";

        LogServer($"{playerIndex}번 플레이어가 찬스 카드 효과로 가장 가까운 적 소유 땅 [{cellName}] 으로 이동했습니다.");

        BumpRevision();

        /*
         * 중요:
         * 그냥 위치만 바꾸면 안 된다.
         * 적 땅에 도착한 것이므로 ApplyLand까지 실행되어야 한다.
         * 여기서 ResolveLanding을 호출해야 천사 카드 선택 / 통행료 처리가 이어진다.
         */
        return ResolveLanding(playerIndex, targetCellIndex);
    }

    /// <summary>
    /// 현재 위치 기준으로 앞으로 가장 가까운 적 소유 땅을 찾는다.
    /// 
    /// 개인전:
    /// - 내 땅이 아닌 다른 생존 플레이어의 땅으로 이동한다.
    /// - 3명 이상 플레이 중이면 여러 상대 중 가장 가까운 상대 땅을 찾는다.
    /// 
    /// 팀전:
    /// - 같은 팀의 땅은 제외한다.
    /// - 상대 팀 플레이어가 소유한 땅 중 가장 가까운 땅을 찾는다.
    /// </summary>
    private int FindNearestEnemyOwnedLandForward(int playerIndex)
    {
        if (board == null || board.CellCount <= 0)
            return -1;

        if (!IsValidAlivePlayer(playerIndex))
            return -1;

        PlayerGameSlot actor = Players.Get(playerIndex);
        int startIndex = actor.tileIndex;
        int cellCount = board.CellCount;

        for (int step = 1; step < cellCount; step++)
        {
            int checkIndex = (startIndex + step) % cellCount;

            BulmabulCellData cell = board.GetCell(checkIndex);

            if (cell == null)
                continue;

            if (cell.cellType != BulmabulCellType.Land)
                continue;

            int ownerIndex = LandOwnerByCell.Get(checkIndex);

            if (!IsValidEnemyLandOwner(playerIndex, ownerIndex))
                continue;

            return checkIndex;
        }

        return -1;
    }

    /// <summary>
    /// 해당 땅 소유자가 카드 사용자의 적인지 확인한다.
    /// 개인전이면 자기 자신만 제외.
    /// 팀전이면 같은 팀 제외.
    /// </summary>
    private bool IsValidEnemyLandOwner(int playerIndex, int ownerIndex)
    {
        if (ownerIndex < 0)
            return false;

        if (ownerIndex == playerIndex)
            return false;

        if (!IsValidAlivePlayer(ownerIndex))
            return false;

        bool isTeamMode =
            BulmabulGameStartCache.ModeInt == (int)MatchMode.Team;

        if (!isTeamMode)
            return true;

        PlayerGameSlot actor = Players.Get(playerIndex);
        PlayerGameSlot owner = Players.Get(ownerIndex);

        if (actor.teamSideInt == (int)TeamSide.None)
            return true;

        if (owner.teamSideInt == (int)TeamSide.None)
            return true;

        return actor.teamSideInt != owner.teamSideInt;
    }

    #endregion

    #region 일시정지

    /// <summary>
    /// 게임 일시정지 요청 RPC.
    /// 
    /// 호출 대상:
    /// - 모든 클라이언트에서 요청 가능
    /// - 실제 처리는 StateAuthority 서버/호스트만 수행
    /// 
    /// 처리 내용:
    /// 1. 이미 일시정지 중이면 무시
    /// 2. 요청한 플레이어를 찾음
    /// 3. 플레이어별 일시정지 사용 횟수 제한 검사
    /// 4. 현재 턴 타이머의 남은 시간을 저장
    /// 5. 일시정지 사용 횟수 증가
    /// 6. 게임 상태를 일시정지로 변경
    /// 7. 일시정지한 플레이어를 PauseOwner로 저장
    /// 8. 로그 출력 및 UI 갱신용 Revision 증가
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestPause(RpcInfo info = default)
    {
        // StateAuthority가 아닌 클라이언트에서는 실제 상태를 변경하지 않는다.
        // 네트워크 게임 상태는 반드시 StateAuthority에서만 변경해야 한다.
        if (!Object.HasStateAuthority)
            return;

        // 이미 일시정지 중이면 중복 일시정지를 막는다.
        if (IsPaused)
            return;

        // RPC를 보낸 플레이어가 게임 참가자 목록에서 몇 번째 플레이어인지 찾는다.
        int idx = FindPlayerIndex(info.Source);

        // 참가자가 아니거나 찾을 수 없으면 무시한다.
        if (idx < 0)
            return;

        // 해당 플레이어의 게임 슬롯 정보를 가져온다.
        PlayerGameSlot s = Players.Get(idx);

        // 플레이어별 일시정지 사용 횟수를 초과했으면 일시정지를 허용하지 않는다.
        if (s.pauseUsed >= maxPauseCountPerPlayer)
            return;

        // 현재 턴 타이머의 남은 시간을 가져온다.
        // 일시정지 해제 시 이 남은 시간부터 다시 시작하기 위해 저장한다.
        float? remain = TurnTimer.RemainingTime(Runner);

        // 남은 시간이 있으면 최소 1초 이상으로 보정해서 저장한다.
        // 남은 시간을 가져오지 못한 경우 기본 턴 시간으로 저장한다.
        PausedRemainSeconds = remain.HasValue ? Mathf.Max(1f, remain.Value) : turnSeconds;

        // 이 플레이어의 일시정지 사용 횟수를 1 증가시킨다.
        s.pauseUsed++;

        // 수정된 플레이어 슬롯 정보를 Networked 배열에 다시 저장한다.
        Players.Set(idx, s);

        // 게임을 일시정지 상태로 변경한다.
        IsPaused = true;

        // 누가 일시정지했는지 저장한다.
        // 이후 재개는 이 PauseOwner만 가능하게 한다.
        PauseOwner = info.Source;

        // 로그에 표시할 닉네임을 가져온다.
        string pauseNick = s.nickname.ToString();

        // 닉네임이 비어 있으면 현재 언어에 맞는 기본 플레이어 이름을 사용한다.
        if (string.IsNullOrWhiteSpace(pauseNick))
            pauseNick = GetByLanguage($"플레이어 {idx + 1}", $"Player {idx + 1}");

        // 일시정지 로그 출력.
        LogServer($"{pauseNick}님이 일시정지했습니다. 사용 횟수 {s.pauseUsed}/{maxPauseCountPerPlayer}");

        // UI 갱신용 Revision 증가.
        // 각 클라이언트 UI가 IsPaused, PauseOwner, pauseUsed 변화를 감지하도록 한다.
        BumpRevision();
    }

    /// <summary>
    /// 게임 재개 요청 RPC.
    /// 
    /// 호출 대상:
    /// - 모든 클라이언트에서 요청 가능
    /// - 실제 처리는 StateAuthority 서버/호스트만 수행
    /// 
    /// 처리 내용:
    /// 1. 일시정지 상태가 아니면 무시
    /// 2. 일시정지한 플레이어가 아니면 재개 불가
    /// 3. 저장해 둔 남은 시간으로 턴 타이머 재시작
    /// 4. 일시정지 상태 해제
    /// 5. 로그 출력 및 UI 갱신용 Revision 증가
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestResume(RpcInfo info = default)
    {
        // StateAuthority가 아닌 클라이언트에서는 실제 상태를 변경하지 않는다.
        if (!Object.HasStateAuthority)
            return;

        // 일시정지 상태가 아니면 재개할 필요가 없으므로 무시한다.
        if (!IsPaused)
            return;

        // 일시정지를 건 플레이어만 게임을 재개할 수 있다.
        // 다른 플레이어가 재개 버튼을 눌러도 무시된다.
        if (info.Source != PauseOwner)
            return;

        // 게임 일시정지 상태를 해제한다.
        IsPaused = false;

        // 일시정지 소유자를 초기화한다.
        PauseOwner = PlayerRef.None;

        // 저장해 둔 남은 시간을 최소 1초 이상으로 보정한다.
        float resumeSeconds = Mathf.Max(1f, PausedRemainSeconds);

        // 일시정지 전 남아 있던 시간부터 턴 타이머를 다시 시작한다.
        TurnTimer = TickTimer.CreateFromSeconds(Runner, resumeSeconds);

        // 저장된 일시정지 남은 시간을 초기화한다.
        PausedRemainSeconds = 0f;

        // 재개 로그 출력.
        LogServer("게임이 재개되었습니다.");

        // UI 갱신용 Revision 증가.
        // 각 클라이언트 UI가 일시정지 해제 상태를 반영하도록 한다.
        BumpRevision();
    }

    #endregion

    #region 시각 연출 RPC
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_PlayDiceVisual(int left, int right)
    {
        // 기존 텍스트형 주사위 연출을 쓰고 있다면 유지
        if (diceVisual != null)
            diceVisual.Play(left, right);

        // 각 클라이언트의 UI가 최종 결과를 받을 수 있도록 저장
        OnDiceRollResolved(left, right);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_PlayPawnMoveVisual(int playerIndex, int fromIndex, int moveCount)
    {
        if (pawnMover != null)
            pawnMover.PlayStepMove(playerIndex, fromIndex, moveCount);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_PlayDirectMoveVisual(int playerIndex, int fromIndex, int targetIndex)
    {
        if (pawnMover != null)
            pawnMover.PlayDirectMove(playerIndex, fromIndex, targetIndex);
    }

    private void PlaceAllPawnsImmediate()
    {
        if (pawnMover == null)
            return;

        pawnMover.EnsurePawnsCreated();

        for (int i = 0; i < MaxPlayers; i++)
        {
            PlayerGameSlot s = Players.Get(i);
            pawnMover.PlacePawnImmediate(i, s.tileIndex, s.occupied == 1);
        }
    }

    #endregion

    #region UI 공개 API


    /// <summary>
    /// 현재 구매 대기 중인 땅을 살 때 부족한 재화가 있는지 확인한다.
    /// 부족하면 true와 부족 금액을 반환한다.
    /// </summary>
    public bool TryGetPendingBuyLackAmount(out int lackAmount)
    {
        lackAmount = 0;

        if (!ShouldShowBuyPanelForLocalPlayer())
            return false;

        if (board == null)
            return false;

        if (!IsValidAlivePlayer(PendingPlayerIndex))
            return false;

        if (PendingCellIndex < 0 || PendingCellIndex >= board.CellCount || PendingCellIndex >= MaxCells)
            return false;

        BulmabulCellData cell = board.GetCell(PendingCellIndex);

        if (cell == null)
            return false;

        if (cell.cellType != BulmabulCellType.Land)
            return false;

        if (LandOwnerByCell.Get(PendingCellIndex) >= 0)
            return false;

        PlayerGameSlot player = Players.Get(PendingPlayerIndex);

        int cost = Mathf.Max(0, cell.buyCost);

        if (player.cash >= cost)
            return false;

        lackAmount = cost - player.cash;
        return true;
    }

    /// <summary>
    /// 현재 건설 대기 중인 땅에 특정 건물을 지을 때 부족한 재화가 있는지 확인한다.
    /// 건설 규칙은 만족하지만 재화만 부족한 경우 true와 부족 금액을 반환한다.
    /// </summary>
    public bool TryGetPendingBuildLackAmount(BulmabulBuildPart part, out int lackAmount)
    {
        lackAmount = 0;

        if (!ShouldShowBuildPanelForLocalPlayer())
            return false;

        if (board == null)
            return false;

        if (!IsValidAlivePlayer(PendingPlayerIndex))
            return false;

        if (PendingCellIndex < 0 || PendingCellIndex >= board.CellCount || PendingCellIndex >= MaxCells)
            return false;

        if (LandOwnerByCell.Get(PendingCellIndex) != PendingPlayerIndex)
            return false;

        BulmabulCellData cell = board.GetCell(PendingCellIndex);

        if (cell == null)
            return false;

        int flags = LandBuildingFlagsByCell.Get(PendingCellIndex);

        bool initial = PendingAction == PendingActionType.InitialBuildAfterBuy;

        // 재화 조건을 제외한 건설 규칙부터 검사한다.
        // 예: 호텔은 작은집+집+큰집이 있어야 가능.
        bool ruleOk = BulmabulLandSystem.CanBuildIgnoringCash(
            cell,
            flags,
            part,
            initial
        );

        if (!ruleOk)
            return false;

        PlayerGameSlot player = Players.Get(PendingPlayerIndex);

        int cost = BulmabulLandSystem.GetBuildCost(cell, part);

        if (player.cash >= cost)
            return false;

        lackAmount = cost - player.cash;
        return true;
    }

    /// <summary>
    /// 현재 건설 버튼을 클릭 가능하게 둘지 판단한다.
    /// 실제 건설 가능하거나, 건설 규칙은 맞지만 재화만 부족한 경우 true.
    /// 재화 부족 토스트를 띄우기 위해 사용한다.
    /// </summary>
    public bool CanLocalBuildButtonClick(BulmabulBuildPart part)
    {
        if (CanLocalBuild(part))
            return true;

        return TryGetPendingBuildLackAmount(part, out _);
    }

    public bool HasValidCurrentTurn()
    {
        return IsValidAlivePlayer(CurrentTurnIndex);
    }

    public bool IsMyTurn()
    {
        if (Runner == null)
            return false;

        return IsMyTurn(Runner.LocalPlayer);
    }

    public bool CanLocalRollDice()
    {
        if (Runner == null)
            return false;

        if (IsPaused || TurnBusy)
            return false;

        if (PendingAction != PendingActionType.None)
            return false;

        return IsMyTurn(Runner.LocalPlayer);
    }

    public float GetRemainTurnSeconds()
    {
        if (Runner == null)
            return 0f;

        if (IsPaused)
            return Mathf.Max(0f, PausedRemainSeconds);

        float? remain = TurnTimer.RemainingTime(Runner);

        return remain.HasValue ? Mathf.Max(0f, remain.Value) : 0f;
    }

    public bool TryGetPlayerCashText(int playerIndex, out string text)
    {
        text = "-";

        if (playerIndex < 0 || playerIndex >= MaxPlayers)
            return false;

        PlayerGameSlot slot = Players.Get(playerIndex);

        if (slot.occupied == 0)
            return false;

        text = slot.bankrupt ? "파산" : slot.cash.ToString("N0");
        return true;
    }

    public bool ShouldShowBuyPanelForLocalPlayer()
    {
        if (Runner == null)
            return false;

        if (PendingAction != PendingActionType.BuyLand)
            return false;

        if (!IsValidAlivePlayer(PendingPlayerIndex))
            return false;

        PlayerGameSlot slot = Players.Get(PendingPlayerIndex);
        return slot.player == Runner.LocalPlayer;
    }

    public string GetPendingBuyInfoText()
    {
        if (board == null)
            return "구매하시겠습니까?";

        BulmabulCellData cell = board.GetCell(PendingCellIndex);

        if (cell == null)
            return "구매하시겠습니까?";

        return
            $"{cell.cellName}\n" +
            $"구매 가격: {cell.buyCost:N0}\n" +
            $"기본 통행료: {cell.tollCost:N0}\n" +
            $"구매하시겠습니까?";
    }

    public bool CanLocalConfirmBuyLand()
    {
        return ShouldShowBuyPanelForLocalPlayer();
    }

    public bool ShouldShowBuildPanelForLocalPlayer()
    {
        if (Runner == null)
            return false;

        if (PendingAction != PendingActionType.InitialBuildAfterBuy &&
            PendingAction != PendingActionType.BuildFromStart)
            return false;

        if (!IsValidAlivePlayer(PendingPlayerIndex))
            return false;

        PlayerGameSlot slot = Players.Get(PendingPlayerIndex);
        return slot.player == Runner.LocalPlayer;
    }

    public string GetPendingBuildInfoText()
    {
        if (board == null)
            return "건설 정보를 불러올 수 없습니다.";

        if (PendingAction == PendingActionType.InitialBuildAfterBuy)
        {
            BulmabulCellData cell = board.GetCell(PendingCellIndex);

            if (cell == null)
                return "건물을 선택하세요.";

            return
                $"{cell.cellName}\n" +
                $"구매한 땅에 건물을 지을 수 있습니다.\n" +
                $"작은집 비용: {cell.smallHouseBuildCost:N0}\n" +
                $"집 비용: {cell.houseBuildCost:N0}\n" +
                $"작은집과 집을 건설할 수 있습니다.";
        }

        if (PendingAction == PendingActionType.BuildFromStart)
        {
            if (PendingCellIndex < 0)
                return "건설할 내 땅을 선택하세요.";

            BulmabulCellData cell = board.GetCell(PendingCellIndex);

            if (cell == null)
                return "건설할 내 땅을 선택하세요.";

            int flags = LandBuildingFlagsByCell.Get(PendingCellIndex);

            return
                $"{cell.cellName}\n" +
                $"현재 건물: {BulmabulBuildFlags.ToText(flags)}\n" +
                $"작은집: {cell.smallHouseBuildCost:N0}\n" +
                $"집: {cell.houseBuildCost:N0}\n" +
                $"큰집: {cell.bigHouseBuildCost:N0}\n" +
                $"호텔: {cell.hotelBuildCost:N0}";
        }

        return "";
    }

    public bool ShouldShowBuildTargetButton(int cellIndex)
    {
        if (PendingAction != PendingActionType.BuildFromStart)
            return false;

        if (!IsValidAlivePlayer(PendingPlayerIndex))
            return false;

        if (board == null)
            return false;

        if (cellIndex < 0 || cellIndex >= board.CellCount || cellIndex >= MaxCells)
            return false;

        if (LandOwnerByCell.Get(cellIndex) != PendingPlayerIndex)
            return false;

        return CanBuildAnyOnCell(PendingPlayerIndex, cellIndex);
    }

    public bool CanLocalBuild(BulmabulBuildPart part)
    {
        if (!ShouldShowBuildPanelForLocalPlayer())
            return false;

        if (PendingCellIndex < 0)
            return false;

        return CanBuildPart(PendingPlayerIndex, PendingCellIndex, part);
    }

    /// <summary>
    /// 로컬 플레이어가 여행 칸 이동 카드를 사용할 수 있는지 확인.
    /// 
    /// 조건:
    /// - 내 턴
    /// - 일시정지 아님
    /// - 턴 처리 중 아님
    /// - 구매/건설/인수/천사카드 선택 대기 중 아님
    /// - 여행 칸이 보드에 존재함
    /// </summary>
    public bool CanLocalUseMoveToTravelCard()
    {
        if (Runner == null)
            return false;

        if (IsPaused || TurnBusy)
            return false;

        if (PendingAction != PendingActionType.None)
            return false;

        if (!IsMyTurn(Runner.LocalPlayer))
            return false;

        int idx = FindPlayerIndex(Runner.LocalPlayer);
        if (!IsValidAlivePlayer(idx))
            return false;

        if (!LocalHasKeptChanceCard(BulmabulChanceCardType.MoveToTravelCard))
            return false;

        return FindTravelCellIndex() >= 0;
    }

    /// <summary>
    /// 로컬 플레이어가 감옥 탈출 카드를 사용할 수 있는지 확인.
    /// 현재 감옥 칸은 별도 구속 턴 시스템이 없으므로,
    /// 자기 말이 감옥 칸에 있을 때만 카드 사용/소비를 허용한다.
    /// </summary>
    public bool CanLocalUseJailEscapeCard()
    {
        if (Runner == null)
            return false;

        if (IsPaused || TurnBusy)
            return false;

        if (PendingAction != PendingActionType.None)
            return false;

        int idx = FindPlayerIndex(Runner.LocalPlayer);

        if (!IsValidAlivePlayer(idx))
            return false;

        if (!LocalHasKeptChanceCard(BulmabulChanceCardType.JailEscapeCard))
            return false;

        if (board == null || board.CellCount <= 0)
            return false;

        PlayerGameSlot slot = Players.Get(idx);

        if (slot.tileIndex < 0 || slot.tileIndex >= board.CellCount)
            return false;

        BulmabulCellData cell = board.GetCell(slot.tileIndex);

        return cell != null && cell.cellType == BulmabulCellType.Jail;
    }

    /// <summary>
    /// 여행 버튼을 사용할 수 있는지 확인.
    /// 
    /// 이 함수는 보관 카드 사용 여부가 아니라,
    /// 여행 칸에서 비용을 지불한 뒤
    /// 다음 턴에 목적지 선택 버튼을 보여줄지 판단한다.
    /// </summary>
    public bool CanLocalUseTravel()
    {
        return CanLocalSelectTravelDestination();
    }

    /// <summary>
    /// 로컬 플레이어가 여행 목적지를 선택할 수 있는지 확인.
    /// 여행 칸에서 비용을 지불해서 hasTravelDestinationReady가 true일 때만 가능.
    /// </summary>
    public bool CanLocalSelectTravelDestination()
    {
        if (Runner == null)
            return false;

        if (IsPaused || TurnBusy)
            return false;

        if (PendingAction != PendingActionType.None)
            return false;

        if (!IsMyTurn(Runner.LocalPlayer))
            return false;

        int idx = FindPlayerIndex(Runner.LocalPlayer);

        if (!IsValidAlivePlayer(idx))
            return false;

        PlayerGameSlot slot = Players.Get(idx);

        return slot.hasTravelDestinationReady;
    }

    public bool CanLocalPause()
    {
        if (Runner == null)
            return false;

        if (IsPaused)
            return false;

        int idx = FindPlayerIndex(Runner.LocalPlayer);

        if (idx < 0)
            return false;

        PlayerGameSlot slot = Players.Get(idx);

        return slot.pauseUsed < maxPauseCountPerPlayer;
    }

    public int GetLocalPauseRemain()
    {
        if (Runner == null)
            return 0;

        int idx = FindPlayerIndex(Runner.LocalPlayer);

        if (idx < 0)
            return 0;

        PlayerGameSlot slot = Players.Get(idx);

        return Mathf.Max(0, maxPauseCountPerPlayer - slot.pauseUsed);
    }

    public bool IsLocalPauseOwner()
    {
        if (Runner == null)
            return false;

        return PauseOwner == Runner.LocalPlayer;
    }


    public void RequestTakeOverLandLocal()
    {
        if (!ShouldShowTakeOverPanelForLocalPlayer())
            return;

        if (TryGetPendingTakeOverLackAmount(out _))
            return;

        RPC_RequestTakeOverLand();
    }

    /// <summary>
    /// 로컬 플레이어가 천사 카드 사용을 선택한다.
    /// useAngelCard = true  : 천사 카드 사용, 통행료 면제
    /// useAngelCard = false : 천사 카드 사용 안 함, 통행료 지불
    /// </summary>
    public void RequestResolveAngelCardTollLocal(bool useAngelCard)
    {
        if (!ShouldShowAngelCardTollPopupForLocalPlayer())
            return;

        RPC_RequestResolveAngelCardToll(useAngelCard);
    }

    public void RequestSkipTakeOverLandLocal()
    {
        if (!ShouldShowTakeOverPanelForLocalPlayer())
            return;

        RPC_RequestSkipTakeOverLand();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestTakeOverLand(RpcInfo info = default)
    {
        if (!Object.HasStateAuthority)
            return;

        if (PendingAction != PendingActionType.TakeOverLand)
            return;

        if (!IsValidAlivePlayer(PendingPlayerIndex))
            return;

        PlayerGameSlot buyer = Players.Get(PendingPlayerIndex);

        if (info.Source != buyer.player)
            return;

        if (PendingCellIndex < 0 || PendingCellIndex >= MaxCells)
            return;

        int oldOwnerIndex = LandOwnerByCell.Get(PendingCellIndex);

        if (!IsValidAlivePlayer(oldOwnerIndex))
            return;

        if (oldOwnerIndex == PendingPlayerIndex)
            return;

        BulmabulCellData cell = board.GetCell(PendingCellIndex);

        if (cell == null)
            return;

        int flags = LandBuildingFlagsByCell.Get(PendingCellIndex);

        // 호텔이 있으면 인수 불가
        if (!BulmabulLandSystem.CanTakeOver(flags))
            return;

        int cost = BulmabulLandSystem.CalculateTakeOverCost(cell, flags);

        if (buyer.cash < cost)
        {
            LogServer($"{PendingPlayerIndex}번 플레이어가 {cell.cellName} 인수 실패. 재화 부족");
            return;
        }

        PlayerGameSlot oldOwner = Players.Get(oldOwnerIndex);

        // 인수하는 사람은 인수 비용 지불
        buyer.cash -= cost;

        // 기존 소유자는 인수 비용 획득
        oldOwner.cash += cost;

        Players.Set(PendingPlayerIndex, buyer);
        Players.Set(oldOwnerIndex, oldOwner);

        /*
         * 중요:
         * 건물은 그대로 유지한다.
         * LandBuildingFlagsByCell은 건드리지 않는다.
         */
        LandOwnerByCell.Set(PendingCellIndex, PendingPlayerIndex);

        LogServer(
            $"{PendingPlayerIndex}번 플레이어가 {oldOwnerIndex}번 플레이어의 {cell.cellName} 인수 완료. 인수 비용 {cost:N0}"
        );

        int finishedPlayer = PendingPlayerIndex;
        bool wasDouble = PendingWasDouble;

        PendingAction = PendingActionType.None;
        PendingPlayerIndex = -1;
        PendingCellIndex = -1;

        /*
         * 더블이면 같은 플레이어 턴 유지,
         * 더블이 아니면 다음 플레이어 턴으로 이동.
         * 실제 분기는 FinishTurnAfterAction 내부에서 처리.
         */
        FinishTurnAfterAction(finishedPlayer, wasDouble);

        // Flag 색상 변경 / 재화 변경 / 소유권 변경 화면 반영
        BumpRevision();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestResolveAngelCardToll(bool useAngelCard, RpcInfo info = default)
    {
        if (!Object.HasStateAuthority)
            return;

        if (PendingAction != PendingActionType.AngelCardTollChoice)
            return;

        if (!IsValidAlivePlayer(PendingPlayerIndex))
            return;

        PlayerGameSlot payerSlot = Players.Get(PendingPlayerIndex);

        if (info.Source != payerSlot.player)
            return;

        if (board == null)
            return;

        if (PendingCellIndex < 0 || PendingCellIndex >= board.CellCount)
            return;

        BulmabulCellData cell = board.GetCell(PendingCellIndex);

        if (cell == null || cell.cellType != BulmabulCellType.Land)
            return;

        int payerIndex = PendingPlayerIndex;
        int cellIndex = PendingCellIndex;
        bool wasDouble = PendingWasDouble;

        int ownerIndex = LandOwnerByCell.Get(cellIndex);

        if (!IsValidAlivePlayer(ownerIndex))
        {
            LandOwnerByCell.Set(cellIndex, -1);
            LandBuildingFlagsByCell.Set(cellIndex, BulmabulBuildFlags.None);

            PendingAction = PendingActionType.None;
            PendingPlayerIndex = -1;
            PendingCellIndex = -1;

            FinishTurnAfterAction(payerIndex, wasDouble);
            BumpRevision();
            return;
        }

        /*
         * 멀티플레이 핵심:
         * 천사 카드 사용 요청이 들어와도 서버가 실제 보유 여부를 확인해야 한다.
         * 로컬 UI에서 먼저 카드를 제거하면 안 된다.
         */
        if (useAngelCard)
        {
            bool consumed = TryConsumeKeptChanceCardForAuthority(
                payerIndex,
                BulmabulChanceCardType.AngelCard
            );

            if (!consumed)
            {
                LogServer($"{payerIndex}번 플레이어는 천사 카드를 가지고 있지 않습니다. 통행료를 지불합니다.");

                useAngelCard = false;
            }
        }

        if (useAngelCard)
        {
            LogServer($"{payerIndex}번 플레이어가 천사 카드를 사용하여 {cell.cellName} 통행료를 면제받았습니다.");

            PendingAction = PendingActionType.None;
            PendingPlayerIndex = -1;
            PendingCellIndex = -1;

            /*
             * 천사 카드는 통행료만 막는다.
             * 호텔이 없는 땅이면 기존 규칙대로 인수 가능 여부를 열어준다.
             */
            if (CanOpenTakeOverPending(payerIndex, ownerIndex, cellIndex, cell))
            {
                OpenTakeOverPending(payerIndex, cellIndex);
                BumpRevision();
                return;
            }

            FinishTurnAfterAction(payerIndex, wasDouble);
            BumpRevision();
            return;
        }

        /*
         * 천사 카드 사용 안 함 또는 서버 검증 실패.
         * 기존처럼 통행료를 낸다.
         */
        bool canContinue = PayToll(payerIndex, ownerIndex, cellIndex, cell);

        PendingAction = PendingActionType.None;
        PendingPlayerIndex = -1;
        PendingCellIndex = -1;

        if (!canContinue)
        {
            FinishTurnAfterAction(payerIndex, wasDouble);
            BumpRevision();
            return;
        }

        if (CanOpenTakeOverPending(payerIndex, ownerIndex, cellIndex, cell))
        {
            OpenTakeOverPending(payerIndex, cellIndex);
            BumpRevision();
            return;
        }

        FinishTurnAfterAction(payerIndex, wasDouble);
        BumpRevision();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestSkipTakeOverLand(RpcInfo info = default)
    {
        if (!Object.HasStateAuthority)
            return;

        if (PendingAction != PendingActionType.TakeOverLand)
            return;

        if (!IsValidAlivePlayer(PendingPlayerIndex))
            return;

        PlayerGameSlot player = Players.Get(PendingPlayerIndex);

        if (info.Source != player.player)
            return;

        int finishedPlayer = PendingPlayerIndex;
        bool wasDouble = PendingWasDouble;

        LogServer($"{finishedPlayer}번 플레이어가 인수하지 않고 턴을 넘겼습니다.");

        PendingAction = PendingActionType.None;
        PendingPlayerIndex = -1;
        PendingCellIndex = -1;

        /*
         * 더블이면 같은 플레이어 턴 유지,
         * 더블이 아니면 다음 플레이어 턴으로 이동.
         */
        FinishTurnAfterAction(finishedPlayer, wasDouble);

        BumpRevision();
    }

    public bool ShouldShowTakeOverPanelForLocalPlayer()
    {
        if (Runner == null)
            return false;

        if (PendingAction != PendingActionType.TakeOverLand)
            return false;

        if (!IsValidAlivePlayer(PendingPlayerIndex))
            return false;

        PlayerGameSlot slot = Players.Get(PendingPlayerIndex);
        return slot.player == Runner.LocalPlayer;
    }

    /// <summary>
    /// 로컬 플레이어가 천사 카드 통행료 선택 팝업을 봐야 하는지 확인.
    /// </summary>
    public bool ShouldShowAngelCardTollPopupForLocalPlayer()
    {
        if (Runner == null)
            return false;

        if (PendingAction != PendingActionType.AngelCardTollChoice)
            return false;

        if (!IsValidAlivePlayer(PendingPlayerIndex))
            return false;

        PlayerGameSlot slot = Players.Get(PendingPlayerIndex);

        return slot.player == Runner.LocalPlayer;
    }

    /// <summary>
    /// 천사 카드 팝업에 표시할 정보.
    /// </summary>
    public string GetPendingAngelCardTollInfoText()
    {
        if (board == null)
            return GetByLanguageForState(
                "상대 땅에 도착했습니다.\n천사 카드를 사용하시겠습니까?",
                "You landed on an opponent's land.\nUse Angel Card?"
            );

        if (PendingCellIndex < 0 || PendingCellIndex >= board.CellCount)
            return GetByLanguageForState(
                "상대 땅에 도착했습니다.\n천사 카드를 사용하시겠습니까?",
                "You landed on an opponent's land.\nUse Angel Card?"
            );

        BulmabulCellData cell = board.GetCell(PendingCellIndex);

        if (cell == null)
            return GetByLanguageForState(
                "상대 땅에 도착했습니다.\n천사 카드를 사용하시겠습니까?",
                "You landed on an opponent's land.\nUse Angel Card?"
            );

        int ownerIndex = LandOwnerByCell.Get(PendingCellIndex);
        int flags = LandBuildingFlagsByCell.Get(PendingCellIndex);
        int toll = BulmabulLandSystem.CalculateToll(cell, flags);

        string ownerName = IsValidAlivePlayer(ownerIndex)
            ? GetPlayerDisplayName(ownerIndex)
            : GetByLanguageForState("알 수 없음", "Unknown");

        return GetByLanguageForState(
            $"{cell.cellName}\n소유자: {ownerName}\n통행료: {toll:N0}\n천사 카드를 사용하시겠습니까?",
            $"{cell.cellName}\nOwner: {ownerName}\nToll: {toll:N0}\nUse Angel Card?"
        );
    }

    public string GetPendingTakeOverInfoText()
    {
        if (board == null)
            return GetByLanguageForState("인수하시겠습니까?", "Take over this land?");

        if (PendingCellIndex < 0 || PendingCellIndex >= board.CellCount)
            return GetByLanguageForState("인수하시겠습니까?", "Take over this land?");

        BulmabulCellData cell = board.GetCell(PendingCellIndex);

        if (cell == null)
            return GetByLanguageForState("인수하시겠습니까?", "Take over this land?");

        int flags = LandBuildingFlagsByCell.Get(PendingCellIndex);
        int cost = BulmabulLandSystem.CalculateTakeOverCost(cell, flags);

        return GetByLanguageForState(
            $"{cell.cellName}\n인수 비용: {cost:N0}\n이 땅을 인수하시겠습니까?",
            $"{cell.cellName}\nTake Over Cost: {cost:N0}\nDo you want to take over this land?"
        );
    }

    public bool TryGetPendingTakeOverLackAmount(out int lackAmount)
    {
        lackAmount = 0;

        if (!ShouldShowTakeOverPanelForLocalPlayer())
            return false;

        if (!IsValidAlivePlayer(PendingPlayerIndex))
            return false;

        if (board == null)
            return false;

        if (PendingCellIndex < 0 || PendingCellIndex >= board.CellCount)
            return false;

        BulmabulCellData cell = board.GetCell(PendingCellIndex);

        if (cell == null)
            return false;

        int flags = LandBuildingFlagsByCell.Get(PendingCellIndex);
        int cost = BulmabulLandSystem.CalculateTakeOverCost(cell, flags);

        PlayerGameSlot buyer = Players.Get(PendingPlayerIndex);

        if (buyer.cash >= cost)
            return false;

        lackAmount = cost - buyer.cash;
        return true;
    }

    private string GetByLanguageForState(string kor, string eng)
    {
        if (LaguageManager.Instance == null)
            return kor;

        return LaguageManager.Instance.currentLang == Lauaguage.Eng ? eng : kor;
    }

    /// <summary>
    /// 현재 일시정지한 플레이어의 닉네임을 반환한다.
    /// PauseOwner는 PlayerRef이므로 FindPlayerIndex로 슬롯 인덱스를 찾은 뒤,
    /// Players 슬롯에서 nickname을 가져온다.
    /// </summary>
    public string GetPauseOwnerNickname()
    {
        if (PauseOwner == PlayerRef.None)
            return GetByLanguage("알 수 없는 플레이어", "Unknown Player");

        int ownerIndex = FindPlayerIndex(PauseOwner);

        if (ownerIndex < 0)
            return GetByLanguage("알 수 없는 플레이어", "Unknown Player");

        PlayerGameSlot slot = Players.Get(ownerIndex);

        string nickname = slot.nickname.ToString();

        if (!string.IsNullOrWhiteSpace(nickname))
            return nickname;

        return GetByLanguage(
            $"플레이어 {ownerIndex + 1}",
            $"Player {ownerIndex + 1}"
        );
    }

    public bool TryGetPlayerViewData(
    int playerIndex,
    out string nickname,
    out int level,
    out int cash,
    out int teamSideInt,
    out int turnOrder,
    out bool isLeader,
    out bool isBankrupt,
    out string photoUrl)
    {
        nickname = "";
        level = 1;
        cash = 0;
        teamSideInt = (int)TeamSide.None;
        turnOrder = 0;
        isLeader = false;
        isBankrupt = false;
        photoUrl = "";

        if (playerIndex < 0 || playerIndex >= MaxPlayers)
            return false;

        PlayerGameSlot slot = Players.Get(playerIndex);

        if (slot.occupied == 0)
            return false;

        nickname = slot.nickname.ToString();

        if (string.IsNullOrWhiteSpace(nickname))
            nickname = $"Player {playerIndex + 1}";

        level = Mathf.Max(1, slot.level);
        cash = slot.cash;
        teamSideInt = slot.teamSideInt;
        turnOrder = slot.turnOrder;
        isLeader = slot.isLeader;
        isBankrupt = slot.bankrupt;
        photoUrl = slot.photoUrl.ToString();

        return true;
    }

    public string GetCurrentTurnPlayerName()
    {
        if (!IsValidAlivePlayer(CurrentTurnIndex))
            return "Player";

        PlayerGameSlot slot = Players.Get(CurrentTurnIndex);

        string nick = slot.nickname.ToString();

        if (string.IsNullOrWhiteSpace(nick))
            nick = $"Player {CurrentTurnIndex + 1}";

        return nick;
    }

    public bool TryGetPlayerPhotoUrl(int playerIndex, out string photoUrl)
    {
        photoUrl = "";

        if (playerIndex < 0 || playerIndex >= MaxPlayers)
            return false;

        PlayerGameSlot slot = Players.Get(playerIndex);

        if (slot.occupied == 0)
            return false;

        photoUrl = slot.photoUrl.ToString();

        return true;
    }

    /// <summary>
    /// 현재 언어에 따라 한국어/영어 문구를 반환한다.
    /// LaguageManager가 없으면 한국어를 기본값으로 사용한다.
    /// </summary>
    private string GetByLanguage(string kor, string eng)
    {
        if (LaguageManager.Instance == null)
            return kor;

        return LaguageManager.Instance.currentLang == Lauaguage.Eng ? eng : kor;
    }


    /// <summary>
    /// 주사위 UI가 결과를 보여주고 꺼질 때까지 기다릴 시간.
    /// DiceRollingUI가 연결되어 있으면 그 값을 사용하고,
    /// 없으면 기본값을 사용한다.
    /// </summary>
    private float GetDicePresentationWaitSeconds()
    {
        if (diceRollingUI != null)
            return Mathf.Max(0.1f, diceRollingUI.TotalPresentationSeconds);

        // DiceRollingUI가 연결되지 않은 경우 fallback.
        // 최소 굴림 + 낙하 + 결과 표시를 포함한 대략적인 시간.
        return 2.8f;
    }

    private string GetPlayerDisplayName(int playerIndex)
    {
        if (playerIndex < 0 || playerIndex >= MaxPlayers)
            return GetByLanguage("알 수 없는 플레이어", "Unknown Player");

        PlayerGameSlot slot = Players.Get(playerIndex);

        string nickname = slot.nickname.ToString();

        if (!string.IsNullOrWhiteSpace(nickname))
            return nickname;

        return GetByLanguage(
            $"플레이어 {playerIndex + 1}",
            $"Player {playerIndex + 1}"
        );
    }
    #endregion

    #region 헬퍼

    private bool IsMyTurn(PlayerRef player)
    {
        if (player == PlayerRef.None)
            return false;

        if (!IsValidAlivePlayer(CurrentTurnIndex))
            return false;

        return Players.Get(CurrentTurnIndex).player == player;
    }

    private int FindPlayerIndex(PlayerRef player)
    {
        for (int i = 0; i < MaxPlayers; i++)
        {
            PlayerGameSlot s = Players.Get(i);

            if (s.occupied == 1 && s.player == player)
                return i;
        }

        return -1;
    }

    private int FindFirstAlivePlayerIndex()
    {
        for (int i = 0; i < MaxPlayers; i++)
        {
            if (IsValidAlivePlayer(i))
                return i;
        }

        return 0;
    }

    private bool IsValidAlivePlayer(int index)
    {
        if (index < 0 || index >= MaxPlayers)
            return false;

        PlayerGameSlot s = Players.Get(index);

        return s.occupied == 1 && s.player != PlayerRef.None && !s.bankrupt;
    }

    private int GetAlivePlayerCount()
    {
        int count = 0;

        for (int i = 0; i < MaxPlayers; i++)
        {
            if (IsValidAlivePlayer(i))
                count++;
        }

        return count;
    }

    private void LogServer(string msg)
    {
        Debug.Log("[BulmabulGameState] " + msg);

        if (Object.HasStateAuthority)
            LastLogMessage = msg;
    }

    private void BumpRevision()
    {
        if (!Object.HasStateAuthority)
            return;

        Revision++;
    }

    /// <summary>
    /// UI가 최근 주사위 결과를 가져갈 때 사용한다.
    /// rollVersion은 같은 결과를 중복 처리하지 않기 위한 번호다.
    /// </summary>
    public bool TryGetLatestDiceResultForLocalPlayer(out int rollVersion, out int dice1, out int dice2)
    {
        rollVersion = _lastRollVersion;
        dice1 = _lastDice1;
        dice2 = _lastDice2;

        return _lastRollVersion > 0;
    }

    /// <summary>
    /// 주사위 결과가 최종 확정되었을 때 호출한다.
    /// UI가 최근 주사위 결과를 읽을 수 있도록 저장한다.
    /// </summary>
    private void OnDiceRollResolved(int finalDice1, int finalDice2)
    {
        _lastRollVersion++;
        _lastDice1 = finalDice1;
        _lastDice2 = finalDice2;
    }
    #endregion

    /// <summary>
    /// 로컬 플레이어가 현재 게임의 승자인지 확인한다.
    /// UI에서 승리 보상 지급 / 자동 퇴장 처리에 사용한다.
    /// </summary>
    public bool IsLocalPlayerWinner()
    {
        if (!IsSpawnReady)
            return false;

        if (Runner == null)
            return false;

        if (!GameFinished)
            return false;

        int localIndex = FindPlayerIndex(Runner.LocalPlayer);

        return localIndex >= 0 && localIndex == WinnerIndex;
    }

    /// <summary>
    /// 현재 승리자 닉네임 반환.
    /// </summary>
    public string GetWinnerNickname()
    {
        if (WinnerIndex < 0 || WinnerIndex >= MaxPlayers)
            return GetByLanguage("승리자", "Winner");

        PlayerGameSlot winner = Players.Get(WinnerIndex);

        string nick = winner.nickname.ToString();

        if (string.IsNullOrWhiteSpace(nick))
            nick = $"Player {WinnerIndex + 1}";

        return nick;
    }

    #region 플레이어 나가기 / 강제 종료 / 승자 판정

    public enum LeaveReasonType
    {
        None = 0,
        ExitButton = 1,
        Disconnected = 2
    }

    /// <summary>
    /// 로컬 플레이어가 게임 나가기를 확정했을 때 호출.
    /// Exit 버튼으로 나가는 경우.
    /// </summary>
    public void RequestLeaveGameLocal()
    {
        if (Runner == null)
            return;

        RPC_RequestLeaveGame(Runner.LocalPlayer, (int)LeaveReasonType.ExitButton);
    }

    /// <summary>
    /// 클라이언트가 "나가겠다"고 서버에게 요청.
    /// 실제 처리는 StateAuthority에서만 한다.
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestLeaveGame(PlayerRef leaver, int reasonInt, RpcInfo info = default)
    {
        if (!Object.HasStateAuthority)
            return;

        // 다른 사람이 남을 강제로 나가게 요청하지 못하게 방어
        if (info.Source != leaver)
            return;

        ForcePlayerLeaveDefeat(leaver, (LeaveReasonType)reasonInt);
    }

    /// <summary>
    /// NetWorkLauncher.OnPlayerLeft에서 호출할 함수.
    /// Alt+F4, 에디터 Stop, 네트워크 끊김 등으로 실제 연결이 끊겼을 때 사용.
    /// </summary>
    public void Server_HandlePlayerDisconnected(PlayerRef leaver)
    {
        if (!Object.HasStateAuthority)
            return;

        ForcePlayerLeaveDefeat(leaver, LeaveReasonType.Disconnected);
    }

    /// <summary>
    /// 플레이어를 "중도 이탈 패배"로 처리한다.
    /// 일반 파산 패배와 구분된다.
    /// </summary>
    private void ForcePlayerLeaveDefeat(PlayerRef leaver, LeaveReasonType reason)
    {
        if (!Object.HasStateAuthority)
            return;

        if (GameFinished)
            return;

        int playerIndex = FindPlayerIndex(leaver);

        if (playerIndex < 0)
            return;

        PlayerGameSlot slot = Players.Get(playerIndex);

        // 이미 파산/패배 처리된 플레이어면 중복 처리 방지
        if (slot.bankrupt)
            return;

        slot.bankrupt = true;
        slot.leftGame = true;
        slot.leaveReasonInt = (int)reason;
        slot.hasTravelDestinationReady = false;
        slot.hasAngelCard = false;
        slot.hasJailEscapeCard = false;
        slot.hasTravelCard = false;
        slot.travelCost = 0;

        Players.Set(playerIndex, slot);

        // 이탈자가 선택 대기 중이었다면 대기 상태 제거
        if (PendingPlayerIndex == playerIndex)
        {
            PendingAction = PendingActionType.None;
            PendingPlayerIndex = -1;
            PendingCellIndex = -1;
            PendingWasDouble = false;
        }

        string nick = slot.nickname.ToString();

        if (string.IsNullOrWhiteSpace(nick))
            nick = $"Player {playerIndex + 1}";

        if (reason == LeaveReasonType.ExitButton)
        {
            LastLogMessage = $"{nick}님이 게임을 나가 이탈 패배 처리되었습니다.";
        }
        else
        {
            LastLogMessage = $"{nick}님의 연결이 끊겨 이탈 패배 처리되었습니다.";
        }

        Debug.Log($"[BulmabulGameState] Leave defeat. player={leaver}, index={playerIndex}, reason={reason}");

        // 현재 턴 플레이어가 나간 경우 턴 진행 막고 다음 생존자에게 넘김
        if (CurrentTurnIndex == playerIndex)
        {
            TurnBusy = false;

            if (GetAlivePlayerCount() > 1)
                AdvanceTurn();
        }

        // 남은 플레이어가 1명이면 승리 처리
        CheckWinnerByRemainingPlayers();

        BumpRevision();
    }

    /// <summary>
    /// 살아있는 플레이어가 1명만 남으면 그 플레이어 승리.
    /// </summary>
    private void CheckWinnerByRemainingPlayers()
    {
        if (!Object.HasStateAuthority)
            return;

        if (GameFinished)
            return;

        int aliveCount = 0;
        int winnerIndex = -1;

        for (int i = 0; i < MaxPlayers; i++)
        {
            if (!IsValidAlivePlayer(i))
                continue;

            aliveCount++;
            winnerIndex = i;
        }

        if (aliveCount == 1 && winnerIndex >= 0)
        {
            FinishGameByWinner(winnerIndex);
        }
    }

    /// <summary>
    /// 게임 종료 및 승자 확정.
    /// </summary>
    private void FinishGameByWinner(int winnerIndex)
    {
        if (!Object.HasStateAuthority)
            return;

        if (GameFinished)
            return;

        GameFinished = true;
        WinnerIndex = winnerIndex;

        TurnBusy = true;
        PendingAction = PendingActionType.None;
        PendingPlayerIndex = -1;
        PendingCellIndex = -1;
        PendingWasDouble = false;
        IsPaused = false;

        PlayerGameSlot winner = Players.Get(winnerIndex);

        string winnerNick = winner.nickname.ToString();

        if (string.IsNullOrWhiteSpace(winnerNick))
            winnerNick = $"Player {winnerIndex + 1}";

        LastLogMessage = $"{winnerNick}님이 승리했습니다.";

        Debug.Log($"[BulmabulGameState] Game Finished. WinnerIndex={winnerIndex}, Winner={winnerNick}");

        BumpRevision();
    }

    #endregion

    #region 멀티 플레이 카드 덱 초기화

    /// <summary>
    /// Photon Fusion 멀티플레이용 찬스 카드 덱 초기화.
    /// 반드시 StateAuthority에서만 호출한다.
    /// </summary>
    private void InitChanceDeckForFusion()
    {
        if (!Object.HasStateAuthority)
            return;

        BulmabulChanceDeck deck = BulmabulChanceDeck.Instance;

        if (deck == null)
        {
            LogServer("BulmabulChanceDeck을 찾을 수 없습니다.");
            return;
        }

        int seed = System.Guid.NewGuid().GetHashCode();

        ChanceDeckSeed = seed;
        ChanceDeckRemainCount = deck.ResetDeckForAuthority(seed);
        ChanceDeckInitialized = true;

        RPC_SyncChanceDeckUI(ChanceDeckRemainCount);

        LogServer($"찬스 카드 덱 초기화 완료. seed={seed}, remain={ChanceDeckRemainCount}");
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_SyncChanceDeckUI(int remainCount)
    {
        if (BulmabulChanceDeck.Instance != null)
            BulmabulChanceDeck.Instance.SetCardCountFromServer(remainCount);
    }

    #endregion

    #region 보관 찬스 카드 네트워크 상태

    /// <summary>
    /// StateAuthority에서 보관 카드를 플레이어에게 지급한다.
    /// 플레이어당 같은 보관 카드는 1장만 가질 수 있다.
    /// </summary>
    public bool TryGiveKeptChanceCardForAuthority(int playerIndex, BulmabulChanceCardData card)
    {
        if (!Object.HasStateAuthority)
            return false;

        if (!IsValidAlivePlayer(playerIndex))
            return false;

        if (card == null)
            return false;

        PlayerGameSlot slot = Players.Get(playerIndex);

        switch (card.cardType)
        {
            case BulmabulChanceCardType.AngelCard:
                if (slot.hasAngelCard)
                    return false;

                slot.hasAngelCard = true;
                Players.Set(playerIndex, slot);
                BumpRevision();
                return true;

            case BulmabulChanceCardType.JailEscapeCard:
                if (slot.hasJailEscapeCard)
                    return false;

                slot.hasJailEscapeCard = true;
                Players.Set(playerIndex, slot);
                BumpRevision();
                return true;

            case BulmabulChanceCardType.MoveToTravelCard:
                if (slot.hasTravelCard)
                    return false;

                slot.hasTravelCard = true;
                Players.Set(playerIndex, slot);
                BumpRevision();
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// StateAuthority에서 보관 카드를 소비한다.
    /// 실제 효과 실행 직전에 호출해야 한다.
    /// </summary>
    public bool TryConsumeKeptChanceCardForAuthority(int playerIndex, BulmabulChanceCardType type)
    {
        if (!Object.HasStateAuthority)
            return false;

        if (!IsValidAlivePlayer(playerIndex))
            return false;

        PlayerGameSlot slot = Players.Get(playerIndex);
        bool consumed = false;

        switch (type)
        {
            case BulmabulChanceCardType.AngelCard:
                if (!slot.hasAngelCard)
                    return false;

                slot.hasAngelCard = false;
                consumed = true;
                break;

            case BulmabulChanceCardType.JailEscapeCard:
                if (!slot.hasJailEscapeCard)
                    return false;

                slot.hasJailEscapeCard = false;
                consumed = true;
                break;

            case BulmabulChanceCardType.MoveToTravelCard:
                if (!slot.hasTravelCard)
                    return false;

                slot.hasTravelCard = false;
                consumed = true;
                break;
        }

        if (!consumed)
            return false;

        Players.Set(playerIndex, slot);
        BumpRevision();
        return true;
    }

    /// <summary>
    /// 로컬 플레이어가 특정 보관 카드를 가지고 있는지 확인.
    /// UI는 이 함수로 자기 카드만 보여주면 된다.
    /// </summary>
    public bool LocalHasKeptChanceCard(BulmabulChanceCardType type)
    {
        if (Runner == null)
            return false;

        int idx = FindPlayerIndex(Runner.LocalPlayer);

        if (!IsValidAlivePlayer(idx))
            return false;

        PlayerGameSlot slot = Players.Get(idx);

        switch (type)
        {
            case BulmabulChanceCardType.AngelCard:
                return slot.hasAngelCard;

            case BulmabulChanceCardType.JailEscapeCard:
                return slot.hasJailEscapeCard;

            case BulmabulChanceCardType.MoveToTravelCard:
                return slot.hasTravelCard;

            default:
                return false;
        }
    }

    #endregion
}