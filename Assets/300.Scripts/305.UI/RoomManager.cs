using Fusion;
using Gpm.Ui;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// RoomManager
/// - 룸(게임씬) 내부 UI 전부 담당:
///   1) 참가자 리스트(ready/leader 표시)
///   2) 룸 설정 표시/변경(팀전/개인전, 최대 인원, 맵)
/// - 룸 설정 변경은 "리더만" 가능:
///   (1) UI interactable 잠금
///   (2) 서버 RPC에서 최종 검증(권장: RoomSettingsState에서 리더 검증)
///
/// 전제:
/// - RoomMembersState, RoomState는 너 프로젝트에 이미 존재(ready/slot/leader 관리)
/// - RoomInfiniteScrollUtil, RoomPlayerData는 기존 사용 그대로
/// - RoomSettingsState(네트워크 동기화) 사용 권장:
///   RoomSettingsState.Instance.RPC_RequestChangeSettings(mode,map,soloMax)
/// </summary>

public struct RoomMemberInfo
{
    public PlayerRef player;
    public string nickname;
    public string name;
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

    [Header("UI - Room Settings (리더만 변경 가능)")]
    [SerializeField] Toggle toggleSolo;
    [SerializeField] Toggle toggleTeam;

    [Tooltip("개인전 최대 인원 입력(2~4). 팀전은 4 고정")]
    [SerializeField] TMP_InputField inputMaxPlayers;

    [Header("Map UI")]
    [SerializeField] Image mapImage;

    [SerializeField] Button[] btnMapSelect;

    [SerializeField] Text txtMapName;

    [Header("Players List")]
    [SerializeField] InfiniteScroll playerScroll;

    [Header("Team UI")]
    [SerializeField] GameObject teamUiObject;

    [SerializeField] int maxSlots = 4;

    [Header("Room Invite")]
    [SerializeField] private RoomInvitePopup roomInvitePopup;

    private bool _localReady;
    private int _lastRevision = -1;
    private float _nextLightTick = 0f;

    // settings cache
    private MatchMode _mode = MatchMode.Solo;
    private int _map = 0;            // 0=Korea, 1=USA
    private int _maxPlayers = 4;     // 표시/제한용 (팀전=4 고정)
    private bool _suppressSettingCallback;
    private bool _suppressTitleCallback;

    // 룰
    private const int SOLO_MIN = 2;
    private const int SOLO_MAX = 4;
    private const int TEAM_FIXED = 4;

    private bool _readyPending = false;
    private bool _readyPendingValue = false;
    private float _readyPendingUntil = 0f;
    private const float READY_PENDING_TIMEOUT = 0.6f; // 0.4~0.8 사이 추천

    private float _readyClickCooldownUntil = 0f;
    private const float READY_CLICK_COOLDOWN = 0.12f; // 연타만 방지(취소는 가능)

    private RoomChatUIManager chatUiManager;

    private void Start()
    {
        if (btnReady != null) btnReady.onClick.AddListener(OnClickReady);
        if (btnGameStart != null) btnGameStart.onClick.AddListener(OnClickStartGame);
        if (btnLeave != null) btnLeave.onClick.AddListener(OnClickLeave);
        chatUiManager = GetComponent<RoomChatUIManager>();
        // --- 모드 토글 ---
        if (toggleSolo != null)
        {
            toggleSolo.onValueChanged.RemoveListener(OnSoloToggleChanged);
            toggleSolo.onValueChanged.AddListener(OnSoloToggleChanged);
        }
        if (toggleTeam != null)
        {
            toggleTeam.onValueChanged.RemoveListener(OnTeamToggleChanged);
            toggleTeam.onValueChanged.AddListener(OnTeamToggleChanged);
        }

        // --- 최대인원 입력 ---
        if (inputMaxPlayers != null)
        {
            inputMaxPlayers.contentType = TMP_InputField.ContentType.IntegerNumber;
            inputMaxPlayers.onValueChanged.RemoveListener(OnMaxPlayersValueChanged);
            inputMaxPlayers.onValueChanged.AddListener(OnMaxPlayersValueChanged);

            inputMaxPlayers.onEndEdit.RemoveListener(OnMaxPlayersEndEdit);
            inputMaxPlayers.onEndEdit.AddListener(OnMaxPlayersEndEdit);
        }

        if (inputRoomTitle != null)
        {
            inputRoomTitle.onEndEdit.RemoveListener(OnRoomTitleEndEdit);
            inputRoomTitle.onEndEdit.AddListener(OnRoomTitleEndEdit);
        }
        if (teamUiObject != null)
            teamUiObject.SetActive(false);
        UpdateReadyButtonText();

        // --- 맵 버튼 ---
        BindMapButtons();
    }

    private void Update()
    {
        // 방 상태 변화(입장/퇴장/레디/리더) 반영용 - 가벼운 폴링
        var m = Members;
        if (m == null || m.Runner == null) return;

        // 네트워크 상태가 바뀐 경우(Revision 변경)만 "무거운 갱신"
        if (m.Revision != _lastRevision)
        {
            _lastRevision = m.Revision;
            RefreshAllUI_Heavy();
            return;
        }

        // 가벼운 갱신은 짧은 주기(0.1초 정도)로만
        if (Time.time >= _nextLightTick)
        {
            _nextLightTick = Time.time + 0.1f;
            RefreshUI_Light();
        }
    }

    private void RefreshAllUI_Heavy()
    {
        // 상태가 바뀌면 한 번만:
        RefreshRoomTitle();
        RefreshRoomSettingsUI();
        RefreshPlayersUI();

        // 버튼/텍스트는 마지막에 가볍게 정리
        RefreshUI_Light();
    }

    private void RefreshUI_Light()
    {
        SyncLocalReadyFromNetwork();
        UpdateButtons();
    }

    #region Leader helper
    private RoomMembersState Members => RoomMembersState.Instance;
    private bool AmILeader()
    {
        var m = Members;
        if (m == null || m.Runner == null) return false;
        if (m.Leader == PlayerRef.None) return false;
        return m.Leader == m.Runner.LocalPlayer;
    }

    private int GetCurrentPlayersCount()
    {
        var m = Members;
        if (m == null) return 0;

        int cur = 0;
        int n = Mathf.Min(maxSlots, RoomMembersState.MaxSlots);
        for (int i = 0; i < n; i++)
        {
            var s = m.Slots.Get(i);
            if (s.occupied == 1) cur++;
        }
        return cur;
    }

    #endregion

    #region Room title
    private void RefreshRoomTitle()
    {
        if (inputRoomTitle == null) return;

        var m = Members;
        if (m == null || m.Runner == null) return;

        bool amLeader = AmILeader();

        _suppressTitleCallback = true;

        //  네트워크 동기화된 표시용 타이틀 사용
        string title = m.RoomTitle.ToString();
        if (string.IsNullOrWhiteSpace(title))
            title = m.Runner.SessionInfo.IsValid ? m.Runner.SessionInfo.Name : "";

        inputRoomTitle.SetTextWithoutNotify(title);

        _suppressTitleCallback = false;

        // 리더만 입력 가능
        inputRoomTitle.interactable = amLeader;
    }


    private void OnRoomTitleEndEdit(string value)
    {
        if (_suppressTitleCallback) return;

        // 리더 아니면 편집 불가 + 혹시 값 바뀌었으면 원복
        if (!AmILeader())
        {
            RefreshRoomTitle();
            return;
        }

        var m = Members;
        if (m == null) return;

        m.RPC_RequestChangeRoomTitle(value);
    }

    #endregion

    #region Buttons
    private void UpdateButtons()
    {
        bool amLeader = AmILeader();

        if (btnGameStart != null)
        {
            //  리더 + (리더 제외 전원 ready) 조건일 때만 true
            btnGameStart.interactable = CanLeaderStartGame();
        }

        ApplySettingsInteractable(amLeader);
        UpdateMapButtonsInteractable(_map, amLeader);
    }

    private void ApplySettingsInteractable(bool amLeader)
    {
        if (toggleSolo != null) toggleSolo.interactable = amLeader;
        if (toggleTeam != null) toggleTeam.interactable = amLeader;

        // 팀전이면 인원은 4 고정이라 입력 잠금
        if (inputMaxPlayers != null)
            inputMaxPlayers.interactable = amLeader && (_mode == MatchMode.Solo);
    }
    #endregion

    #region Ready / Start / Leave
    private void OnClickReady()
    {
        var m = Members;
        if (m == null || m.Runner == null) return;
        if (AmILeader()) return; // 리더는 Ready 버튼 자체가 안 보이지만 안전장치

        if (Time.time < _readyClickCooldownUntil) return;
        _readyClickCooldownUntil = Time.time + READY_CLICK_COOLDOWN;

        //  지금 화면에 보이는 값 기준으로 토글
        bool desired = !_localReady;

        //  UI 즉시 반영(깜빡임 방지)
        _localReady = desired;

        //  pending 갱신(서버 값이 따라오면 확정)
        _readyPending = true;
        _readyPendingValue = desired;
        _readyPendingUntil = Time.time + READY_PENDING_TIMEOUT;

        m.RPC_SetReady(m.Runner.LocalPlayer, desired);
        UpdateReadyButtonText();
    }

    private void OnClickStartGame()
    {
        var m = Members;
        if (m == null || m.Runner == null) return;

        // 리더만 시작 가능
        if (!AmILeader()) return;

        // 버튼 조건 검사
        if (!CanLeaderStartGame()) return;

        var leader = m.Leader;
        if (leader == PlayerRef.None) return;

        // 리더 제외 전원 Ready 검사
        int n = Mathf.Min(maxSlots, RoomMembersState.MaxSlots);

        for (int i = 0; i < n; i++)
        {
            var s = m.Slots.Get(i);

            if (s.occupied == 0) continue;

            // 방장은 Ready 검사 제외
            if (s.player == leader) continue;

            // 일반 참가자가 준비 안 했으면 시작 불가
            if (!s.ready)
            {
                Debug.LogWarning("[RoomManager] Cannot start. Not all members are ready.");
                return;
            }
        }

        // 중복 클릭 방지
        if (btnGameStart != null)
            btnGameStart.interactable = false;

        Debug.Log("[RoomManager] Request start game.");

        // StateAuthority 쪽에 게임 시작 요청
        m.RPC_RequestStartGame();
    }

    private async void OnClickLeave()
    {
        var m = Members;
        if (m == null || m.Runner == null) return;

        var me = m.Runner.LocalPlayer;

        // 서버에게 나가기 요청(슬롯 정리 + 리더 재선출)
        m.RPC_RequestLeave(me);

        await Task.Yield();

        // 런처가 있다면 런처가 Shutdown/씬이동 담당하는게 더 좋음
        if (NetWorkLauncher.instance != null)
        {
            // 네 프로젝트에 Leave 함수가 있으면 여기서 호출
            await NetWorkLauncher.instance.LeaveRoomToLobby(1);
            return;
        }

        // 런처가 없으면 직접 종료 후 로비 씬 이동
        await m.Runner.Shutdown();
        SceneManager.LoadScene(1, LoadSceneMode.Single);
    }
    #endregion

    #region Players UI
    /// <summary>
    /// 현재 방 참가자 정보를 읽어서 GPM InfiniteScroll에 뿌린다.
    /// - 현재 선택된 플레이 가능 인원 수(m.MaxPlayers)에 맞춰 슬롯 수를 표시
    /// - 개인전: m.MaxPlayers 기준으로 2~4개 표시
    /// - 팀전: 4개 고정 표시
    /// - 빈 슬롯도 표시해서 방장만 + 초대 버튼을 사용할 수 있게 함
    /// </summary>
    private void RefreshPlayersUI()
    {
        // 플레이어 목록을 표시할 InfiniteScroll이 없으면 갱신 불가
        if (playerScroll == null) return;

        // 현재 방 멤버 상태 가져오기
        var m = Members;

        // 방 멤버 상태 또는 Fusion Runner가 없으면 갱신 불가
        if (m == null || m.Runner == null) return;

        // 내 PlayerRef
        var me = m.Runner.LocalPlayer;

        // 현재 방장 PlayerRef
        var leader = m.Leader;

        // 내가 방장인지 확인
        // 방장일 때만 빈 슬롯에 + 초대 버튼을 보여줄 예정
        bool amLeader = AmILeader();

        // =========================
        // 표시할 슬롯 수 계산
        // =========================
        // 핵심:
        // maxSlots 전체를 무조건 보여주는 것이 아니라
        // 현재 방 설정의 최대 플레이어 수(m.MaxPlayers)에 맞춰 보여준다.
        //
        // 개인전:
        // - inputMaxPlayers에서 설정한 2~4명 기준으로 슬롯 표시
        //
        // 팀전:
        // - 팀전은 4명 고정이므로 4칸 표시
        int displaySlots = (_mode == MatchMode.Team)
            ? TEAM_FIXED
            : Mathf.Clamp(m.MaxPlayers, SOLO_MIN, SOLO_MAX);

        // 혹시 maxSlots 또는 RoomMembersState.MaxSlots보다 큰 값이 들어와도 안전하게 제한
        int n = Mathf.Min(displaySlots, maxSlots, RoomMembersState.MaxSlots);

        // 빈 슬롯까지 포함해서 RoomPlayerData 리스트 생성
        // 기존 RoomMemberInfo는 빈 슬롯 정보를 담기 애매하므로 RoomPlayerData를 직접 정렬함
        List<RoomPlayerData> list = new List<RoomPlayerData>(n);

        // 전체 슬롯 순회
        // n은 현재 플레이 가능 인원 수 기준이므로,
        // 개인전 2명 방이면 0~1 슬롯까지만 표시된다.
        for (int i = 0; i < n; i++)
        {
            // i번 슬롯 정보 가져오기
            var s = m.Slots.Get(i);

            // =========================
            // 빈 슬롯 처리
            // =========================
            if (s.occupied == 0)
            {
                // 빈 슬롯도 UI에 표시해야 + 버튼을 띄울 수 있음
                list.Add(new RoomPlayerData
                {
                    // 실제 방 슬롯 번호
                    slotIndex = i,

                    // 빈 슬롯 표시용 플래그
                    isEmpty = true,

                    // 빈 슬롯이므로 PlayerRef 없음
                    player = PlayerRef.None,

                    // 빈 슬롯이라 표시할 유저 정보 없음
                    nickname = "",
                    name = "",
                    level = 0,

                    // 빈 슬롯은 리더/준비/나 자신 아님
                    isLeader = false,
                    isReady = false,
                    isMe = false,

                    // 빈 슬롯은 프로필 URL 없음
                    photoUrl = "",

                    // 팀전일 경우 빈 슬롯도 팀 위치를 보여줄 수 있게 팀 계산
                    // 개인전이면 TeamSide.None
                    team = (_mode == MatchMode.Team)
                        ? GetTeamBySlotIndex(i)
                        : TeamSide.None,

                    // 방장만 빈 슬롯의 + 버튼을 볼 수 있음
                    canInviteHere = amLeader,

                    // + 버튼 클릭 시 RoomManager의 초대 팝업 함수 호출
                    onClickInviteEmptySlot = OnClickInviteEmptySlot
                });

                // 빈 슬롯 처리가 끝났으므로 다음 슬롯으로 이동
                continue;
            }

            // =========================
            // 실제 플레이어 슬롯 처리
            // =========================
            list.Add(new RoomPlayerData
            {
                // 실제 방 슬롯 번호
                slotIndex = i,

                // 플레이어가 있으므로 빈 슬롯 아님
                isEmpty = false,

                // Fusion PlayerRef
                player = s.player,

                // 네트워크 슬롯에 저장된 유저 정보
                nickname = s.nickname.ToString(),
                name = s.name.ToString(),
                level = s.level,

                // 현재 슬롯의 플레이어가 방장인지 확인
                isLeader = (leader != PlayerRef.None && s.player == leader),

                // 준비 상태
                isReady = s.ready,

                // 이 슬롯의 플레이어가 나인지 확인
                isMe = (s.player != PlayerRef.None && s.player == me),

                // 프로필 이미지 URL
                photoUrl = s.photoUrl.ToString(),

                // 팀전이면 슬롯 기준으로 팀 계산
                // 개인전이면 팀 없음
                team = (_mode == MatchMode.Team)
                    ? GetTeamBySlotIndex(i)
                    : TeamSide.None,

                // 실제 플레이어 슬롯에는 + 초대 버튼을 보여주면 안 됨
                canInviteHere = false,
                onClickInviteEmptySlot = null
            });
        }

        // =========================
        // UI 정렬 규칙
        // =========================
        list.Sort((a, b) =>
        {
            if (_mode == MatchMode.Team)
            {
                // 팀전은 자리 고정이 중요함
                // 리더가 바뀌어도 0,1,2,3 슬롯 순서를 그대로 유지
                return a.slotIndex.CompareTo(b.slotIndex);
            }
            else
            {
                // 개인전은 방장을 항상 위로 올림
                // true가 false보다 앞으로 오도록 b와 a를 비교
                int leaderCmp = b.isLeader.CompareTo(a.isLeader);
                if (leaderCmp != 0) return leaderCmp;

                // 그 다음은 실제 플레이어를 빈 슬롯보다 먼저 표시
                // isEmpty false가 true보다 먼저 오게 됨
                int emptyCmp = a.isEmpty.CompareTo(b.isEmpty);
                if (emptyCmp != 0) return emptyCmp;

                // 나머지는 슬롯 번호 순서 유지
                return a.slotIndex.CompareTo(b.slotIndex);
            }
        });

        // =========================
        // InfiniteScroll 갱신
        // =========================

        // 기존 플레이어 UI 제거
        RoomInfiniteScrollUtil.ClearAll(playerScroll);

        // 정렬된 순서대로 UI에 삽입
        for (int i = 0; i < list.Count; i++)
        {
            RoomInfiniteScrollUtil.Insert(playerScroll, list[i], i);
        }

        // InfiniteScroll 화면 갱신
        RoomInfiniteScrollUtil.UpdateAll(playerScroll);
    }

    // 빈 슬롯의 + 버튼을 눌렀을 때 실행되는 함수
    // slotIndex는 몇 번째 빈 슬롯을 눌렀는지 알려주는 값
    private void OnClickInviteEmptySlot(int slotIndex)
    {
        // 방장이 아니면 친구 초대 불가
        if (!AmILeader())
        {
            ToastMessageManager.instance?.ShowToast(
                "방장만 친구를 초대할 수 있습니다.",
                "Only the leader can invite friends."
            );
            return;
        }

        // 현재 방 멤버 정보 확인
        var m = Members;
        if (m == null || m.Runner == null)
            return;

        // 현재 인원 수 확인
        int currentPlayers = GetCurrentPlayersCount();

        // 현재 모드 기준 최대 인원 계산
        // 팀전은 4명 고정
        // 개인전은 m.MaxPlayers 기준
        int max = (m.Mode == MatchMode.Team)
            ? TEAM_FIXED
            : Mathf.Clamp(m.MaxPlayers, SOLO_MIN, SOLO_MAX);

        // 방이 가득 찼으면 초대 불가
        if (currentPlayers >= max)
        {
            ToastMessageManager.instance?.ShowToast(
                "방이 가득 찼습니다.",
                "The room is full."
            );
            return;
        }

        // 초대 팝업이 연결되지 않았으면 중단
        if (roomInvitePopup == null)
        {
            Debug.LogWarning("[RoomManager] roomInvitePopup is not assigned.");
            return;
        }

        // 현재 Photon/Fusion 방 이름 가져오기
        string roomName = m.Runner.SessionInfo.IsValid
            ? m.Runner.SessionInfo.Name
            : "";

        // 친구 초대 팝업 열기
        // 이 팝업 안에서 온라인 친구만 보여주고 초대 버튼을 누르게 됨
        roomInvitePopup.Open(
            roomName,
            m.Mode,
            m.Map,
            m.MaxPlayers
        );
    }
    #endregion

    #region Room Settings (Mode/Max/Map)
    private void RefreshRoomSettingsUI()
    {
        var members = RoomMembersState.Instance;
        if (members == null || members.Runner == null) return;

        // (RoomMembersState에 Mode/Map/MaxPlayers 네트워크 동기화가 들어간 버전 기준)
        _mode = members.Mode;
        _map = members.Map;
        _maxPlayers = members.MaxPlayers;

        /*if (teamUiObject != null)
            teamUiObject.SetActive(_mode == MatchMode.Team);*/

        chatUiManager.OnUiTeamChatting(_mode == MatchMode.Team);

        // 맵 미리보기
        if (mapImage != null)
            SpriteManager.instance.imageMapSpriteChange(mapImage, _map);

        // 모드 토글/인원 입력 UI 반영 (재귀 콜백 방지)
        _suppressSettingCallback = true;

        if (toggleSolo != null) toggleSolo.isOn = (_mode == MatchMode.Solo);
        if (toggleTeam != null) toggleTeam.isOn = (_mode == MatchMode.Team);

        if (inputMaxPlayers != null)
        {
            if (_mode == MatchMode.Team)
            {
                inputMaxPlayers.SetTextWithoutNotify(TEAM_FIXED.ToString());
            }
            else
            {
                // 개인전 표시: 2~4로 보정해서 보여줌
                int soloMax = Mathf.Clamp(_maxPlayers, SOLO_MIN, SOLO_MAX);
                inputMaxPlayers.SetTextWithoutNotify(soloMax.ToString());
            }
        }

        _suppressSettingCallback = false;

        int cur = members.Runner.SessionInfo.IsValid ? members.Runner.SessionInfo.PlayerCount : 0;
        string modeStr = (_mode == MatchMode.Team) ? "TEAM" : "SOLO";
        string mapStr = (_map == 0) ? "KOREA" : "USA";

        var lang = (LaguageManager.Instance != null)
            ? LaguageManager.Instance.currentLang
            : Lauaguage.Kor;

        switch (mapStr)
        {
            case "KOREA":
                if (lang == Lauaguage.Kor) txtMapName.text = "한국 맵";
                else txtMapName.text = "KOREA Map";
                break;
            case "USA":
                if (lang == Lauaguage.Kor) txtMapName.text = "미국 맵";
                else txtMapName.text = "USA Map";
                break;
        }

        bool amLeader = AmILeader();

        if (btnGameStart != null)
            btnGameStart.gameObject.SetActive(amLeader);

        if (btnReady != null)
            btnReady.gameObject.SetActive(!amLeader);

        // 리더가 되면 로컬 ready 상태는 false로 리셋
        if (amLeader)
            _localReady = false;
    }

    private void BindMapButtons()
    {
        if (btnMapSelect == null) return;

        for (int i = 0; i < btnMapSelect.Length; i++)
        {
            int idx = i;
            if (btnMapSelect[idx] == null) continue;

            btnMapSelect[idx].onClick.RemoveAllListeners();
            btnMapSelect[idx].onClick.AddListener(() => OnClickMapSelect(idx));
        }
    }

    private void UpdateMapButtonsInteractable(int selectedMap, bool amLeader)
    {
        if (btnMapSelect == null) return;

        // 리더가 아니면 전부 비활성
        if (!amLeader)
        {
            foreach (var b in btnMapSelect)
                if (b != null) b.interactable = false;
            return;
        }

        // 리더면 "선택된 맵" 버튼은 비활성, 나머지는 활성
        for (int i = 0; i < btnMapSelect.Length; i++)
        {
            var b = btnMapSelect[i];
            if (b == null) continue;

            b.interactable = (i != selectedMap);
        }
    }

    private void OnClickMapSelect(int map)
    {
        if (!AmILeader()) return;

        var m = Members;
        if (m == null) return;

        map = Mathf.Clamp(map, 0, 1);

        // 맵만 바꿀 거면, 현재 모드/최대인원은 "그대로 유지"해서 서버에 다시 보내면 됨
        int keepModeInt = (int)m.Mode;
        int keepSoloMax = Mathf.Clamp(m.MaxPlayers, SOLO_MIN, SOLO_MAX); // Team이어도 서버가 4로 고정해줌

        //UI 즉시 반영(체감 빠름) - 실제 확정은 네트워크 동기화 값으로 다시 맞춰짐
        _map = map;
        if (mapImage != null)
            SpriteManager.instance.imageMapSpriteChange(mapImage, _map);

        m.RPC_RequestChangeRoomSettings(keepModeInt, map, keepSoloMax);
    }

    private void OnSoloToggleChanged(bool isOn)
    {
        if (_suppressSettingCallback) return;
        if (!isOn) return;
        if (!AmILeader()) return;

        RequestChangeMode(MatchMode.Solo);
    }

    private void OnTeamToggleChanged(bool isOn)
    {
        if (_suppressSettingCallback) return;
        if (!isOn) return;
        if (!AmILeader()) return;

        RequestChangeMode(MatchMode.Team);
    }

    private void RequestChangeMode(MatchMode newMode)
    {
        var m = Members;
        if (m == null) return;

        int soloMax = Mathf.Clamp(ParseMaxPlayersOrDefault(), SOLO_MIN, SOLO_MAX);
        int sendMax = (newMode == MatchMode.Team) ? TEAM_FIXED : soloMax;

        // 팀전 선택 시 입력 UI는 4로 강제 표기(UX)
        _suppressSettingCallback = true;
        if (inputMaxPlayers != null)
        {
            if (newMode == MatchMode.Team)
                inputMaxPlayers.SetTextWithoutNotify(TEAM_FIXED.ToString());
            else
                inputMaxPlayers.SetTextWithoutNotify(soloMax.ToString());
        }
        _suppressSettingCallback = false;

        // 현재 인원보다 작게 바꾸려는 시도 방지
        int curPlayers = GetCurrentPlayersCount();
        if (curPlayers > sendMax)
        {
            RefreshRoomSettingsUI();
            return;
        }

        m.RPC_RequestChangeRoomSettings((int)newMode, _map, soloMax);
    }

    private void OnMaxPlayersValueChanged(string _)
    {
        if (_suppressSettingCallback) return;

        // 팀전이면 입력 즉시 4로 고정(리더여도 변경 불가)
        if (_mode == MatchMode.Team)
        {
            if (inputMaxPlayers != null && inputMaxPlayers.text != TEAM_FIXED.ToString())
                inputMaxPlayers.SetTextWithoutNotify(TEAM_FIXED.ToString());
            return;
        }

        // 개인전: 숫자만 + 상한(4) 적용 (입력중)
        if (inputMaxPlayers == null) return;

        string t = inputMaxPlayers.text;
        string digitsOnly = "";
        foreach (char c in t)
            if (char.IsDigit(c)) digitsOnly += c;

        if (digitsOnly != t)
            inputMaxPlayers.SetTextWithoutNotify(digitsOnly);

        if (string.IsNullOrEmpty(digitsOnly)) return;
        // 상한 4
        if (int.TryParse(digitsOnly, out int v) && v > SOLO_MAX)
            inputMaxPlayers.SetTextWithoutNotify(SOLO_MAX.ToString());
    }

    private void OnMaxPlayersEndEdit(string _)
    {
        if (_suppressSettingCallback) return;
        if (!AmILeader()) return;
        var members = RoomMembersState.Instance;
        if (members == null) return;

        if (_mode == MatchMode.Team)
        {
            // 팀전은 4 고정
            if (inputMaxPlayers != null)
                inputMaxPlayers.SetTextWithoutNotify(TEAM_FIXED.ToString());
            return;
        }

        // 개인전: 비면 최소값으로 확정
        int soloMax = ParseMaxPlayersOrDefault();
        soloMax = Mathf.Clamp(soloMax, SOLO_MIN, SOLO_MAX);

        if (inputMaxPlayers != null)
            inputMaxPlayers.SetTextWithoutNotify(soloMax.ToString());

        // 현재 인원보다 작게 변경 방지(UX)
        int curPlayers = members.Runner != null && members.Runner.SessionInfo.IsValid
            ? members.Runner.SessionInfo.PlayerCount
            : 0;

        if (curPlayers > soloMax)
        {
            RefreshRoomSettingsUI();
            return;
        }

        // 서버에 반영 요청
        members.RPC_RequestChangeRoomSettings((int)MatchMode.Solo, _map, soloMax);
    }

    private int ParseMaxPlayersOrDefault()
    {
        if (inputMaxPlayers == null) return SOLO_MIN;
        if (int.TryParse(inputMaxPlayers.text, out int v)) return v;
        return SOLO_MIN;
    }

    private void UpdateReadyButtonText()
    {
        if (btnReady == null) return;

        var txt = btnReady.GetComponentInChildren<Text>();
        if (txt == null) return;

        var lang = (LaguageManager.Instance != null)
            ? LaguageManager.Instance.currentLang
            : Lauaguage.Kor;

        if (_localReady)
            txt.text = (lang == Lauaguage.Kor) ? "준비 취소" : "CANCEL";
        else
            txt.text = (lang == Lauaguage.Kor) ? "준비" : "READY";
    }

    private void SyncLocalReadyFromNetwork()
    {
        var m = Members;
        if (m == null || m.Runner == null) return;

        // 리더면 ready 의미 없음
        if (AmILeader())
        {
            _localReady = false;
            _readyPending = false;
            return;
        }

        var me = m.Runner.LocalPlayer;

        bool found = false;
        bool netReady = false;

        int n = Mathf.Min(maxSlots, RoomMembersState.MaxSlots);
        for (int i = 0; i < n; i++)
        {
            var s = m.Slots.Get(i);
            if (s.occupied == 1 && s.player == me)
            {
                found = true;
                netReady = s.ready;
                break;
            }
        }

        // 내 슬롯 자체가 없으면(강퇴/나가기 등) pending 해제
        if (!found)
        {
            _localReady = false;
            _readyPending = false;
            return;
        }

        // pending 중이면: 서버 값이 따라오는 순간 확정
        if (_readyPending)
        {
            if (netReady == _readyPendingValue)
            {
                _readyPending = false;
                _localReady = netReady;
                UpdateReadyButtonText();
                return;
            }

            // 타임아웃이면 서버값을 채택(롤백)
            if (Time.time >= _readyPendingUntil)
            {
                _readyPending = false;
                _localReady = netReady;
                UpdateReadyButtonText();
                return;
            }

            // 아직 서버 반영 전: UI는 내가 누른 값 유지
            return;
        }

        // 일반 동기화: 값이 바뀔 때만 갱신
        if (_localReady != netReady)
        {
            _localReady = netReady;
            UpdateReadyButtonText();
        }
    }

    private bool CanLeaderStartGame()
    {
        var m = Members;
        if (m == null || m.Runner == null) return false;

        // 방장만 시작 가능
        if (!AmILeader()) return false;

        var leader = m.Leader;
        if (leader == PlayerRef.None) return false;

        int n = Mathf.Min(maxSlots, RoomMembersState.MaxSlots);

        int totalPlayerCount = 0;
        int nonLeaderCount = 0;

        int redCount = 0;
        int blueCount = 0;

        for (int i = 0; i < n; i++)
        {
            var s = m.Slots.Get(i);

            if (s.occupied == 0) continue;

            totalPlayerCount++;

            // 팀 모드일 때만 슬롯 인덱스 기준으로 팀 인원 체크
            // 현재 구조에서는 s.team이 없음
            // 0, 2번 슬롯 = Red / 1, 3번 슬롯 = Blue
            if (m.ModeInt == (int)MatchMode.Team)
            {
                TeamSide team = m.GetTeamBySlotIndex(i);

                if (team == TeamSide.Red)
                    redCount++;
                else if (team == TeamSide.Blue)
                    blueCount++;
            }

            // 방장은 Ready 검사 제외
            if (s.player == leader)
                continue;

            nonLeaderCount++;

            // 방장 제외 인원 중 한 명이라도 준비 안 했으면 시작 불가
            if (!s.ready)
                return false;
        }

        // 방에 1명만 있으면 시작 불가
        if (totalPlayerCount <= 1)
            return false;

        // 방장 제외 인원이 없으면 시작 불가
        if (nonLeaderCount <= 0)
            return false;

        // 팀 모드일 경우 Red / Blue 양쪽 팀에 최소 1명씩 있어야 하고,
        // 팀 인원 수도 반드시 같아야 시작 가능
        if (m.ModeInt == (int)MatchMode.Team)
        {
            if (redCount <= 0 || blueCount <= 0)
            {
                Debug.LogWarning(
                    $"[RoomManager] Cannot start. Team mode requires both teams. Red={redCount}, Blue={blueCount}"
                );
                return false;
            }

            if (redCount != blueCount)
            {
                Debug.LogWarning(
                    $"[RoomManager] Cannot start. Team count mismatch. Red={redCount}, Blue={blueCount}"
                );
                return false;
            }
        }

        return true;
    }
    #endregion

    #region Team
    private TeamSide GetTeamBySlotIndex(int slotIndex)
    {
        //  0,2 = Red / 1,3 = Blue
        return (slotIndex % 2 == 0) ? TeamSide.Red : TeamSide.Blue;
    }
    #endregion
}
