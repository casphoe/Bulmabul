using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Globalization;
using System.Collections;
using System.Threading.Tasks;

[Serializable]
/// <summary>
/// 방 리스트 정렬 모드
/// - Alpha : 가나다(한글->영어->숫자) 정렬
/// - Latest: 최신순(생성시간 createdAt 내림차순)
/// </summary>
public enum RoomSortMode
{
    Alpha = 0,   // 가나다(한글->영어->숫자)
    Latest = 1,  // 최신(생성시간 내림차순)
}

[Serializable]
//방이 개인전인지 팀전인지 나누는 enum
public enum MatchMode
{
    Solo = 0,
    Team = 1
}

/// <summary>
/// Photon Fusion 2용 네트워크 런처(방 생성/참가/나가기)
/// - Host: 방 생성(세션 생성)
/// - Client: 방 참가(세션 참가)
/// 
/// 중요:
/// 1) Photon은 "방이 여러 개" 동시에 존재 가능.
/// 2) 하지만 "한 게임 실행(한 NetworkRunner)"은 동시에 하나의 방만 들어갈 수 있음.
///    -> 다른 방 만들기/참가하려면 기존 방에서 나가야 함(Shutdown/Reset).
/// </summary>
public class NetWorkLauncher : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("Room")]
    public string roomName = "BulmabulRoom";

    [Header("표시용: 현재 방 최대 인원(실제 적용은 모드에 따라 강제 보정됨)")]
    public int maxPlayers = 4;

    [Header("표시용: 현재 방 인원")]
    public int playerCount = 0;

    [Header("표시용: 현재 방 모드")]
    public MatchMode currentMode = MatchMode.Solo;

    [Header("방 선택 인덱스")]
    public int selectindex = 0;

    [Header("방 정렬 모드")]
    public RoomSortMode sortMode = RoomSortMode.Alpha;

    [Header("Game Scene (Fusion 네트워크 로드용)")]
    [SerializeField] private SceneRef gameScene;

    [Header("Lobby Scene Name (언로드용)")]
    [SerializeField] private string lobbySceneName = "LobbyScene"; // 너 로비씬 이름으로

    #region 방 리스트
    [Header("Lobby(방 리스트)")]
    [SerializeField] public SessionLobby lobby = SessionLobby.ClientServer; // 보통 Shared 사용

    public List<RoomPrefabData> roomPrefabList = new List<RoomPrefabData>();

    private bool _resetting;

    private bool _joinedLobby;

    // 로비에서 받은 방(세션) 리스트를 여기 저장
    public List<SessionInfo> _cachedSessions = new List<SessionInfo>();

    public IReadOnlyList<SessionInfo> CachedSessions => _cachedSessions;
    public int RoomCount => _cachedSessions.Count;

    public NetworkRunner Runner => _runner;

    /// <summary>
    /// 방 리스트가 갱신되면 UI가 다시 로드할 수 있도록 이벤트 발생
    /// LobbyUIManager.OnEnable에서 구독해서 roomLoad() 호출하는 구조
    /// </summary>
    public event Action<IReadOnlyList<SessionInfo>> OnRoomsUpdated;
    #endregion

    private NetworkRunner _runner;
    private NetworkSceneManagerDefault _sceneManager;
    private bool _starting;

    public static NetWorkLauncher instance;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        selectindex = -1;
        DontDestroyOnLoad(gameObject);
        CreateRunnerOnce();
    }

    /// <summary>
    /// Runner는 "반드시 한 번만" 만들기.
    /// - 중복 AddComponent 하면 이상 증상(콜백 2번, 세션 꼬임) 생김
    /// </summary>
    private void CreateRunnerOnce()
    {
        // 파괴 중이면 아무 것도 하지 않음
        if (!this || gameObject == null) return;

        // 이미 붙어있는 Runner가 있으면 그걸 잡아온다 (중복 AddComponent 방지)
        if (_runner == null)
            _runner = GetComponent<NetworkRunner>();

        if (_runner == null)
            _runner = gameObject.AddComponent<NetworkRunner>();

        _runner.ProvideInput = true;

        // 콜백 중복 등록 방지 (Fusion 버전에 따라 RemoveCallbacks가 없을 수도 있음)
        // 있으면 쓰고, 컴파일 에러 나면 이 줄만 지워도 됨
        _runner.RemoveCallbacks(this);
        _runner.AddCallbacks(this);

        //  SceneManager도 마찬가지로 재사용
        if (_sceneManager == null)
            _sceneManager = GetComponent<NetworkSceneManagerDefault>();

        if (_sceneManager == null)
            _sceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>();
    }

    /// <summary>
    /// 현재 방에서 나가기(러너 리셋)
    /// - 주의: NetworkRunner.Shutdown()의 destroyGameObject 기본값이 true면
    ///         Runner가 붙은 게임오브젝트가 파괴될 수 있음.
    ///         우리는 런처 오브젝트를 유지해야 하니 destroyGameObject:false로 종료.
    /// </summary>
    private async void ResetRunner()
    {
        if (_resetting) return;
        _resetting = true;

        try
        {
            if (_runner != null && _runner.IsRunning)
                await _runner.Shutdown(destroyGameObject: false);

            if (_runner != null)
                Destroy(_runner);

            _runner = null;

            // 오브젝트가 파괴 중이면 더 진행하지 않기
            if (this == null) return;

            CreateRunnerOnce();

            _starting = false;
            playerCount = 0;
            _joinedLobby = false;
        }
        finally
        {
            _resetting = false;
        }
    }


    #region UI 버튼
    public void SetRoomName(string name)
    {
        roomName = NormalizeRoomName(name);
    }


    /// <summary>
    /// 개인전 방 만들기(Host)
    /// - 방 최대 인원: 최소 2 이상 (1명 방 금지)
    /// - 이름이 이미 있으면 자동으로 _1234 붙여서 새 이름으로 생성 재시도
    /// </summary>
    public void OnClickCreateRoom_Solo()
    {
        if (_starting) return;
        currentMode = MatchMode.Solo;

        StartHostWithAutoRename(MatchMode.Solo);
    }

    /// <summary>
    /// 팀전 방 만들기(Host)
    /// - 팀전은 무조건 4명 고정
    /// - 이름 충돌 시 자동 rename 후 재시도
    /// </summary>
    public void OnClickCreateRoom_Team()
    {
        if (_starting) return;
        currentMode = MatchMode.Team;

        StartHostWithAutoRename(MatchMode.Team);
    }

    /// <summary>
    /// 참가하기(Client)
    /// - 입력한 roomName으로 참가 시도
    /// - 방이 없으면 GameNotFound로 실패할 수 있음 (로그 확인)
    /// </summary>
    public void OnClickJoinRoom()
    {
        if (_starting) return;
        StartGame(GameMode.Client, NormalizeRoomName(roomName), currentMode);
    }

    /// <summary>
    /// 현재 방 나가기
    /// </summary>
    public void OnClickLeaveRoom()
    {
        ResetRunner();
        Debug.Log("[Fusion] Left room (runner reset).");
    }

    #endregion

    #region 방 이름 정리
    /// <summary>
    /// 방 이름 정리:
    /// - null/공백이면 기본값
    /// - 앞뒤 공백 제거
    /// </summary>
    private string NormalizeRoomName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "BulmabulRoom";
        return name.Trim();
    }

    /// <summary>
    /// "이름 충돌"로 Host 생성이 실패했을 때, suffix를 붙여 유니크한 이름을 만든다.
    /// </summary>
    private string MakeUniqueRoomName(string baseName)
    {
        // 예: MyRoom_4821
        int suffix = UnityEngine.Random.Range(1000, 9999);
        return $"{baseName}_{suffix}";
    }
    #endregion

    #region 방 모드에 따라서 최대 인원 강제 보정하는 기능
    /// <summary>
    /// 모드에 따른 "최대 인원 강제 보정"
    /// - Team: 무조건 4
    /// - Solo: 최소 2 이상, 최대 4 이하로 보정
    /// </summary>
    private int GetMaxPlayersByMode(MatchMode mode)
    {
        if (mode == MatchMode.Team)
            return 4;

        // Solo는 최소 2명. (원하면 2~4 사이로만)
        int clamped = Mathf.Clamp(maxPlayers, 2, 4);
        return clamped;
    }
    #endregion


    #region 로비 참가
    /// <summary>
    /// 로비 참가(방 리스트 수신 받기 시작)
    /// - LobbyUIManager.Start()에서 1회 호출해주면 됨
    /// - _joinedLobby 체크로 중복 참가 방지
    /// </summary>
    public async void JoinLobbyIfNeeded()
    {
        CreateRunnerOnce();
        if (_runner == null) return;
        if (_joinedLobby) return;

        var res = await _runner.JoinSessionLobby(lobby);
        _joinedLobby = res.Ok;

        Debug.Log(_joinedLobby
            ? $"[Fusion] Joined Lobby: {lobby}"
            : $"[Fusion] JoinLobby Failed: {res.ShutdownReason}");
    }
    #endregion

    #region UI 리스트에서 방 참가
    /// <summary>
    /// RoomPrefab에서 Join 버튼 누를 때 호출하도록 쓰는 함수
    /// - roomIndex는 "현재 CachedSessions의 인덱스"여야 함
    /// - 따라서 CachedSessions가 정렬되면, UI index도 그 정렬 결과 기준으로 맞춰야 함
    /// </summary>
    public void JoinRoomByIndex(int roomIndex)
    {
        if (_starting) return;
        if (roomIndex < 0 || roomIndex >= _cachedSessions.Count) return;

        var s = _cachedSessions[roomIndex];
        string sessionName = s.Name;

        // 방 모드 읽기(없으면 현재값)
        MatchMode mode = currentMode;
        if (s.Properties != null && s.Properties.TryGetValue("mode", out var pm))
            mode = (MatchMode)(int)pm;

        StartGame(GameMode.Client, sessionName, mode);
    }
    #endregion

    #region 방이 가득 찼는지 판단(가득 찬 방 숨김)
    /// <summary>
    /// 세션 최대 인원값 얻기
    /// - Fusion SessionInfo.MaxPlayers가 정상일 때가 많지만
    /// - 혹시 0으로 오면 우리가 심어둔 SessionProperties["max"]로 보정
    /// </summary>
    private int GetSessionMax(SessionInfo s)
    {
        int max = s.MaxPlayers; // 보통 이게 들어옴
        if (max <= 0 && s.Properties != null && s.Properties.TryGetValue("max", out var px))
            max = (int)px; // 너가 넣어둔 백업
        return max;
    }
    /// <summary>
    /// 현재 인원이 max에 도달했으면 full 처리
    /// </summary>
    private bool IsSessionFull(SessionInfo s)
    {
        int max = GetSessionMax(s);
        if (max <= 0) return false; // max를 모르면 일단 숨기지 않음(보수적으로)
        return s.PlayerCount >= max;
    }

    #endregion

    #region 방 맵 선택

    [Header("방 맵 선택 (0=Korea, 1=USA)")]
    public int selectedMap = 0;

    public void SetSelectedMap(int map)
    {
        selectedMap = Mathf.Clamp(map, 0, 1);
    }

    #endregion


    #region 방 정렬하기 

    private readonly Dictionary<string, int> _firstSeenOrder = new Dictionary<string, int>();
    private int _firstSeenCounter = 0;


    private int GetSessionCreatedAt(SessionInfo s)
    {
        // 1) createdAt 프로퍼티(초) 우선
        if (s.Properties != null && s.Properties.TryGetValue("createdAt", out var ca))
            return (int)ca;

        // 2) 없으면 "처음 본 순서"로 대체
        if (!_firstSeenOrder.TryGetValue(s.Name, out int order))
        {
            order = ++_firstSeenCounter;
            _firstSeenOrder[s.Name] = order;
        }
        return order;
    }

    // 한글 / 영어 / 숫자 순서용 카테고리
    private int GetNameCategory(string name)
    {
        if (string.IsNullOrEmpty(name)) return 3;
        char c = name[0];

        if (c >= '가' && c <= '힣') return 0;                 // 한글
        if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) return 1; // 영어
        if (char.IsDigit(c)) return 2;                        // 숫자
        return 3;                                             // 기타
    }

    private int CompareRoomName(string a, string b)
    {
        a ??= "";
        b ??= "";

        int ca = GetNameCategory(a);
        int cb = GetNameCategory(b);
        if (ca != cb) return ca.CompareTo(cb);

        // 같은 카테고리 내부 정렬
        if (ca == 0)
        {
            // 한글: ko-KR 정렬(가나다)
            return string.Compare(a, b, CultureInfo.GetCultureInfo("ko-KR"),
                CompareOptions.StringSort);
        }
        if (ca == 1)
        {
            // 영어: 대소문자 무시
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }
        if (ca == 2)
        {
            // 숫자: 가능한 숫자로 비교
            bool pa = int.TryParse(a, out int ia);
            bool pb = int.TryParse(b, out int ib);
            if (pa && pb) return ia.CompareTo(ib);
            // 숫자가 섞인 문자열이면 그냥 문자열 비교
            return string.Compare(a, b, StringComparison.Ordinal);
        }

        // 기타는 그냥 문자열
        return string.Compare(a, b, StringComparison.Ordinal);
    }

    private void ApplyRoomSort()
    {
        if (_cachedSessions == null || _cachedSessions.Count <= 1) return;

        if (sortMode == RoomSortMode.Latest)
        {
            // 최신: createdAt 내림차순(큰 값이 최신)
            _cachedSessions.Sort((x, y) => GetSessionCreatedAt(y).CompareTo(GetSessionCreatedAt(x)));
        }
        else
        {
            // 가나다: 한글 -> 영어 -> 숫자, 각 내부는 가나다/알파/숫자
            _cachedSessions.Sort((x, y) => CompareRoomName(x.Name, y.Name));
        }
    }

    public void SetSortMode(RoomSortMode mode)
    {
        sortMode = mode;
        ApplyRoomSort();
        RebuildRoomPrefabList();
        OnRoomsUpdated?.Invoke(_cachedSessions);
    }

    #endregion

    #region 방 참가하기 & 등록하기의 대한 내부 함수 기능 구현

    /// <summary>
    /// Host 생성 시:
    /// - 입력한 roomName을 먼저 사용해 StartGame 시도
    /// - 이미 존재하면(GameIdAlreadyExists 등) 자동 rename 후 재시도
    /// </summary>
    private async void StartHostWithAutoRename(MatchMode mode)
    {
        CreateRunnerOnce();
        if (_runner == null) return;

        // 이미 방에 들어가 있으면 먼저 나가고 새로 시작
        if (_runner.IsRunning)
        {
            Debug.LogWarning("[Fusion] Already in a room. Leaving current room then create new.");
            ResetRunner();
        }

        string baseName = NormalizeRoomName(roomName);

        // 모드에 따라 최대 인원 강제
        int forcedMaxPlayers = GetMaxPlayersByMode(mode);

        // 최대 3번까지 이름 바꿔가며 생성 시도
        const int MAX_TRY = 3;
        string tryName = baseName;

        _starting = true;

        for (int attempt = 1; attempt <= MAX_TRY; attempt++)
        {
            var result = await StartGameInternal(GameMode.Host, tryName, mode, forcedMaxPlayers);

            if (result.Ok)
            {
                // 성공했으면 실제 방 이름을 tryName으로 확정(표시용)
                roomName = tryName;
                maxPlayers = forcedMaxPlayers;
                _starting = false;

                Debug.Log($"[Fusion] Host created. Room={roomName} Mode={mode} Max={maxPlayers}");

                //SceneManager.LoadScene(2);
                return;
            }

            // "방 이름이 이미 존재" 같은 경우면 이름을 바꿔서 재시도
            // Fusion ShutdownReason에 GameIdAlreadyExists가 있음. :contentReference[oaicite:0]{index=0}
            if (result.ShutdownReason == ShutdownReason.GameIdAlreadyExists ||
                result.ShutdownReason == ShutdownReason.ServerInRoom)
            {
                Debug.LogWarning($"[Fusion] Room name already exists. Retry with new name. " +
                                 $"Attempt {attempt}/{MAX_TRY} Reason={result.ShutdownReason} Msg={result.ErrorMessage}");

                tryName = MakeUniqueRoomName(baseName);
                continue;
            }

            // 그 외 실패는 바로 종료
            Debug.LogError($"[Fusion] Host StartGame failed. Reason={result.ShutdownReason} Msg={result.ErrorMessage}");
            break;
        }

        _starting = false;
        // 실패했으니 Runner를 리셋해 다시 시도 가능하게
        ResetRunner();
    }

    /// <summary>
    /// 공통 StartGame 진입점(Host/Client)
    /// - Client는 방이 없으면 GameNotFound로 실패 가능
    /// - Team/Solo에 따라 최대 인원 강제
    /// </summary>
    private async void StartGame(GameMode mode, string sessionName, MatchMode modeValue)
    {
        CreateRunnerOnce();
        if (_runner == null) return;

        // 이미 실행 중이면 방 변경 전에는 나가야 함
        if (_runner.IsRunning)
        {
            Debug.LogWarning("[Fusion] Already running. Leave first to join/create another room.");
            return;
        }

        _starting = true;

        int forcedMaxPlayers = GetMaxPlayersByMode(modeValue);

        // 참가(Client)도 방 프로퍼티를 읽어 currentMode를 맞추는 게 정석이지만
        // 여기서는 "참가 요청"은 sessionName만 정확하면 됨.
        var result = await StartGameInternal(mode, sessionName, modeValue, forcedMaxPlayers);

        _starting = false;

        if (result.Ok)
        {
            //  Client 참가 성공 시 씬 이동
            Debug.Log($"[Fusion] Join success. Mode={mode} Room={sessionName} Max={forcedMaxPlayers}");

            //SceneManager.LoadScene(2);
        }
        else
        {
            Debug.LogWarning($"[Fusion] StartGame failed. Mode={mode} Room={sessionName} " +
                             $"Reason={result.ShutdownReason} Msg={result.ErrorMessage}");

            // 참가 실패(GameNotFound 등) 시 다시 시도 가능하게 리셋
            ResetRunner();
        }
    }

    /// <summary>
    /// 실제 Fusion StartGame 호출
    /// 
    /// SessionProperties:
    /// - mode      : 0=Solo, 1=Team
    /// - max       : 강제 최대 인원(표시/백업용)
    /// - createdAt : 방 생성 시간(UTC 초) -> 최신정렬에 사용
    /// </summary>
    private System.Threading.Tasks.Task<StartGameResult> StartGameInternal(
        GameMode mode, string sessionName, MatchMode modeValue, int forcedMaxPlayers)
    {

        // 방 설정(모드/최대인원)을 세션 프로퍼티로 저장 -> 참가자들이 읽을 수 있음
        var props = new Dictionary<string, SessionProperty>
        {
            { "mode", (int)modeValue },           // 0=Solo, 1=Team
            { "max",  forcedMaxPlayers },         // 참고용
            { "createdAt", (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
            { "map", selectedMap },
        };

        return _runner.StartGame(new StartGameArgs
        {
            GameMode = mode,
            SessionName = sessionName,
            Scene = gameScene,
            SceneManager = _sceneManager,
            PlayerCount = forcedMaxPlayers,        //  최대 인원 강제 적용
            SessionProperties = props,
        });
    }

    // ---- INetworkRunnerCallbacks (Fusion 2.0.8 시그니처) ----
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        // 현재 인원 갱신(ActivePlayers 열거)
        int count = 0;
        foreach (var _ in runner.ActivePlayers) count++;
        playerCount = count;

        // 세션 프로퍼티에서 모드 읽기
        // SessionProperty는 enum으로 바로 캐스팅 불가 -> int로 꺼낸 뒤 enum 변환
        if (runner.SessionInfo.IsValid &&
            runner.SessionInfo.Properties != null &&
            runner.SessionInfo.Properties.TryGetValue("mode", out var m))
        {
            int modeInt = m; // 대부분 버전에서 SessionProperty -> int 암시적 변환 가능
            if (Enum.IsDefined(typeof(MatchMode), modeInt))
                currentMode = (MatchMode)modeInt;
            else
                currentMode = MatchMode.Solo;
        }

        // 모드별 최대 인원 강제 (표시용에도 반영)
        maxPlayers = GetMaxPlayersByMode(currentMode);

        Debug.Log($"[Fusion] Player Joined: {player} / Count={playerCount} / Mode={currentMode} / Max={maxPlayers}");

        //  최종 안전장치: 서버(Host)가 인원 초과면 끊기
        if (runner.IsServer && playerCount > maxPlayers)
            runner.Disconnect(player);

        // (참고) "게임 시작 가능 조건"을 UI에서 쓰고 싶으면:
        // - Solo: 최소 2명 이상이면 시작 가능
        // - Team: 4명 다 찼을 때 시작 가능
        // bool canStart = (currentMode == MatchMode.Solo) ? (playerCount >= 2) : (playerCount >= 4);
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        int count = 0;
        foreach (var _ in runner.ActivePlayers) count++;
        playerCount = count;

        Debug.Log($"[Fusion] Player Left: {player} / Count={playerCount}");
    }

    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        Debug.LogWarning($"[Fusion] ConnectFailed: {reason} (방이 없거나 네트워크 문제일 수 있음)");
        ResetRunner();
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.LogWarning($"[Fusion] Shutdown: {shutdownReason}");
        _starting = false;
        playerCount = 0;
        _joinedLobby = false;
    }

    /// <summary>
    /// 로비에서 방 리스트가 갱신될 때 호출됨 (매우 중요)
    /// - 여기서 _cachedSessions를 새로 구성하고
    /// - 가득 찬 방은 숨기고
    /// - 정렬을 적용하고
    /// - OnRoomsUpdated 이벤트를 쏴서 UI 갱신
    /// </summary>
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        _cachedSessions.Clear();

        // 유효한 것만 담기(선택)
        foreach (var s in sessionList)
        {
            if (!s.IsValid) continue;
            //가득찬 방은 숨김
            if (IsSessionFull(s)) continue;

            _cachedSessions.Add(s);
        }

        ApplyRoomSort();

        RebuildRoomPrefabList();

        Debug.Log($"[Fusion] Lobby Rooms: {_cachedSessions.Count}");
        // UI 갱신 이벤트
        OnRoomsUpdated?.Invoke(_cachedSessions);
    }

    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnSceneLoadStart(NetworkRunner runner)
    {
        Debug.Log("[Fusion] OnSceneLoadStart");
    }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        // 1) 게임씬을 활성씬으로 바꾸기
        // SceneRef의 BuildIndex를 사용하려면 gameScene이 올바르게 세팅되어 있어야 함
        var loadedGameScene = SceneManager.GetSceneByBuildIndex(2);
        if (loadedGameScene.IsValid() && loadedGameScene.isLoaded)
        {
            SceneManager.SetActiveScene(loadedGameScene);
        }

        // 2) 로비씬 언로드 (로비 UI/카메라/오디오리스너 제거)
        var lobby = SceneManager.GetSceneByName(lobbySceneName);
        if (lobby.IsValid() && lobby.isLoaded)
        {
            SceneManager.UnloadSceneAsync(lobby);
        }

        Debug.Log($"[Fusion] SceneLoadDone -> ActiveScene: {SceneManager.GetActiveScene().name}");
    }


    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }

    #endregion


    #region 방 검색에 따른 변수 및 기능
    [Header("방 검색 키워드(부분 일치)")]
    [SerializeField] private string _roomSearchKeyword = "";
    public string RoomSearchKeyword => _roomSearchKeyword;

    /// <summary>
    /// 로비 방 검색 키워드 세팅 (부분검색)
    /// - 비우면 전체 표시
    /// </summary>
    public void SetRoomSearchKeyword(string keyword)
    {
        _roomSearchKeyword = (keyword ?? "").Trim();
        RebuildRoomPrefabList();
        // UI에게 "갱신" 신호 (LobbyUIManager가 구독 중) :contentReference[oaicite:4]{index=4}
        OnRoomsUpdated?.Invoke(_cachedSessions);
    }

    private bool PassSearchFilter(string roomName, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return true;
        if (string.IsNullOrEmpty(roomName)) return false;

        // 대/소문자 무시(영문). 한글은 그대로 포함검색.
        return roomName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// 현재 _cachedSessions(정렬/가득찬방 제외된 리스트)에 대해
    /// 검색 필터를 적용해서 roomPrefabList를 재생성.
    /// RoomPrefabData.index는 "CachedSessions 인덱스" 그대로 유지.
    /// </summary>
    private void RebuildRoomPrefabList()
    {
        roomPrefabList.Clear();

        for (int i = 0; i < _cachedSessions.Count; i++)
        {
            var s = _cachedSessions[i];
            if (!PassSearchFilter(s.Name, _roomSearchKeyword))
                continue;

            roomPrefabList.Add(new RoomPrefabData
            {
                index = i,                 // RoomPrefab가 CachedSessions에서 찾아 쓰는 인덱스
                number = roomPrefabList.Count
            });
        }
    }
    #endregion

    #region 방 나가기

    public async Task LeaveRoomToLobby(int lobbySceneIndex)
    {
        try
        {
            if (Runner != null && Runner.IsRunning)
                await Runner.Shutdown(destroyGameObject: false);
        }
        finally
        {
            SceneManager.LoadScene(lobbySceneIndex, LoadSceneMode.Single);
        }
    }

    #endregion
}
