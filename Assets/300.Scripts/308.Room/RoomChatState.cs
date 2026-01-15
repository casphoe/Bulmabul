using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// RoomChatState (씬에 1개)
///
/// [변경 핵심]
///  기존: NetworkArray<ChatMessage> 로 "채팅 히스토리 100개"를 네트워크 상태로 들고 있었음
///    -> Capacity가 100이면 "항상 100칸 전체"가 State로 잡혀서 32KB 제한을 쉽게 초과함.
///    -> 특히 NetworkString<_256> 같은 큰 문자열이 100개면 거의 무조건 터짐.
///
///  변경: 채팅은 "네트워크 상태로 저장하지 않고"
///    1) 클라 -> 서버 : RPC_SendChatToServer(message)
///    2) 서버 -> 모두 : RPC_BroadcastChat(sender, nick, msg, time, seq)
///    3) 각 클라는 로컬(List)로 히스토리 N개만 유지 + UI 갱신
///
/// 장점:
/// - NetworkObject State 크기 거의 0 → 씬 로드 시 AllocateObject 터짐 방지
/// - 채팅은 이벤트 성격이라 RPC가 더 적합
/// - UI는 "Revision" 대신 이벤트로 갱신 가능
/// </summary>
public class RoomChatState : NetworkBehaviour
{
    public static RoomChatState Instance { get; private set; }

    // =========================
    // [로컬 히스토리 설정]
    // =========================

    [Header("로컬에 유지할 채팅 히스토리 개수(네트워크 아님)")]
    [SerializeField] private int localHistoryLimit = 100;

    /// <summary>
    /// UI/로직에서 쓰는 "로컬 채팅 메시지" 구조체 (네트워크 상태 아님)
    /// - 각 클라에서만 보관
    /// </summary>
    [Serializable]
    public struct LocalChatMessage
    {
        public PlayerRef sender;
        public string nickname;
        public string text;
        public int unixTime;
        public int seq;

        public ChatChannel channel;
        public TeamSide team;
    }

    // 로컬 히스토리(각 클라 개별 보관)
    private readonly List<LocalChatMessage> _history = new List<LocalChatMessage>(128);

    /// <summary>
    /// UI가 구독할 이벤트
    /// - 새 메시지가 들어오면 UI가 이 이벤트를 받아서 "한 줄 추가" 방식으로 갱신 가능
    /// </summary>
    public event Action<LocalChatMessage> OnChatReceived;

    // 서버에서만 쓰는 시퀀스(정렬/중복 방지용)
    [Networked] private int _serverSeq { get; set; }

    // (서버 전용) 스팸 방지
    private readonly Dictionary<PlayerRef, double> _lastSendTime = new();
    private const double SEND_COOLDOWN_SEC = 0.25;

    public override void Spawned()
    {
        Instance = this;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this) Instance = null;
    }

    #region 외부 UI 에서 사용되는 ApI

    /// <summary>
    /// UI에서 "현재 로컬 히스토리" 스냅샷이 필요할 때 사용
    /// - 예: 채팅창 열 때 한 번 전체 그리기
    /// </summary>
    public void CopyHistoryTo(List<LocalChatMessage> dst)
    {
        dst.Clear();
        dst.AddRange(_history);
    }

    /// <summary>
    /// 해당 PlayerRef가 현재 방 멤버인지 검사
    /// - RoomMembersState 슬롯에 존재하는지로 판단
    /// </summary>
    private bool IsRoomMember(PlayerRef who)
    {
        var members = RoomMembersState.Instance;
        if (members == null) return false;

        // MaxSlots = 4 고정이면 그냥 4로 돌려도 됨
        for (int i = 0; i < RoomMembersState.MaxSlots; i++)
        {
            var s = members.Slots.Get(i);
            if (s.occupied == 1 && s.player == who) return true;
        }
        return false;
    }

    /// <summary>
    /// 닉네임을 서버에서 다시 조회(스푸핑 방지)
    /// </summary>
    private string GetNickname(PlayerRef who)
    {
        var members = RoomMembersState.Instance;
        if (members == null) return $"Player_{who.PlayerId}";

        for (int i = 0; i < RoomMembersState.MaxSlots; i++)
        {
            var s = members.Slots.Get(i);
            if (s.occupied == 1 && s.player == who)
            {
                var nick = s.nickname.ToString();
                if (!string.IsNullOrWhiteSpace(nick)) return nick.Trim();
                break;
            }
        }
        return $"Player_{who.PlayerId}";
    }

    #endregion

    #region 메시지 정리 - 용량 줄이기

    /// <summary>
    /// 채팅 텍스트 정리
    /// - 줄바꿈 제거, trim
    /// - 길이 제한 ( 256은 길어서 네트워크/성능에 부담. 80~120 추천)
    /// </summary>
    private static string Sanitize(string msg, int maxLen = 120)
    {
        msg ??= "";
        msg = msg.Replace("\r", " ").Replace("\n", " ").Trim();

        if (msg.Length > maxLen)
            msg = msg.Substring(0, maxLen);

        return msg;
    }

    #endregion

    #region 클라에서 -> 서버 채팅 전송

    /// <summary>
    /// 클라이언트가 호출하는 채팅 전송 함수
    ///
    /// 호출 흐름:
    /// - UI에서 RoomChatState.Instance.SendChatFromUI("안녕") 호출
    /// - 내부에서 RPC_SendChatToServer("안녕") 실행
    /// - 서버(=StateAuthority)가 검사/정리 후 브로드캐스트 RPC 호출
    /// </summary>
    public void SendChatFromUI(string message, ChatChannel channel = ChatChannel.Global)
    {
        // Runner/오브젝트 준비 안 됐으면 무시
        if (Runner == null)
        {
            Debug.LogWarning("[Chat] Runner null (SendChatFromUI)");
            return;
        }

        RPC_SendChatToServer(message, (int)channel);
    }

    /// <summary>
    /// [RPC] 클라 -> 서버(권한자) 전송
    /// - "방 멤버만" 허용
    /// - 스팸 방지
    /// - 서버가 브로드캐스트를 수행
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_SendChatToServer(string message, int channelInt, RpcInfo info = default)
    {
        // 서버(=StateAuthority)에서만 처리
        if (!Object.HasStateAuthority) return;
        if (Runner == null) return;

        // 보낸 사람
        var sender = info.Source;
        if (sender == PlayerRef.None)
            sender = Runner.LocalPlayer;

        Debug.Log($"[Chat] (Server) RPC_SendChatToServer sender={sender} msg={message}");

        // 방 멤버만 채팅 가능
        if (sender == PlayerRef.None || !IsRoomMember(sender))
            return;

        // 스팸 방지(쿨타임)
        double now = Runner.SimulationTime;
        if (_lastSendTime.TryGetValue(sender, out var last) && now - last < SEND_COOLDOWN_SEC)
            return;
        _lastSendTime[sender] = now;

        // 메시지 정리
        string msg = Sanitize(message, maxLen: 120);
        if (string.IsNullOrWhiteSpace(msg))
            return;

        // 닉네임은 서버에서 다시 읽기(스푸핑 방지)
        string nick = GetNickname(sender);
        if (nick.Length > 32) nick = nick.Substring(0, 32);

        // 서버 시퀀스 증가
        int seq = ++_serverSeq;

        // 시간(UTC seconds)
        int unix = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // 채널 정규화
        ChatChannel channel = (channelInt == (int)ChatChannel.Party) ? ChatChannel.Party : ChatChannel.Global;

        // Party는 "팀 모드"에서만 허용 + sender의 팀을 구해둠
        var members = RoomMembersState.Instance;
        TeamSide senderTeam = TeamSide.None;

        bool isTeamMode = (members != null && members.ModeInt == (int)MatchMode.Team);
        if (isTeamMode)
            senderTeam = members.GetTeamByPlayer(sender);

        if (channel == ChatChannel.Party)
        {
            if (!isTeamMode)
            {
                Debug.LogWarning($"[Chat] Drop Party msg in SOLO. sender={sender} msg={msg}");
                return;
            }
            if (senderTeam == TeamSide.None)
            {
                Debug.LogWarning($"[Chat] Drop Party msg no team. sender={sender} msg={msg}");
                return;
            }
        }

        int teamInt = isTeamMode ? (int)senderTeam : (int)TeamSide.None;

        Debug.Log($"[Chat] (Server) Broadcast sender={sender} nick={nick} seq={seq}");

        // 서버 -> 모두(수신 후 클라에서 같은 팀만 저장하도록 필터링)
        RPC_BroadcastChat(sender, nick, msg, unix, seq, (int)channel, teamInt);
    }
    #endregion

    #region 서버에서 모두 

    /// <summary>
    /// [RPC] 서버(권한자) -> 모든 클라이언트에게 채팅 이벤트 전달
    ///
    /// 여기서 하는 일:
    /// 1) 로컬 히스토리에 저장(각 클라)
    /// 2) UI 갱신 이벤트(OnChatReceived) 호출
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastChat(PlayerRef sender, string nick, string msg, int unixTime, int seq,int channelInt,int teamInt, RpcInfo info = default)
    {
        Debug.Log($"[Chat] recv channelInt={channelInt} resolved={(channelInt == (int)ChatChannel.Party ? "Party" : "Global")} teamInt={teamInt} mode={(RoomMembersState.Instance != null ? RoomMembersState.Instance.ModeInt : -999)}");


        // 1) 채널 복원 (일단 Party/Global만)
        ChatChannel channel = (channelInt == (int)ChatChannel.Party) ? ChatChannel.Party : ChatChannel.Global;
        TeamSide senderTeam = (TeamSide)teamInt;

        var members = RoomMembersState.Instance;

        // 방어 1: 팀모드가 아닌데 Party로 왔으면 -> Global로 강등
        if (channel == ChatChannel.Party)
        {
            if (members == null || members.ModeInt != (int)MatchMode.Team)
            {
                channel = ChatChannel.Global;
                senderTeam = TeamSide.None;
            }
            else
            {
                //  방어 2: 팀모드인데 senderTeam이 None이면 -> Global로 강등
                if (senderTeam == TeamSide.None)
                {
                    channel = ChatChannel.Global;
                }
            }
        }

        // 2) Party(팀채팅)일 때만 같은 팀 필터링
        if (channel == ChatChannel.Party)
        {
            if (members == null) return; // (위에서 강등되었어야 하지만 안전빵)
            var me = members.Runner != null ? members.Runner.LocalPlayer : PlayerRef.None;
            if (me == PlayerRef.None) return;

            TeamSide myTeam = members.GetTeamByPlayer(me);
            if (myTeam == TeamSide.None) return;

            if (senderTeam != myTeam)
                return; //  다른 팀이면 팀채팅에서만 차단
        }


        // 3) 최종 저장(글로벌이면 team=None)
        var item = new LocalChatMessage
        {
            sender = sender,
            nickname = nick,
            text = msg,
            unixTime = unixTime,
            seq = seq,
            channel = channel,
            team = (channel == ChatChannel.Party) ? senderTeam : TeamSide.None
        };

        AppendToLocalHistory(item);

        // UI에게 "새 메시지 왔다" 알림
        OnChatReceived?.Invoke(item);
    }

    /// <summary>
    /// 로컬 히스토리에 추가 + 개수 제한 유지
    /// - localHistoryLimit 초과하면 앞에서 제거
    /// </summary>
    private void AppendToLocalHistory(LocalChatMessage item)
    {
        _history.Add(item);

        int limit = Mathf.Clamp(localHistoryLimit, 10, 500);
        int overflow = _history.Count - limit;
        if (overflow > 0)
        {
            // 앞에서 overflow개 삭제
            _history.RemoveRange(0, overflow);
        }
    }

    #endregion
}
