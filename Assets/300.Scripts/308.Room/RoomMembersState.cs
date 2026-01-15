using Fusion;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum TeamSide
{
    None = 0,
    Red = 1,
    Blue = 2
}

/// <summary>
/// 방 참가자들의 닉네임/레벨/레디 상태를 네트워크로 공유하는 상태 오브젝트(씬에 1개).
/// - 각 클라는 Spawned 때 자기 프로필(Firebase)을 서버에 제출
/// - 서버는 Slots에 저장하고 모든 클라에게 동기화
/// </summary>
public class RoomMembersState : NetworkBehaviour
{
    #region 변수
    public static RoomMembersState Instance { get; private set; }

    public const int MaxSlots = 4;

    public struct MemberSlot : INetworkStruct
    {
        public PlayerRef player;
        public NetworkString<_16> nickname;

        //  이름 추가 (한글 이름/실명 고려해서 32 추천)
        public NetworkString<_32> name;

        public int level;
        //준비 상태
        public NetworkBool ready;
        //프로필 URL
        public NetworkString<_256> photoUrl;

        public byte occupied; // 1=사용, 0=비어있음
    }
    // ===== 멤버 슬롯 =====
    [Networked, Capacity(MaxSlots)]
    public NetworkArray<MemberSlot> Slots => default;

    // ===== 방장(Leader) =====
    [Networked] public PlayerRef Leader { get; set; }

    // ===== 룸 설정(네트워크 동기화) =====
    // 0=Solo, 1=Team / map:0=Korea,1=USA / max: Solo 2~4, Team 4
    [Networked] public int ModeInt { get; set; }
    [Networked] public int MapInt { get; set; }
    [Networked] public int MaxPlayers { get; set; }


    //  네트워크 상태 변경 "버전" 번호
    // - 목적:
    //   RoomMembersState의 Slots/Leader/Ready/RoomSettings/RoomTitle 등이 바뀌었을 때,
    //   UI가 그 변경을 감지해서 "필요한 순간에만" 무거운 갱신(플레이어 리스트 재구성)을 하도록 만들기 위함.
    //
    // - 왜 int 하나로 하냐?
    //   GPM InfiniteScroll 리스트를 자주 Clear/Insert 하면(예: 0.2초마다),
    //   클릭 중인 버튼 리스너가 사라지거나(아이템 재바인딩/재사용) 클릭이 씹히는 문제가 생길 수 있음.
    //   그래서 "상태가 바뀐 순간"만 감지해서 RefreshPlayersUI()를 1번만 실행하려고,
    //   서버가 Revision을 올리고, 클라이언트가 Revision 변화를 보고 갱신하도록 함.
    //
    // - 동작 방식:
    //   (서버/호스트) 상태가 바뀔 때마다 Revision++
    //   (클라) 마지막으로 본 Revision(_lastRevision)과 비교해서 값이 달라졌으면
    //         → 그때만 RefreshPlayersUI() 같은 무거운 UI 갱신 실행.
    //
    // - 중요한 규칙:
    //   * Revision은 서버(StateAuthority)만 변경해야 함.
    //     클라이언트가 값을 바꿔도 네트워크 권한이 없으면 반영되지 않음.
    //   * 따라서 Slots.Set/Leader 변경/Ready 변경/설정 변경 등 "네트워크 상태가 변한 직후"에만 증가시키면 됨.
    //
    // - 어디서 증가시키나? (예시)
    //   RPC_SubmitProfile (슬롯 생성/프로필 저장) 이후
    //   RPC_SetReady (ready 토글) 이후
    //   Server_RemovePlayer / OnPlayerLeft 처리 이후
    //   RPC_RequestTransferLeader (리더 변경) 이후
    //   RPC_RequestKick / RPC_RequestLeave 이후
    //   RPC_RequestChangeRoomSettings / RPC_RequestChangeRoomTitle 이후
    //
    // - 어디서 쓰나? (예시: RoomManager.Update)
    //   if (members.Revision != _lastRevision) { _lastRevision = members.Revision; RefreshAllUI_Heavy(); }
    //   else { RefreshUI_Light(); }  // 버튼/텍스트 같은 가벼운 갱신만
    [Networked] public int Revision { get; set; }

    [Networked] public PlayerRef KickSignalTarget { get; set; }

    [Networked] public int KickSignalNonce { get; set; } // 바뀜 감지용

    private int _lastKickNonce = -1;
    private int _lastKickRpcNonce = -1;
    private bool _kickedHandling;

    // KickSignal 못 받는 경우 대비(내 슬롯이 사라졌는데도 남아있으면 강퇴로 처리)
    private float _orphanKickTimer = 0f;
    private const float ORPHAN_KICK_TIMEOUT = 1.0f; // 1초 정도면 충분

    public MatchMode Mode => (MatchMode)ModeInt;
    public int Map => MapInt;

    private const int SOLO_MIN = 2;
    private const int SOLO_MAX = 4;
    private const int TEAM_FIXED = 4;

    private bool _profileSubmitted;

    private float _cleanupTimer;

    private Coroutine _coWaitSubmit;
    #endregion

    public override void Spawned()
    {
        Instance = this;

        _lastKickNonce = KickSignalNonce;
        _orphanKickTimer = 0f;
        _kickedHandling = false;
        _profileSubmitted = false;

        if (_coWaitSubmit != null)
        {
            StopCoroutine(_coWaitSubmit);
            _coWaitSubmit = null;
        }

        // --- 1) "권한자(=StateAuthority, Shared에서는 Master)" 쪽 초기화 ---
        // IMPORTANT:
        // NetworkObject에 "Is Master Client Object"가 켜져 있으면
        // Master Client가 바뀌면 이 오브젝트의 StateAuthority도 자동으로 바뀜.
        if (Object.HasStateAuthority)
        {
            bool changed = false;

            // (1) 룸 타이틀 초기화 (빈값이면 세션이름)
            if (RoomTitle.ToString().Length == 0)
            {
                string defaultTitle =
                    (Runner != null && Runner.SessionInfo.IsValid)
                    ? Runner.SessionInfo.Name
                    : "Room";

                RoomTitle = defaultTitle;
                changed = true;
            }

            // (2) 세션 프로퍼티에서 모드/맵/Max 초기화
            // (주의) 이 함수 내부에서 Networked 값(ModeInt/MapInt/MaxPlayers)을 세팅하므로
            // 변경이 있었을 수 있음
            int prevMode = ModeInt;
            int prevMap = MapInt;
            int prevMax = MaxPlayers;

            ServerInitSettingsFromSessionProperties();

            if (ModeInt != prevMode || MapInt != prevMap || MaxPlayers != prevMax)
                changed = true;

            // (3) 리더(표시용)는 "현재 권한자(StateAuthority)"로 맞추는 게 Shared에서 가장 안전함
            // - 슬롯이 아직 비어있어도(프로필 제출 전) 리더는 안정적으로 존재해야 함
            var master = Object.StateAuthority;
            if (master != PlayerRef.None && Leader != master)
            {
                Leader = master;
                changed = true;
            }

            // (4) 변경이 있었다면 Revision 증가(UI 갱신 트리거)
            if (changed)
                BumpRevision();
        }

        // 로컬 플레이어 프로필 제출 (Firebase 준비가 늦을 수 있어서 재시도 코루틴 포함)
        TrySubmitLocalProfileOnce();
        if (!_profileSubmitted)
            _coWaitSubmit = StartCoroutine(CoWaitFirebaseThenSubmit());
    }
    #region 프로필 이미지 적용
    private IEnumerator CoWaitFirebaseThenSubmit()
    {
        float timeout = 6f; // 너무 길게 기다릴 필요 없음(원하면 늘려)
        float t = 0f;

        while (!_profileSubmitted && t < timeout)
        {
            TrySubmitLocalProfileOnce();
            t += 0.2f;
            yield return new WaitForSeconds(0.2f);
        }
    }

    private void TrySubmitLocalProfileOnce()
    {
        if (_profileSubmitted) return;
        if (Runner == null) return;
        if (Runner.LocalPlayer == PlayerRef.None) return;

        string nick = $"Player_{Runner.LocalPlayer.PlayerId}";
        string name = "-";
        int level = 1;
        string photoUrl = "";

        var fb = FireBaseAuthManager.Instance;
        if (fb != null && fb.IsReady && fb.CurrentAccount != null)
        {
            nick = fb.CurrentAccount.NickName;
            name = fb.CurrentAccount.Name;
            level = fb.CurrentAccount.AccountLevel;

            // Account에 PhotoUrl 있어야 함
            photoUrl = (fb.CurrentAccount.PhotoUrl ?? "").Trim();
        }
        else
        {
            // Firebase 준비 안 됐으면 아직 제출하지 않음
            return;
        }

        RPC_SubmitProfile(Runner.LocalPlayer, nick, name, level, photoUrl);
        _profileSubmitted = true;
    }

    /// <summary>
    /// [프로필 제출 RPC]
    /// - 모든 클라이언트(RpcSources.All)가 호출 가능
    /// - 실제 반영은 StateAuthority(Shared 모드에서는 Master)만 수행(RpcTargets.StateAuthority)
    ///
    /// 역할:
    /// 1) 입력 값(닉/이름/URL/레벨)을 정리/검증(Trim, 길이 제한, Clamp)
    /// 2) 해당 플레이어(PlayerRef who)의 슬롯을 확보(없으면 생성/배정)
    /// 3) 슬롯 데이터(Slots)에 프로필 정보를 저장
    /// 4) 리더가 아직 없으면 리더 선출
    /// 5) Revision 증가로 UI 갱신 트리거
    ///
    /// 왜 이렇게?
    /// - 네트워크 공유 데이터(Slots)는 권한자만 수정해야 값 경쟁/덮어쓰기 문제를 방지할 수 있음.
    /// - 문자열은 NetworkString 용량 제한이 있으므로 길이 제한을 걸어 안전하게 저장.
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_SubmitProfile(PlayerRef who, string nick, string name, int level, string photoUrl, RpcInfo info = default)
    {
        // 안전장치: 이 RPC는 "StateAuthority(=마스터)"에서만 실제로 반영해야 함
        // 모든 클라가 동시에 Slots를 만지면 데이터 경합/설정 튐이 발생할 수 있음
        if (!Object.HasStateAuthority) return;

        // 1) 닉네임 검증
        nick = (nick ?? "").Trim();
        if (nick.Length == 0) nick = $"Player_{who.PlayerId}";
        if (nick.Length > 16) nick = nick.Substring(0, 16);

        // 2) 표시 이름(설명/이름) 검증
        name = (name ?? "").Trim();
        if (name.Length == 0) name = "-";
        if (name.Length > 32) name = name.Substring(0, 32);

        //3) 프로필 이미지 URL 정리
        photoUrl = (photoUrl ?? "").Trim();
        // NetworkString<_256> 초과 방지(안전)
        if (photoUrl.Length > 256) photoUrl = photoUrl.Substring(0, 256);
        
        //4) 레벨 검증
        level = Mathf.Clamp(level, 1, 999);

        // 5) 슬롯 확보/배정
        // - 해당 플레이어(who)가 들어갈 슬롯 인덱스를 확보(없으면 생성)
        // - 실패하면(-1 등) 처리 중단
        int idx = EnsureSlot(who);
        if (idx < 0) return;

        // 6) 슬롯 데이터에 프로필 저장
        // - Slots는 네트워크로 동기화되는 참가자 데이터 목록/배열(추정)
        // - Get/Set으로 구조체(또는 데이터)를 꺼내 수정 후 다시 저장하는 패턴
        var s = Slots.Get(idx);
        s.nickname = nick;
        s.name = name;
        s.level = level;
        s.photoUrl = photoUrl;
        Slots.Set(idx, s);

        // 리더가 아직 없으면 선출
        if (Leader == PlayerRef.None)
            ServerElectLeader();

        BumpRevision();
    }

    #endregion

    #region 초기 리더 선출
    /// <summary>
    /// [서버/권한자 전용] 리더 선출(초기 선출용)
    /// - Leader가 아직 없을 때(Leader == None),
    /// - 또는 프로필 제출/슬롯 생성 이후 최초 1회 리더를 정해야 할 때 사용.
    ///
    /// 방식:
    /// - Slots를 앞에서부터 훑어서 "occupied==1"인 첫 번째 플레이어를 리더로 지정.
    /// - (즉, 사실상 '가장 먼저 슬롯을 차지한 플레이어'가 리더가 되는 규칙)
    ///
    /// 주의:
    /// - 이 함수는 '리더 위임'이 아니라 '초기 리더 자동 선출'에 가깝다.
    /// </summary>
    private void ServerElectLeader()
    {
        if (!Object.HasStateAuthority) return;

        PlayerRef found = PlayerRef.None;
        for (int i = 0; i < MaxSlots; i++)
        {
            var s = Slots.Get(i);
            if (s.occupied == 1) { found = s.player; break; }
        }

        if (Leader == found) return;

        Leader = found;

        BumpRevision();
    }
    #endregion

    #region 방 생헝 / 참가 직후 룸 설정 초기화
    /// <summary>
    /// 세션(SessionInfo) 생성 시 넣어둔 Properties("mode","map","max")를 읽어서
    /// 룸 설정(Networked 값: ModeInt/MapInt/MaxPlayers)을 "권한자(StateAuthority)"가 초기화/확정하는 함수.
    ///
    /// 목적:
    /// - 방 생성/입장 직후 UI/게임 로직이 참조할 공식 룸 설정값을 하나로 통일
    /// - 잘못된 값(범위 밖, 쓰레기 값)을 방어(정규화/Clamp)
    /// - Fusion에서 SessionInfo.MaxPlayers가 0으로 오는 경우를 대비해 "max" 프로퍼티를 우선 신뢰
    ///
    /// 주의:
    /// - 반드시 Object.HasStateAuthority(Shared에서는 Master)인 쪽에서만 호출하는 것이 안전함.
    ///   (모든 클라이언트가 동시에 Networked 값을 만지면 덮어쓰기 경쟁/설정 튐 발생)
    /// </summary>
    private void ServerInitSettingsFromSessionProperties()
    {
        // 기본값
        int mode = (int)MatchMode.Solo;
        int map = 0;
        int max = 4;

        // 1) 세션 정보가 유효하면, 세션 생성 시 저장해둔 커스텀 프로퍼티를 읽는다
       // - Runner.SessionInfo.Properties: 로비/세션 생성 시 넣어둔 key-value (mode/map/max 등)
        if (Runner != null && Runner.SessionInfo.IsValid && Runner.SessionInfo.Properties != null)
        {
            var props = Runner.SessionInfo.Properties;

            if (props.TryGetValue("mode", out var pm)) mode = (int)pm;
            if (props.TryGetValue("map", out var pmap)) map = Mathf.Clamp((int)pmap, 0, 1);

            // MaxPlayers가 0일 수 있어서 "max" 프로퍼티 우선
            if (props.TryGetValue("max", out var pmax)) max = (int)pmax;
            else max = (Runner.SessionInfo.MaxPlayers > 0) ? Runner.SessionInfo.MaxPlayers : 4;
        }
        //2) 값 정규화(유효하지 않은 값 방어)
        mode = (mode == (int)MatchMode.Team) ? (int)MatchMode.Team : (int)MatchMode.Solo;
        max = (mode == (int)MatchMode.Team) ? TEAM_FIXED : Mathf.Clamp(max, SOLO_MIN, SOLO_MAX);

        ModeInt = mode;
        MapInt = map;
        MaxPlayers = max;
    }
    #endregion

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_SetReady(PlayerRef who, bool ready, RpcInfo info = default)
    {
        if (!Object.HasStateAuthority) return;

        int idx = FindSlot(who);
        if (idx < 0) return;

        var s = Slots.Get(idx);
        s.ready = ready;
        Slots.Set(idx, s);

        BumpRevision();
    }

    // ===== 룸 설정 변경 (리더만) =====
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestChangeRoomSettings(int newModeInt, int newMap, int soloMaxPlayers, RpcInfo info = default)
    {
        if (!Object.HasStateAuthority) return;

        // 요청자 = RPC 보낸 플레이어
        var requester = info.Source;

        // 리더만 가능
        if (requester != Leader) return;

        int mode = (newModeInt == (int)MatchMode.Team) ? (int)MatchMode.Team : (int)MatchMode.Solo;
        int map = Mathf.Clamp(newMap, 0, 1);
        int max = (mode == (int)MatchMode.Team) ? TEAM_FIXED : Mathf.Clamp(soloMaxPlayers, SOLO_MIN, SOLO_MAX);

        // 현재 인원보다 작게 줄이는 건 서버에서도 방지
        int curPlayers = 0;
        for (int i = 0; i < MaxSlots; i++)
        {
            var s = Slots.Get(i);
            if (s.occupied == 1) curPlayers++;
        }
        if (curPlayers > max) return;

        ModeInt = mode;
        MapInt = map;
        MaxPlayers = max;

        // (선택) 룸 설정 바뀌면 전원 Ready 해제(원하면 유지해도 됨)
        for (int i = 0; i < MaxSlots; i++)
        {
            var s = Slots.Get(i);
            if (s.occupied == 0) continue;
            s.ready = false;
            Slots.Set(i, s);
        }

        BumpRevision();
    }

    // ===== 리더 위임 (리더만) =====
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestTransferLeader(PlayerRef newLeader, RpcInfo info = default)
    {
        if (!Object.HasStateAuthority) return;

        var requester = info.Source;
        if (requester == PlayerRef.None && Runner != null)
            requester = Runner.LocalPlayer; // Host 예외 방어

        Debug.Log($"[RoomMembersState] TransferLeader req from={requester} -> {newLeader} / currentLeader={Leader}");

        // 리더만 위임 가능
        if (requester != Leader) return;
        if (newLeader == PlayerRef.None) return;
        if (newLeader == requester) return;

        // newLeader가 현재 방 멤버인지 확인(슬롯 기준)
        bool exists = false;
        for (int i = 0; i < MaxSlots; i++)
        {
            var s = Slots.Get(i);
            if (s.occupied == 1 && s.player == newLeader)
            {
                exists = true;
                break;
            }
        }
        if (!exists) return;

        Leader = newLeader;

        BumpRevision();
    }

    // ===== 강퇴 (리더만) =====
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestKick(PlayerRef target, RpcInfo info = default)
    {
        if (!Object.HasStateAuthority) return;

        var requester = info.Source;
        if (requester == PlayerRef.None && Runner != null)
            requester = Runner.LocalPlayer;

        if (requester != Leader) return;
        if (target == PlayerRef.None) return;
        if (target == requester) return;

        // 타겟이 실제 멤버인지 확인
        bool exists = false;
        for (int i = 0; i < MaxSlots; i++)
        {
            var s = Slots.Get(i);
            if (s.occupied == 1 && s.player == target) { exists = true; break; }
        }
        if (!exists) return;

        // 1) 슬롯 정리 (모든 클라 UI 반영)
        Server_RemovePlayer(target);

        // 2) 킥 신호 (Disconnect 실패 대비, "자진 퇴장" 강제)
        KickSignalTarget = target;
        KickSignalNonce++;

        // 3) 킥 알림 RPC (이게 씬 이동을 “확실”하게 만든다)
        RPC_NotifyKicked(target, KickSignalNonce);

        if (Runner != null && Runner.IsServer)
        {
            Runner.Disconnect(target);
        }

        BumpRevision();
    }

    private void HandleKickedOnce()
    {
        if (_kickedHandling) return;
        _kickedHandling = true;

        NetWorkLauncher.instance?.ReturnToLobbyFromKicked();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_NotifyKicked(PlayerRef target, int nonce, RpcInfo info = default)
    {
        // 중복 수신 방지(Shared에서 가끔 같은 프레임에 여러 번 들어오는 케이스 방어)
        if (nonce == _lastKickRpcNonce) return;
        _lastKickRpcNonce = nonce;

        if (Runner == null) return;

        if (Runner.LocalPlayer == target)
        {
            Debug.Log($"[Kick] I am kicked. target={target} nonce={nonce}");
            HandleKickedOnce(); // 기존 함수 그대로 사용
        }
    }

    // ===== 나가기 =====
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestLeave(PlayerRef who, RpcInfo info = default)
    {
        if (!Object.HasStateAuthority) return;
        Server_RemovePlayer(who);

        BumpRevision();
    }
    #region 서버 슬롯 관리
    // ===== 서버 슬롯 관리 =====
    private int FindSlot(PlayerRef who)
    {
        for (int i = 0; i < MaxSlots; i++)
        {
            var s = Slots.Get(i);
            if (s.occupied == 1 && s.player == who) return i;
        }
        return -1;
    }

    /// <summary>
    /// [슬롯 확보/생성]
    /// - 참가자(PlayerRef who)가 들어갈 슬롯을 확보한다.
    /// - 이미 슬롯이 있으면 그 인덱스를 반환하고,
    /// - 없으면 빈 슬롯(occupied==0)을 찾아 새로 할당 후 초기값으로 세팅한다.
    ///
    /// 사용처 예:
    /// - RPC_SubmitProfile에서 프로필을 저장하기 전에 반드시 슬롯이 있어야 함.
    /// - 방 참가자가 새로 들어오거나, 늦게 프로필을 제출하는 경우에도 안전하게 슬롯을 만들기 위함.
    ///
    /// 반환값:
    /// - 성공: 슬롯 인덱스(0~MaxSlots-1)
    /// - 실패: -1 (빈 슬롯이 없음 = 방이 꽉 참 / 데이터 꼬임 방어)
    /// </summary>
    private int EnsureSlot(PlayerRef who)
    {
        // 1) 이미 슬롯이 할당되어 있으면 재사용(중복 생성 방지)
        int existing = FindSlot(who);
        if (existing >= 0) return existing;

        // 2) 빈 슬롯 탐색(occupied == 0인 자리)
        for (int i = 0; i < MaxSlots; i++)
        {
            var s = Slots.Get(i);
            if (s.occupied == 0)
            {
                s.occupied = 1;
                s.player = who;
                s.nickname = default;
                s.name = default;
                s.level = 1;
                s.ready = false;
                s.photoUrl = default;
                Slots.Set(i, s);
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// [서버/권한자 전용] 플레이어 제거(슬롯 비우기)
    /// - 누군가 방에서 나갔거나(Disconnect/Leave),
    /// - 강퇴(Kick)되었거나,
    /// - 게임/로비 로직상 제거해야 할 때 호출되어
    /// 해당 플레이어의 슬롯 데이터를 초기화하고 UI 갱신을 트리거한다.
    ///
    /// 중요:
    /// - Slots(네트워크 동기화 데이터)는 StateAuthority(Shared에서는 Master)만 수정해야 안전함.
    /// </summary>
    public void Server_RemovePlayer(PlayerRef who)
    {
        if (!Object.HasStateAuthority) return;

        bool changed = false;

        int idx = FindSlot(who);
        if (idx >= 0)
        {
            var s = Slots.Get(idx);
            s.occupied = 0;
            s.player = default;
            s.nickname = default;
            s.name = default;
            s.level = 1;
            s.ready = false;
            s.photoUrl = default;
            Slots.Set(idx, s);
            changed = true;
        }

        // 리더였으면 새 리더 선출
        if (Leader == who)
        {
            TryElectLeaderRandom();
            changed = true;
        }
        //  팀전이면 슬롯을 앞에서부터 당겨서 “인덱스=팀” 규칙 유지
        if (changed && ModeInt == (int)MatchMode.Team)
        {
            Server_CompactSlots_TeamMode();
        }

        if (changed) BumpRevision(); // OnPlayerLeft로 빠져도 UI 갱신 보장
    }
    #endregion

    // ===== 표시용 룸 타이틀(세션 이름과 별개로 "룸 안에서"만 동기화) =====
    [Networked] public NetworkString<_32> RoomTitle { get; set; }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestChangeRoomTitle(string newTitle, RpcInfo info = default)
    {
        if (!Object.HasStateAuthority) return;

        var requester = info.Source;
        if (requester != Leader) return; // 리더만

        newTitle = (newTitle ?? "").Trim();
        if (newTitle.Length == 0) newTitle = "Room";
        if (newTitle.Length > 32) newTitle = newTitle.Substring(0, 32);

        RoomTitle = newTitle;

        BumpRevision();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this) Instance = null;
    }
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public override void FixedUpdateNetwork()
    {
        // ===== 0) (권한 상관없이) 킥 신호 감지 =====
        if (Runner != null && Runner.LocalPlayer != PlayerRef.None)
        {
            if (KickSignalNonce != _lastKickNonce)
            {
                _lastKickNonce = KickSignalNonce;

                if (KickSignalTarget == Runner.LocalPlayer)
                {
                    HandleKickedOnce();
                    return;
                }
            }

            // 내 슬롯이 사라졌으면(=서버가 나를 제거했는데 신호 누락/타이밍 문제)
            // 일정 시간 지나면 강퇴 처리
            if (_profileSubmitted)
            {
                bool existsMe = false;
                for (int i = 0; i < MaxSlots; i++)
                {
                    var s = Slots.Get(i);
                    if (s.occupied == 1 && s.player == Runner.LocalPlayer) { existsMe = true; break; }
                }

                if (!existsMe)
                {
                    _orphanKickTimer += Runner.DeltaTime;
                    if (_orphanKickTimer >= ORPHAN_KICK_TIMEOUT)
                    {
                        HandleKickedOnce();
                        return;
                    }
                }
                else
                {
                    _orphanKickTimer = 0f;
                }
            }
            else
            {
                _orphanKickTimer = 0f;
            }
        }

        // ===== 1) 아래부터는 StateAuthority(Shared에서는 Master)만 청소 =====
        if (!Object.HasStateAuthority) return;
        if (Runner == null) return;

        _cleanupTimer += Runner.DeltaTime;
        if (_cleanupTimer < 1.0f) return;
        _cleanupTimer = 0f;

        var actives = new HashSet<PlayerRef>();
        foreach (var p in Runner.ActivePlayers) actives.Add(p);

        bool changed = false;

        for (int i = 0; i < MaxSlots; i++)
        {
            var s = Slots.Get(i);
            if (s.occupied == 0) continue;

            if (s.player == PlayerRef.None || !actives.Contains(s.player))
            {
                s.occupied = 0;
                s.player = default;
                s.nickname = default;
                s.name = default;
                s.level = 1;
                s.ready = false;
                s.photoUrl = default;
                Slots.Set(i, s);
                changed = true;
            }
        }

        if (Leader == PlayerRef.None || !actives.Contains(Leader))
        {
            var master = Object.StateAuthority;

            if (master != PlayerRef.None && actives.Contains(master))
            {
                if (Leader != master)
                {
                    Leader = master;
                    changed = true;
                }
            }
            else
            {
                if (TryElectLeaderRandom(actives))
                    changed = true;
            }
        }

        if (changed && ModeInt == (int)MatchMode.Team)
            Server_CompactSlots_TeamMode();

        if (changed) BumpRevision();
    }

    //  서버에서만 Revision을 올리는 함수
    // - 네트워크 상태가 변경된 "직후"에 호출한다.
    // - 클라이언트가 호출해도 권한이 없으면 증가하지 않음(반영 안 됨).
    private void BumpRevision()
    {
        if (!Object.HasStateAuthority) return;
        Revision++;
    }

    #region 랜덤하게 리더 선출 

    private bool TryElectLeaderRandom(HashSet<PlayerRef> actives = null)
    {
        if (!Object.HasStateAuthority) return false;

        List<PlayerRef> candidates = new List<PlayerRef>(MaxSlots);

        for (int i = 0; i < MaxSlots; i++)
        {
            var s = Slots.Get(i);
            if (s.occupied == 0) continue;
            if (s.player == PlayerRef.None) continue;

            if (actives != null && !actives.Contains(s.player)) continue;
            candidates.Add(s.player);
        }

        PlayerRef newLeader = (candidates.Count > 0)
            ? candidates[UnityEngine.Random.Range(0, candidates.Count)]
            : PlayerRef.None;

        if (Leader == newLeader) return false;

        Leader = newLeader;

        // 리더 = 마스터로도 맞춰주고 싶으면(Shared에서 방장 개념 통일)
        if (Runner != null && newLeader != PlayerRef.None)
            Runner.SetMasterClient(newLeader);

        return true;
    }

    #endregion

    #region 팀전일 경우 방에 있는 인원이 나갔을 경우 인덱스 번호가 변경시 인덱스에 맞게 팀이 변경이 되어야하는 함수
    private void Server_CompactSlots_TeamMode()
    {
        if (!Object.HasStateAuthority) return;
        if (ModeInt != (int)MatchMode.Team) return;

        // 현재 occupied인 슬롯을 앞에서부터 모은다 (0->1->2->3 순서 유지)
        List<MemberSlot> occupied = new List<MemberSlot>(MaxSlots);
        for (int i = 0; i < MaxSlots; i++)
        {
            var s = Slots.Get(i);
            if (s.occupied == 1)
                occupied.Add(s);
        }

        // 전체 슬롯 비우고
        for (int i = 0; i < MaxSlots; i++)
        {
            var empty = Slots.Get(i);
            empty.occupied = 0;
            empty.player = default;
            empty.nickname = default;
            empty.name = default;
            empty.level = 1;
            empty.ready = false;
            empty.photoUrl = default;
            Slots.Set(i, empty);
        }

        // 앞에서부터 다시 채운다 => 인덱스가 당겨짐(팀도 당연히 바뀜)
        for (int i = 0; i < occupied.Count && i < MaxSlots; i++)
        {
            var s = occupied[i];
            s.occupied = 1;
            Slots.Set(i, s);
        }
    }
    #endregion

    #region 팀 찾기
    /// <summary>
    /// 현재 플레이어(PlayerRef who)가 "몇 번 슬롯(0~MaxSlots-1)"에 들어있는지 찾는다.
    /// - Slots 배열을 앞에서부터 훑어서:
    ///   occupied == 1(사용중) 이고 player == who 인 슬롯을 찾으면 그 인덱스를 반환
    /// - 못 찾으면 -1 반환(방 멤버가 아니거나 아직 슬롯 배정 전, 또는 나간 상태)
    /// </summary>
    public int FindSlotIndex(PlayerRef who)
    {
        for (int i = 0; i < MaxSlots; i++)
        {
            var s = Slots.Get(i);
            if (s.occupied == 1 && s.player == who) return i;
        }
        return -1;
    }

    /// <summary>
    /// 슬롯 인덱스(0~3)를 팀으로 변환한다.
    /// - 팀 규칙(고정):
    ///   0, 2번 슬롯 = Red 팀
    ///   1, 3번 슬롯 = Blue 팀
    /// - slotIndex가 음수(=없음)면 TeamSide.None 반환
    /// </summary>
    public TeamSide GetTeamBySlotIndex(int slotIndex)
    {
        if (slotIndex < 0) return TeamSide.None;
        // 0,2 = Red / 1,3 = Blue
        return (slotIndex % 2 == 0) ? TeamSide.Red : TeamSide.Blue;
    }

    /// <summary>
    /// 특정 플레이어(PlayerRef who)의 "현재 팀"을 구한다.
    /// - 1) FindSlotIndex로 who의 슬롯 번호를 찾고
    /// - 2) 그 슬롯 번호를 GetTeamBySlotIndex 규칙으로 팀으로 변환한다.
    /// - who가 방에 없으면 FindSlotIndex가 -1 → TeamSide.None 반환
    /// </summary>
    public TeamSide GetTeamByPlayer(PlayerRef who)
    {
        int idx = FindSlotIndex(who);
        return GetTeamBySlotIndex(idx);
    }
    #endregion
}
