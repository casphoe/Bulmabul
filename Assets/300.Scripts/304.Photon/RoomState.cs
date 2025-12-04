using Fusion;
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

    /// <summary>
    /// 방장 넘기기 요청
    /// - 누구나 요청은 보낼 수 있지만
    /// - 서버(StateAuthority)가 "요청자 == 현재 방장"일 때만 반영
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestTransferLeader(PlayerRef requester, PlayerRef newLeader)
    {
        if (requester != Leader) return;
        Leader = newLeader;
    }

    /// <summary>
    /// 강퇴 요청
    /// - 최종 Disconnect는 서버(StateAuthority)에서만 수행
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestKick(PlayerRef requester, PlayerRef target)
    {
        if (requester != Leader) return;
        Runner.Disconnect(target); // Runner는 NetworkBehaviour에서 접근 가능
    }
}
