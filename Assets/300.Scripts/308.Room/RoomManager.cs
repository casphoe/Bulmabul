using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Fusion;
using Gpm.Ui;
using UnityEngine.SceneManagement;
using System.Threading.Tasks;

public struct RoomMemberInfo
{
    public PlayerRef player;
    public string nickname;
    public int level;
    public bool isLeader;
    public bool isReady;
    public int slotIndex;   // Player1/2/3… 순서
    public bool isMe;
}

public class RoomManager : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] TMP_InputField inputRoomTitle;

    [SerializeField] Button btnReady;
    [SerializeField] Button btnGameStart;

    [SerializeField] Button btnLeave;

    [Header("Players List")]
    [SerializeField] InfiniteScroll playerScroll;

    [SerializeField] int maxSlots = 4;

    private bool _localReady;
    private float _nextRefresh;

    private void Start()
    {
        if (btnReady != null) btnReady.onClick.AddListener(OnClickReady);
        if (btnGameStart != null) btnGameStart.onClick.AddListener(OnClickStartGame);
        if (btnLeave != null) btnLeave.onClick.AddListener(OnClickLeave);

        // 첫 갱신
        RefreshPlayersUI();
        UpdateButtons();
    }

    private void Update()
    {
        // 방 상태 변화(입장/퇴장/레디/리더) 반영용 - 가벼운 폴링
        if (Time.time >= _nextRefresh)
        {
            _nextRefresh = Time.time + 0.2f;
            RefreshPlayersUI();
            UpdateButtons();
        }
    }

    private void OnClickReady()
    {
        var members = RoomMembersState.Instance;
        if (members == null) return;

        _localReady = !_localReady;
        members.RPC_SetReady(members.Runner.LocalPlayer, _localReady);
    }

    private void OnClickStartGame()
    {
        // 리더만 시작 가능 + 전원 Ready 체크
        var members = RoomMembersState.Instance;
        if (members == null) return;

        var roomState = RoomState.instance;
        if (roomState == null) return;

        var me = members.Runner.LocalPlayer;
        if (roomState.Leader == PlayerRef.None || roomState.Leader != me) return;

        // 전원 Ready 검사(occupied만)
        for (int i = 0; i < maxSlots; i++)
        {
            var s = members.Slots.Get(i);
            if (s.occupied == 0) continue;
            if (!s.ready) return;
        }

        // TODO: 실제 게임 시작 로직
        // NetWorkLauncher.instance.StartGameFromRoom();
    }

    private async void OnClickLeave()
    {
        var members = RoomMembersState.Instance;
        if (members == null) return;

        var me = members.Runner.LocalPlayer;

        // 1) 서버(권한자)에게 "나 나갈게" 요청 -> 슬롯 정리 + 리더 재선출
        // (나중에 Disconnect/Shutdown에서도 OnPlayerLeft로 한 번 더 정리될 수 있는데,
        //  Server_RemovePlayer가 idempotent(이미 없으면 무시)면 안전함)
        members.RPC_RequestLeave(me);

        // 2) 로컬은 러너 종료 후 로비 씬(1번) 이동
        // NetWorkLauncher에 Runner가 있다면 거기서 Shutdown을 담당하는게 가장 깔끔
        if (NetWorkLauncher.instance != null)
        {
            //await NetWorkLauncher.instance.LeaveRoomToLobby(1); // 아래에 함수 만들어줄게
            return;
        }

        // 런처가 없으면 여기서 직접 종료
        if (members.Runner != null)
            await members.Runner.Shutdown();

        SceneManager.LoadScene(1, LoadSceneMode.Single);
    }

    private void UpdateButtons()
    {
        var members = RoomMembersState.Instance;
        var roomState = RoomState.instance;

        bool amLeader = false;
        if (members != null && roomState != null && roomState.Leader != PlayerRef.None)
            amLeader = (roomState.Leader == members.Runner.LocalPlayer);

        if (btnGameStart != null)
            btnGameStart.interactable = amLeader;
    }

    /// <summary>
    /// 현재 방 참가자 정보를 읽어서 GPM InfiniteScroll에 뿌린다.
    /// - maxSlots(기본 4) 기준으로 "빈 슬롯도 표시"한다.
    /// </summary>
    private void RefreshPlayersUI()
    {
        if (playerScroll == null) return;

        var members = RoomMembersState.Instance;
        var roomState = RoomState.instance;
        if (members == null || roomState == null) return;

        var me = members.Runner.LocalPlayer;
        var leader = roomState.Leader;

        // 1) 슬롯 데이터 만들기
        // 여기서 RoomPlayer(셀)가 받는 Data 타입이 너 프로젝트에서 무엇인지에 따라 맞춰야 함.
        // 네가 RoomPlayer(셀)에서 RoomPlayerData를 쓴다고 하면 RoomPlayerData로 만들어서 Insert하면 됨.
        // 우선 RoomMemberInfo 리스트로 만들고, 이후 Data 변환해서 넣어줄게.

        List<RoomMemberInfo> list = new List<RoomMemberInfo>(maxSlots);

        for (int i = 0; i < maxSlots; i++)
        {
            var s = members.Slots.Get(i);

            if (s.occupied == 0)
            {
                list.Add(new RoomMemberInfo
                {
                    slotIndex = i,
                    player = default,
                    nickname = "-",
                    level = 0,
                    isLeader = false,
                    isReady = false,
                    isMe = false
                });
                continue;
            }

            list.Add(new RoomMemberInfo
            {
                slotIndex = i,
                player = s.player,
                nickname = s.nickname.ToString(),
                level = s.level,
                isLeader = (leader != PlayerRef.None) && s.player == leader,
                isReady = s.ready,
                isMe = (s.player != PlayerRef.None && s.player == me)
            });
        }

        // 2) InfiniteScroll 갱신
        // 너 프로젝트에서 쓰는 InfiniteScroll API가 버전마다 달라서,
        // 이전에 말한 InfiniteScrollUtil (ClearAll/Insert/UpdateAll)로 처리하는게 안전함.

        RoomInfiniteScrollUtil.ClearAll(playerScroll);

        // RoomPlayer(셀)이 어떤 Data 타입을 받는지에 맞게 넣어야 함.
        // 여기서는 "RoomPlayerData"를 사용한다고 가정하고 변환해서 넣음.
        // (만약 네 셀 스크립트가 RoomMemberInfo를 직접 받게 만들고 싶으면, RoomMemberInfo를 InfiniteScrollData로 바꿔야 함)

        foreach (var m in list)
        {
            var data = new RoomPlayerData
            {
                slotIndex = m.slotIndex,
                isEmpty = (m.nickname == "-" || m.player == PlayerRef.None),
                nickname = m.nickname,
                level = m.level,
                isLeader = m.isLeader,
                isReady = m.isReady,
                isMe = (m.player != PlayerRef.None && m.player == me)
            };

            RoomInfiniteScrollUtil.Insert(playerScroll, data);
        }

        RoomInfiniteScrollUtil.UpdateAll(playerScroll);
    }
}
