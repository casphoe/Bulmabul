using Fusion;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 방의 "게임 규칙상 방장(Leader)"과 모드 등을 네트워크로 공유.
/// - Host(서버)가 StateAuthority를 갖고 Networked 값을 갱신
/// - 클라들은 값을 읽기만 함
/// - 방장 넘기기/강퇴는 RPC로 서버에게 요청 -> 서버가 실행
/// </summary>
public class RoomState : NetworkBehaviour
{
    public static RoomState instance;

    [Networked] public PlayerRef Leader { get; set; }
    [Networked] public int ModeInt { get; set; } // MatchMode를 int로 저장

    public override void Spawned()
    {
        // 네트워크 스폰이 완료된 시점(모든 피어에서 호출)
        instance = this;

        // (선택) 새로 권한자가 된 경우 리더 비어있으면 자동 선출
        if (Object.HasStateAuthority && Leader == PlayerRef.None)
            ServerElectLeader();
    }

    /// <summary>
    /// 서버(Host)가 최초 1회 초기화.
    /// Object.HasStateAuthority == true 인 쪽(보통 Host)만 값을 세팅할 수 있음.
    /// </summary>
    public void ServerInit(PlayerRef leader, int modeInt)
    {
        if (!Object.HasStateAuthority) return; // 서버만 쓰도록 가드
        Leader = leader;
        ModeInt = modeInt;
    }

    public void ServerOnPlayerLeft(PlayerRef left)
    {
        if (!Object.HasStateAuthority) return;

        if (Leader == left)
            ServerElectLeader();
    }

    public void ServerElectLeader()
    {
        if (!Object.HasStateAuthority) return;

        var members = RoomMembersState.Instance;
        if (members == null)
        {
            Leader = PlayerRef.None;
            return;
        }

        List<PlayerRef> alive = new List<PlayerRef>();
        for (int i = 0; i < RoomMembersState.MaxSlots; i++)
        {
            var s = members.Slots.Get(i);
            if (s.occupied == 1)
                alive.Add(s.player);
        }

        if (alive.Count == 0)
        {
            Leader = PlayerRef.None;
            return;
        }

        // “랜덤” (권한자가 결정 -> 네트워크로 동기화)
        int r = Random.Range(0, alive.Count);
        Leader = alive[r];
    }


    /// <summary>
    /// 방장 넘기기 요청
    /// - 누구나 요청은 보낼 수 있지만
    /// - 서버(StateAuthority)가 "요청자 == 현재 방장"일 때만 반영
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestTransferLeader(PlayerRef requester, PlayerRef newLeader)
    {
        if (!Object.HasStateAuthority) return;

        // 요청자가 현재 리더가 아니면 무시
        if (requester != Leader) return;

        // newLeader 유효성(버전 호환: None 체크)
        if (newLeader == PlayerRef.None) return;

        // newLeader가 현재 방 멤버인지 확인
        var members = RoomMembersState.Instance;
        if (members == null) return;

        bool exists = false;
        for (int i = 0; i < RoomMembersState.MaxSlots; i++)
        {
            var s = members.Slots.Get(i);
            if (s.occupied == 1 && s.player == newLeader)
            {
                exists = true;
                break;
            }
        }

        if (!exists) return;

        // 통과하면 리더 변경
        Leader = newLeader;
    }

    /// <summary>
    /// 강퇴 요청
    /// - 최종 Disconnect는 서버(StateAuthority)에서만 수행
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestKick(PlayerRef requester, PlayerRef target)
    {
        if (!Object.HasStateAuthority) return;

        // 1) 리더만 가능
        if (requester != Leader) return;

        // 2) target 유효성
        if (target == PlayerRef.None) return;

        // 3) 자기 자신 강퇴 금지 (리더가 자기 자신도 못 함)
        if (target == requester) return;

        // 4) target이 방 멤버인지 확인
        var members = RoomMembersState.Instance;
        if (members == null) return;

        bool exists = false;
        for (int i = 0; i < RoomMembersState.MaxSlots; i++)
        {
            var s = members.Slots.Get(i);
            if (s.occupied == 1 && s.player == target)
            {
                exists = true;
                break;
            }
        }
        if (!exists) return;

        // 5) 슬롯 정리(권장: UI 즉시 반영)
        members.Server_RemovePlayer(target);

        // 6) 연결 끊기
        Runner.Disconnect(target);
    }
}
