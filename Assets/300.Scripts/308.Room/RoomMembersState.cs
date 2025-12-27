using Fusion;
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

    [Networked, Capacity(MaxSlots)]
    public NetworkArray<MemberSlot> Slots => default;

    public override void Spawned()
    {
        Instance = this;

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

        // 리더였으면 새 리더 뽑기
        if (RoomState.instance != null)
            RoomState.instance.ServerOnPlayerLeft(who);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestLeave(PlayerRef who, RpcInfo info = default)
    {
        if (!Object.HasStateAuthority) return;

        // 슬롯 제거(여기서 리더 재선출도 같이 호출되도록 Server_RemovePlayer에 연결해둔 상태면 끝)
        Server_RemovePlayer(who);

        // 만약 Server_RemovePlayer에서 리더 선출 호출을 안 한다면 여기서 호출:
        // if (RoomState.instance != null) RoomState.instance.ServerOnPlayerLeft(who);
    }
}
