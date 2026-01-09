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

    [SerializeField] int maxSlots = 4;

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

    private void Start()
    {
        if (btnReady != null) btnReady.onClick.AddListener(OnClickReady);
        if (btnGameStart != null) btnGameStart.onClick.AddListener(OnClickStartGame);
        if (btnLeave != null) btnLeave.onClick.AddListener(OnClickLeave);

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

        if (!AmILeader())
            UpdateReadyButtonText();
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
        if (m == null) return;

        _localReady = !_localReady;
        m.RPC_SetReady(m.Runner.LocalPlayer, _localReady);

        UpdateReadyButtonText();
    }

    private void OnClickStartGame()
    {
        var m = Members;
        if (m == null || m.Runner == null) return;

        // 리더만 시작 가능
        if (!AmILeader()) return;

        // 버튼이 true일 때만 시작
        if (!CanLeaderStartGame()) return;

        // 전원 Ready 검사(occupied만)
        int n = Mathf.Min(maxSlots, RoomMembersState.MaxSlots);
        for (int i = 0; i < n; i++)
        {
            var s = m.Slots.Get(i);
            if (s.occupied == 0) continue;
            if (!s.ready) return;
        }

        // TODO: 실제 게임 시작 로직
        // NetWorkLauncher.instance.StartGameFromRoom();
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
    /// - maxSlots 기준으로 "빈 슬롯도 표시"
    /// </summary>
    private void RefreshPlayersUI()
    {
        if (playerScroll == null) return;

        var m = Members;
        if (m == null || m.Runner == null) return;

        var me = m.Runner.LocalPlayer;
        var leader = m.Leader;

        // 1) 데이터 생성 
        List<RoomMemberInfo> list = new List<RoomMemberInfo>(maxSlots);
        int n = Mathf.Min(maxSlots, RoomMembersState.MaxSlots);

        for (int i = 0; i < n; i++)
        {
            var s = m.Slots.Get(i);
            if (s.occupied == 0) continue; // 빈 슬롯은 표시 안 함

            list.Add(new RoomMemberInfo
            {
                slotIndex = i, // 필요하면 유지(슬롯 번호를 그대로 보여주고 싶을 때)
                player = s.player,
                nickname = s.nickname.ToString(),
                name = s.name.ToString(),
                level = s.level,
                isLeader = (leader != PlayerRef.None && s.player == leader),
                isReady = s.ready,
                isMe = (s.player != PlayerRef.None && s.player == me)
            });
        }

        // 2) UI 정렬: 리더가 항상 0번째, 그 다음은 slotIndex(=들어온/배정 순서)
        list.Sort((a, b) =>
        {
            // 리더 먼저(true가 앞으로 오게)
            int leaderCmp = b.isLeader.CompareTo(a.isLeader);
            if (leaderCmp != 0) return leaderCmp;

            // 그 다음은 슬롯 인덱스 오름차순
            return a.slotIndex.CompareTo(b.slotIndex);
        });


        // 3) InfiniteScroll 갱신
        RoomInfiniteScrollUtil.ClearAll(playerScroll);

        for (int i = 0; i < list.Count; i++)
        {
            var mm = list[i];
            var s = m.Slots.Get(mm.slotIndex);

            var data = new RoomPlayerData
            {
                slotIndex = mm.slotIndex,
                isEmpty = false,
                player = mm.player,
                nickname = mm.nickname,
                name = mm.name,
                level = mm.level,
                isLeader = mm.isLeader,
                isReady = mm.isReady,
                isMe = mm.isMe,
                photoUrl = s.photoUrl.ToString()
            };

            // i번째에 넣기(정렬 순서 유지)
            RoomInfiniteScrollUtil.Insert(playerScroll, data, i);
        }

        RoomInfiniteScrollUtil.UpdateAll(playerScroll);
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

        var me = m.Runner.LocalPlayer;

        int n = Mathf.Min(maxSlots, RoomMembersState.MaxSlots);
        for (int i = 0; i < n; i++)
        {
            var s = m.Slots.Get(i);
            if (s.occupied == 1 && s.player == me)
            {
                _localReady = s.ready;
                return;
            }
        }

        _localReady = false;
    }

    private bool CanLeaderStartGame()
    {
        var m = Members;
        if (m == null || m.Runner == null) return false;

        // 리더만 시작 가능
        if (!AmILeader()) return false;

        var leader = m.Leader;
        if (leader == PlayerRef.None) return false;

        int nonLeaderCount = 0;

        int n = Mathf.Min(maxSlots, RoomMembersState.MaxSlots);
        for (int i = 0; i < n; i++)
        {
            var s = m.Slots.Get(i);
            if (s.occupied == 0) continue;

            // 리더는 Ready 검사 제외
            if (s.player == leader) continue;

            nonLeaderCount++;

            // 리더 제외 인원 중 한명이라도 ready=false면 시작 불가
            if (!s.ready) return false;
        }

        // (선택) 리더 혼자면 시작 불가
        if (nonLeaderCount == 0) return false;

        return true;
    }
    #endregion
}
