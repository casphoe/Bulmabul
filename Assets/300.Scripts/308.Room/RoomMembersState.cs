using Fusion;
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
        public int level;
        public NetworkBool ready;
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


    public MatchMode Mode => (MatchMode)ModeInt;
    public int Map => MapInt;

    private const int SOLO_MIN = 2;
    private const int SOLO_MAX = 4;
    private const int TEAM_FIXED = 4;

    public override void Spawned()
    {
        Instance = this;

        // 서버(권한자)가 최초 룸 설정 초기화 + 리더 선출
        if (Object.HasStateAuthority)
        {
            if (RoomTitle.ToString().Length == 0)
            {
                string defaultTitle = (Runner != null && Runner.SessionInfo.IsValid) ? Runner.SessionInfo.Name : "Room";
                RoomTitle = defaultTitle;
            }

            ServerInitSettingsFromSessionProperties();
            if (Leader == PlayerRef.None)
                ServerElectLeader();
        }

        // 로컬 플레이어: Firebase에서 내 정보 가져와 서버에 제출
        if (Runner.LocalPlayer != PlayerRef.None)
        {
            string nick = $"Player_{Runner.LocalPlayer.PlayerId}";
            int level = 1;

            var fb = FireBaseAuthManager.Instance;
            if (fb != null && fb.IsReady && fb.CurrentAccount != null)
            {
                nick = fb.CurrentAccount.NickName;
                level = fb.CurrentAccount.AccountLevel;
            }

            RPC_SubmitProfile(Runner.LocalPlayer, nick, level);
        }
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
    public void RPC_SubmitProfile(PlayerRef who, string nick, int level, RpcInfo info = default)
    {
        if (!Object.HasStateAuthority) return;

        nick = (nick ?? "").Trim();
        if (nick.Length == 0) nick = $"Player_{who.PlayerId}";
        if (nick.Length > 16) nick = nick.Substring(0, 16);
        level = Mathf.Clamp(level, 1, 999);

        int idx = EnsureSlot(who);
        if (idx < 0) return;

        var s = Slots.Get(idx);
        s.nickname = nick;
        s.level = level;
        Slots.Set(idx, s);

        // 리더가 아직 없으면 선출
        if (Leader == PlayerRef.None)
            ServerElectLeader();
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
    }

    // ===== 리더 위임 (리더만) =====
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestTransferLeader(PlayerRef newLeader, RpcInfo info = default)
    {
        if (!Object.HasStateAuthority) return;

        var requester = info.Source;
        if (requester != Leader) return;
        if (newLeader == PlayerRef.None) return;

        // newLeader가 방 멤버인지 확인
        bool exists = false;
        for (int i = 0; i < MaxSlots; i++)
        {
            var s = Slots.Get(i);
            if (s.occupied == 1 && s.player == newLeader) { exists = true; break; }
        }
        if (!exists) return;

        Leader = newLeader;
    }

    // ===== 강퇴 (리더만) =====
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestKick(PlayerRef target, RpcInfo info = default)
    {
        if (!Object.HasStateAuthority) return;

        var requester = info.Source;
        if (requester != Leader) return;
        if (target == PlayerRef.None) return;
        if (target == requester) return;

        bool exists = false;
        for (int i = 0; i < MaxSlots; i++)
        {
            var s = Slots.Get(i);
            if (s.occupied == 1 && s.player == target) { exists = true; break; }
        }
        if (!exists) return;

        Server_RemovePlayer(target);
        Runner.Disconnect(target);
    }

    // ===== 나가기 =====
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestLeave(PlayerRef who, RpcInfo info = default)
    {
        if (!Object.HasStateAuthority) return;
        Server_RemovePlayer(who);
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
                s.level = 1;
                s.ready = false;
                Slots.Set(i, s);
                return i;
            }
        }
        return -1;
    }

    public void Server_RemovePlayer(PlayerRef who)
    {
        if (!Object.HasStateAuthority) return;

        int idx = FindSlot(who);
        if (idx >= 0)
        {
            var s = Slots.Get(idx);
            s.occupied = 0;
            s.player = default;
            s.nickname = default;
            s.level = 1;
            s.ready = false;
            Slots.Set(idx, s);
        }

        // 리더였으면 새 리더 선출
        if (Leader == who)
            ServerElectLeader();
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

        Leader = found;
    }

    // ===== 표시용 룸 타이틀(세션 이름과 별개로 "룸 안에서"만 동기화) =====
    [Networked] public NetworkString<_32> RoomTitle { get; set; }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestChangeRoomTitle(string newTitle, RpcInfo info = default)
    {
        if (!Object.HasStateAuthority) return;

        var requester = info.Source;
        if (requester != Leader) return; // ✅ 리더만

        newTitle = (newTitle ?? "").Trim();
        if (newTitle.Length == 0) newTitle = "Room";
        if (newTitle.Length > 32) newTitle = newTitle.Substring(0, 32);

        RoomTitle = newTitle;
    }
}
