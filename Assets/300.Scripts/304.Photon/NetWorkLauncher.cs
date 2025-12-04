using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

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
public class NetWorkLauncher : MonoBehaviour , INetworkRunnerCallbacks
{
    [Header("Room")]
    public string roomName = "BulmabulRoom";

    [Header("표시용: 현재 방 최대 인원(실제 적용은 모드에 따라 강제 보정됨)")]
    public int maxPlayers = 4;

    [Header("표시용: 현재 방 인원")]
    public int playerCount = 0;

    [Header("표시용: 현재 방 모드")]
    public MatchMode currentMode = MatchMode.Solo;

    #region 방 리스트
    [Header("Lobby(방 리스트)")]
    [SerializeField] public SessionLobby lobby = SessionLobby.Shared; // 보통 Shared 사용

    private bool _joinedLobby;

    // 로비에서 받은 방(세션) 리스트를 여기 저장
    public List<SessionInfo> _cachedSessions = new List<SessionInfo>();

    public IReadOnlyList<SessionInfo> CachedSessions => _cachedSessions;
    public int RoomCount => _cachedSessions.Count;

    // UI가 갱신되게 이벤트로 뿌리고 싶으면 사용(선택)
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
        DontDestroyOnLoad(gameObject);
        CreateRunnerOnce();
    }

    /// <summary>
    /// Runner는 "반드시 한 번만" 만들기.
    /// - 중복 AddComponent 하면 이상 증상(콜백 2번, 세션 꼬임) 생김
    /// </summary>
    private void CreateRunnerOnce()
    {
        if (_runner != null) return;

        _runner = gameObject.AddComponent<NetworkRunner>();
        _runner.ProvideInput = true;

        // 콜백 등록(이거 안 하면 INetworkRunnerCallbacks 함수들이 안 불림)
        _runner.AddCallbacks(this);

        // 씬 매니저도 1번만 생성해서 재사용(매번 AddComponent 하면 쌓임)
        // 로비와 게임씬을 분리하기 위해서 사용
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
        if (_runner == null) return;

        // 방에 들어가 있었다면 정상 종료
        if (_runner.IsRunning)
        {
            await _runner.Shutdown(destroyGameObject: false);
        }

        // Runner 컴포넌트 제거 후 새로 생성
        Destroy(_runner);
        _runner = null;

        CreateRunnerOnce();

        _starting = false;
        playerCount = 0;
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

                SceneManager.LoadScene(2);
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

            SceneManager.LoadScene(2);
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
    /// StartGame 실제 호출(재사용)
    /// </summary>
    private System.Threading.Tasks.Task<StartGameResult> StartGameInternal(
        GameMode mode, string sessionName, MatchMode modeValue, int forcedMaxPlayers)
    {
        var scene = SceneRef.FromIndex(2);

        // 방 설정(모드/최대인원)을 세션 프로퍼티로 저장 -> 참가자들이 읽을 수 있음
        var props = new Dictionary<string, SessionProperty>
        {
            { "mode", (int)modeValue },           // 0=Solo, 1=Team
            { "max",  forcedMaxPlayers },         // 참고용
        };

        return _runner.StartGame(new StartGameArgs
        {
            GameMode = mode,
            SessionName = sessionName,
            Scene = scene,
            SceneManager = _sceneManager,
            PlayerCount = forcedMaxPlayers,        //  최대 인원 강제 적용
            SessionProperties = props
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
        ResetRunner();
    }

    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        _cachedSessions.Clear();

        // 유효한 것만 담기(선택)
        foreach (var s in sessionList)
        {
            if (s.IsValid)
                _cachedSessions.Add(s);
        }

        // 이름순 정렬(선택)
        _cachedSessions.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        Debug.Log($"[Fusion] Lobby Rooms: {_cachedSessions.Count}");

        // UI 갱신 이벤트(선택)
        OnRoomsUpdated?.Invoke(_cachedSessions);
    }

    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }

    #endregion
}
