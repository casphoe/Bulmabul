using Fusion;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 방 참가자들의 닉네임/레벨/레디 상태를 네트워크로 공유하는 상태 오브젝트(씬에 1개).
/// - 각 클라는 Spawned 때 자기 프로필(Firebase)을 서버에 제출
/// - 서버는 Slots에 저장하고 모든 클라에게 동기화
/// </summary>
public class RoomMembersState : NetworkBehaviour
{
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


    private void ServerInitSettingsFromSessionProperties()
    {
        // 기본값
        int mode = (int)MatchMode.Solo;
        int map = 0;
        int max = 4;

        if (Runner != null && Runner.SessionInfo.IsValid && Runner.SessionInfo.Properties != null)
        {
            var props = Runner.SessionInfo.Properties;

            if (props.TryGetValue("mode", out var pm)) mode = (int)pm;
            if (props.TryGetValue("map", out var pmap)) map = Mathf.Clamp((int)pmap, 0, 1);

            // MaxPlayers가 0일 수 있어서 "max" 프로퍼티 우선
            if (props.TryGetValue("max", out var pmax)) max = (int)pmax;
            else max = (Runner.SessionInfo.MaxPlayers > 0) ? Runner.SessionInfo.MaxPlayers : 4;
        }

        mode = (mode == (int)MatchMode.Team) ? (int)MatchMode.Team : (int)MatchMode.Solo;
        max = (mode == (int)MatchMode.Team) ? TEAM_FIXED : Mathf.Clamp(max, SOLO_MIN, SOLO_MAX);

        ModeInt = mode;
        MapInt = map;
        MaxPlayers = max;
    }


    // ===== 프로필/레디 =====
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_SubmitProfile(PlayerRef who, string nick, string name, int level, string photoUrl, RpcInfo info = default)
    {
        if (!Object.HasStateAuthority) return;

        nick = (nick ?? "").Trim();
        if (nick.Length == 0) nick = $"Player_{who.PlayerId}";
        if (nick.Length > 16) nick = nick.Substring(0, 16);

        name = (name ?? "").Trim();
        if (name.Length == 0) name = "-";
        if (name.Length > 32) name = name.Substring(0, 32);

        photoUrl = (photoUrl ?? "").Trim();
        // NetworkString<_256> 초과 방지(안전)
        if (photoUrl.Length > 256) photoUrl = photoUrl.Substring(0, 256);

        level = Mathf.Clamp(level, 1, 999);

        int idx = EnsureSlot(who);
        if (idx < 0) return;

        var s = Slots.Get(idx);
        s.nickname = nick;
        s.name = name;
        s.level = level;
        s.photoUrl = photoUrl;
        Slots.Set(idx, s);

        //Debug.Log($"[Slots] who={who.PlayerId} idx={idx} url='{photoUrl}'");

        // 리더가 아직 없으면 선출
        if (Leader == PlayerRef.None)
            ServerElectLeader();

        BumpRevision();
    }

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

    private int EnsureSlot(PlayerRef who)
    {
        int existing = FindSlot(who);
        if (existing >= 0) return existing;

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

        if (changed) BumpRevision(); // OnPlayerLeft로 빠져도 UI 갱신 보장
    }

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
}
