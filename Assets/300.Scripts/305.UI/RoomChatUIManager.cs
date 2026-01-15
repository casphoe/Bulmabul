using Fusion;
using Gpm.Ui;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 
///
///  기존 방식(문제):
/// - RoomChatState가 NetworkArray(Messages) + Revision 으로 "네트워크 상태"에 채팅 히스토리를 저장.
/// - UI는 매 프레임 Revision 체크 -> BuildOrderedSnapshot -> 전체 리빌드(무거움)
/// - NetworkArray + NetworkString 용량 때문에 32KB 제한 터짐의 주범.
///
///  변경 방식(해결):
/// - RoomChatState는 채팅을 "네트워크 상태로 저장하지 않고",
///   서버가 RPC로 전체에게 브로드캐스트 → 각 클라가 로컬 히스토리에 저장.
/// - UI는:
///   1) 시작 시 로컬 히스토리 전체를 1회 그린다.
///   2) 이후 새 메시지는 RoomChatState.OnChatReceived 이벤트로 "한 줄씩" 추가.
/// - 더 이상 Chat.Revision / BuildOrderedSnapshot 사용하지 않음.
/// </summary>
public class RoomChatUIManager : MonoBehaviour
{
    #region 변수
    [Header("UI")]
    [SerializeField] private InfiniteScroll chatScroll;
    [SerializeField] private TMP_InputField inputChat;
    [SerializeField] private Button btnSend;
    [SerializeField] Text txtChattSetting;

    [Header("팀 모드일 때 전체 채팅, 파티 채팅인지 알 수 있는 버튼")]
    [SerializeField] private Button btnAllChat;
    [SerializeField] private Button btnTeamChat;

    [Header("Optional ScrollRect (자동 맨 아래)")]
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private bool autoScrollToBottom = true;

    [Header("Profanity (Local UX)")]
    [Tooltip("로컬에서 금지어를 미리 검사할지(UX용). 최종은 서버에서 막아야 안전.")]
    [SerializeField] private bool useLocalProfanityCheck = true;

    [Tooltip("true면 금지어 포함 시 전송 차단 / false면 ***로 마스킹해서 전송")]
    [SerializeField] private bool blockOnProfanity = true;

    [Tooltip("금지어 차단 시 유저에게 보여줄 문구(없으면 Debug.LogWarning만)")]
    [SerializeField] private TMP_Text txtWarn;

    [SerializeField] private float warnShowSec = 1.2f;

    [Header("현재 채팅 채널 - 전체 채팅인지 팀 채팅인지 알수 있게 해주는 변수")]
    [SerializeField] private ChatChannel _currentChannel = ChatChannel.Global;

    private float _warnUntil = 0f;

    // UI에 표시할 데이터 리스트
    private readonly List<ChatMessageData> _dataList = new();

    // RoomChatState.LocalChatMessage 스냅샷(처음 전체 그릴 때 사용)
    private readonly List<RoomChatState.LocalChatMessage> _historySnapshot = new();

    // 인스턴스 접근 편의
    private RoomChatState Chat => RoomChatState.Instance;
    private RoomMembersState Members => RoomMembersState.Instance;

    // 이벤트 구독 상태(중복 구독 방지)
    private bool _subscribed;

    // 로컬 히스토리 최대치(=RoomChatState localHistoryLimit 과 같은 값 권장)
    // RoomChatState 쪽이 기본 100으로 유지하면 여기서도 100으로 맞추는 게 깔끔.
    [Header("UI History Limit (RoomChatState localHistoryLimit와 맞추기 권장)")]
    [SerializeField] private int uiHistoryLimit = 100;

    // Enter / EndEdit / Button 등 여러 경로에서 TrySend가 호출돼도 1번만 전송되게
    private int _lastSendFrame = -1;
    private string _lastSentText = "";
    private float _lastSendTime = -999f;

    // ===== Auto-scroll 정책 =====
    // 유저가 바닥 근처에 있으면 새 메시지 올 때 계속 바닥 고정
    [SerializeField] private bool onlyAutoScrollWhenNearBottom = true;

    // 바닥 판정 오차(0이면 완전 바닥일 때만)
    [SerializeField] private float bottomThreshold = 0.02f;

    // 같은 메시지 “연속 중복” 방지 시간(원하면 0으로 꺼도 됨)
    [SerializeField] private float duplicateBlockWindow = 0.15f;

    #endregion

    private void Start()
    {
        // 버튼/입력 이벤트 연결
        if (btnSend != null)
        {
            btnSend.onClick.RemoveListener(OnClickSend);
            btnSend.onClick.AddListener(OnClickSend);
        }

        if (inputChat != null)
        {
            inputChat.onSubmit.RemoveListener(OnEndEdit);
            inputChat.onSubmit.AddListener(OnEndEdit);
        }

        if (btnAllChat != null)
        {
            btnAllChat.onClick.RemoveAllListeners();
            btnAllChat.onClick.AddListener(() => SwitchChannel(ChatChannel.Global));
        }
        if (btnTeamChat != null)
        {
            btnTeamChat.onClick.RemoveAllListeners();
            btnTeamChat.onClick.AddListener(() => SwitchChannel(ChatChannel.Party));
        }

        bool team = (Members != null && Members.ModeInt == (int)MatchMode.Team);
        if (team)
        {
            _currentChannel = ChatChannel.Party;
        }
        else
        {
            _currentChannel = ChatChannel.Global;
        }
        OnUiTeamChatting(team);
        TxtChattingSetting(_currentChannel);
    }

    public void OnPanelShown()
    {
        // 패널 켜질 때마다 최신 히스토리로 UI를 다시 그려준다
        StartCoroutine(CoInitWhenChatReady());
    }

    public void OnPanelHidden()
    {
        // 선택: 이벤트 중복 구독 방지용으로 해제
        UnsubscribeChatEvent();
    }

    private IEnumerator CoInitWhenChatReady()
    {
        // RoomChatState.Instance 생성(Spawned)될 때까지 대기
        // (씬 로드/네트워크 타이밍에 따라 Start 시점에 null일 수 있음)
        float timeout = 5f;
        float t = 0f;

        while (Chat == null && t < timeout)
        {
            t += 0.1f;
            yield return new WaitForSeconds(0.1f);
        }

        // 그래도 없으면(씬에 오브젝트가 없거나 스폰 실패) 그냥 종료
        if (Chat == null)
        {
            Debug.LogWarning("[RoomChatUIManager] RoomChatState.Instance is null. Check scene NetworkObject setup.");
            yield break;
        }

        // 1) 이벤트 구독
        SubscribeChatEventOnce();

        // 2) 시작 시 로컬 히스토리 전체 1회 그리기
        RebuildFromLocalHistory();

        // 3) 초기 상태에서 맨 아래로
        if (autoScrollToBottom)
            ScrollToBottomNextFrame();
    }

    private void OnDestroy()
    {
        UnsubscribeChatEvent();
    }

    private void Update()
    {
        // 경고 텍스트 자동 숨김
        if (txtWarn != null)
        {
            if (Time.time >= _warnUntil && txtWarn.gameObject.activeSelf)
                txtWarn.gameObject.SetActive(false);
        }

        // 입력칸 포커스 + Enter -> 전송
        if (inputChat != null && inputChat.interactable && inputChat.isFocused)
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                TrySend("update");
        }

        // 방 참가자만 입력 가능하도록(가벼운 체크)
        bool canChat = (Chat != null) && AmIValidMember(out _, out _);
        if (btnSend != null) btnSend.interactable = canChat;
        if (inputChat != null) inputChat.interactable = canChat;
    }

    private void SubscribeChatEventOnce()
    {
        if (_subscribed) return;
        if (Chat == null) return;

        // 새 메시지 올 때마다 한 줄 추가
        Chat.OnChatReceived += HandleChatReceived;
        _subscribed = true;
    }

    private void UnsubscribeChatEvent()
    {
        if (!_subscribed) return;
        if (Chat != null)
            Chat.OnChatReceived -= HandleChatReceived;

        _subscribed = false;
    }

    private void OnClickSend() => TrySend("button");

    private void OnEndEdit(string _)
    {
        // 모바일 키보드의 Done/Send에도 대응
        TrySend("endEdit");
    }

    /// <summary>
    /// 내가 현재 RoomMembersState 슬롯에 들어있는 “유효한 멤버”인지 확인
    /// </summary>
    private bool AmIValidMember(out NetworkRunner runner, out PlayerRef me)
    {
        runner = null;
        me = PlayerRef.None;

        if (Members == null || Members.Runner == null) return false;
        runner = Members.Runner;
        me = runner.LocalPlayer;
        if (me == PlayerRef.None) return false;

        for (int i = 0; i < RoomMembersState.MaxSlots; i++)
        {
            var s = Members.Slots.Get(i);
            if (s.occupied == 1 && s.player == me) return true;
        }
        return false;
    }

    /// <summary>
    /// 입력값 검증 + (선택) 금지어 처리 후 채팅 전송
    /// - 이제 Chat.RPC_SendChat(...)가 아니라
    ///   Chat.SendChatFromUI(...) (또는 Chat이 제공하는 UI용 Send 함수)로 보낸다.
    /// </summary>
    private void TrySend(string calledFrom)
    {
        if (Chat == null) return;
        if (inputChat == null) return;

        // ===== 1) 같은 프레임 중복 호출 차단 =====
        if (_lastSendFrame == Time.frameCount)
            return;

        string raw = (inputChat.text ?? "");
        string msg = raw.Trim();

        // 빈 문자열이면 종료 (프레임 락도 걸 필요 없음)
        if (string.IsNullOrWhiteSpace(msg)) return;

        // 2) 아주 짧은 시간에 같은 메시지 중복 전송 방지(선택)
        // onEndEdit + Update가 “다른 프레임”으로 들어오는 케이스도 있어서 추가 안전장치
        if (duplicateBlockWindow > 0f)
        {
            if (msg == _lastSentText && (Time.unscaledTime - _lastSendTime) < duplicateBlockWindow)
                return;
        }

        // 이제부터 “전송 시도”로 간주 → 락 걸기
        _lastSendFrame = Time.frameCount;
        _lastSentText = msg;
        _lastSendTime = Time.unscaledTime;

        // 3) 멤버 체크 
        if (!AmIValidMember(out _, out _))
            return;

        // party 채널 전송 조건 검사

        if (_currentChannel == ChatChannel.Party)
        {
            // (1) 팀전인지 확인
            bool isTeamMode = (Members != null && Members.ModeInt == (int)MatchMode.Team);
            if (!isTeamMode)
            {
                ShowWarn("파티 채팅은 팀전에서만 사용할 수 있습니다.");
                _currentChannel = ChatChannel.Global; // 안전: 채널을 Global로 되돌림
                return;
            }

            // (2) 내 팀 확인
            TeamSide myTeam = GetMyTeamSide();
            if (myTeam == TeamSide.None)
            {
                // 팀 정보가 아직 안 들어온 상태(입장 직후)일 가능성
                ShowWarn("팀 정보 동기화 중입니다. 잠시 후 다시 시도해주세요.");
                return;
            }

            // 여기서 "같은 팀만 볼 수 있게"는 UI 필터로 이미 처리했고,
            // 실제 네트워크 수신 필터/서버 필터는 RoomChatState에서 처리해야 완벽함.
            // UI 레벨에서는 전송 가능한 상태만 확인하면 됨.
        }

        //  5) 로컬 금지어 필터
        if (useLocalProfanityCheck)
        {
            ProfanityFilter.BlockMode = blockOnProfanity;

            bool changedOrBlocked;
            string filtered = ProfanityFilter.Filter(msg, out changedOrBlocked);

            if (filtered == null)
            {
                ShowProfanityToast();

                inputChat.ActivateInputField();
                return;
            }

            msg = filtered;
        }

        //  6) 전송
        Chat.SendChatFromUI(msg, _currentChannel);

        // 입력창 정리
        inputChat.SetTextWithoutNotify("");
        inputChat.ActivateInputField();
    }

    private void ShowWarn(string text)
    {
        Debug.LogWarning($"[Chat] blocked: {text}");

        if (txtWarn == null) return;

        txtWarn.text = text;
        txtWarn.gameObject.SetActive(true);
        _warnUntil = Time.time + warnShowSec;
    }

    /// <summary>
    /// (초기 1회) RoomChatState가 들고 있는 “로컬 히스토리”를 통째로 가져와서
    /// InfiniteScroll을 한번에 다시 빌드한다.
    /// </summary>
    private void RebuildFromLocalHistory()
    {
        if (Chat == null) return;

        bool wasNearBottom = (!onlyAutoScrollWhenNearBottom) || IsNearBottom();

        // Chat이 가진 로컬 히스토리 스냅샷 복사
        _historySnapshot.Clear();
        Chat.CopyHistoryTo(_historySnapshot);

        // UI용 데이터 리스트로 변환
        _dataList.Clear();

        AmIValidMember(out var runner, out var me);

        TeamSide myTeam = GetMyTeamSide();
        bool isTeamMode = (Members != null && Members.ModeInt == (int)MatchMode.Team);

        for (int i = 0; i < _historySnapshot.Count; i++)
        {
            var m = _historySnapshot[i];
            //전체 탭에 맞게 필터
            if (_currentChannel == ChatChannel.Global)
            {
                if (m.channel != ChatChannel.Global) continue;
            }
            else // Party 탭
            {
                if (!isTeamMode) continue;
                if (m.channel != ChatChannel.Party) continue;
                if (m.team != myTeam) continue;   // 같은 팀만
            }
            var data = ConvertToData(m, runner, me, viewIndex: _dataList.Count, animate: false);
            data.channel = m.channel; // ChatMessageData에도 channel 있으니 세팅
            _dataList.Add(data);
        }

        RebuildScroll();

        if (autoScrollToBottom && wasNearBottom)
            ScrollToBottomNextFrame();
    }

    /// <summary>
    /// 새 채팅 1개 수신 시 호출되는 핸들러
    /// - "한 줄 추가"로 처리해서 무거운 전체 리빌드를 피한다.
    /// - 단, 히스토리 제한을 넘으면(맨 앞 제거 필요) 한번 전체 리빌드로 정리하는 게 안전.
    /// </summary>
    private void HandleChatReceived(RoomChatState.LocalChatMessage m)
    {

        Debug.Log($"[ChatUI] recv channel={m.channel} team={m.team} / currentTab={_currentChannel}");

        // 새 메시지 추가 전에 “현재 바닥 근처인지” 저장
        bool wasNearBottom = (!onlyAutoScrollWhenNearBottom) || IsNearBottom();

        // 내가 아직 채팅 가능한 멤버인지 여부(내 말풍선 강조에만 사용)
        AmIValidMember(out var runner, out var me);

        bool isTeamMode = (Members != null && Members.ModeInt == (int)MatchMode.Team);
        TeamSide myTeam = GetMyTeamSide();

        // 현재 보고 있는 채널이 아니면 UI에 추가하지 않음
        if (_currentChannel == ChatChannel.Global)
        {
            if (m.channel != ChatChannel.Global) return;
        }
        else
        {
            if (!isTeamMode) return;
            if (m.channel != ChatChannel.Party) return;
            if (m.team != myTeam) return;
        }

        // UI 히스토리 제한 처리:
        // RoomChatState 쪽에서 앞을 잘라내면, UI도 맨 앞 제거가 필요함.
        // GPM InfiniteScroll은 "맨 앞 제거"가 번거로울 수 있어서,
        // limit 초과 시에는 한번 전체 리빌드(안전/간단)로 처리.
        int limit = Mathf.Clamp(uiHistoryLimit, 10, 500);

        if (_dataList.Count >= limit)
        {
            // 안전하게 전체 리빌드 (RoomChatState의 최신 히스토리 기준)
            RebuildFromLocalHistory();
            if (autoScrollToBottom && wasNearBottom)
                ScrollToBottomNextFrame();
            return;
        }

        // 한 줄 추가
        int viewIndex = _dataList.Count;
        bool animate = true; // 최신 1개만 타이핑/애니메이션 하고 싶으면 true

        var data = ConvertToData(m, runner, me, viewIndex, animate);
        _dataList.Add(data);

        // InfiniteScroll에 Insert
        if (chatScroll != null)
        {
            GpmInfiniteScrollCompat.InsertData(chatScroll, data, viewIndex);
            GpmInfiniteScrollCompat.UpdateAllData(chatScroll);
        }

        // “유저가 원래 바닥 근처였을 때만” 자동으로 바닥 유지
        if (autoScrollToBottom && wasNearBottom)
            ScrollToBottomNextFrame();
    }

    /// <summary>
    /// RoomChatState.LocalChatMessage -> ChatMessageData 변환
    /// (너 프로젝트의 ChatMessageItem이 읽는 데이터 구조에 맞춤)
    /// </summary>
    private ChatMessageData ConvertToData(RoomChatState.LocalChatMessage m, NetworkRunner runner, PlayerRef me, int viewIndex, bool animate)
    {
        string timeText = "";
        bool isTeamMode = (Members != null && Members.ModeInt == (int)MatchMode.Team);

        try
        {
            var dt = DateTimeOffset.FromUnixTimeSeconds(m.unixTime).ToLocalTime();
            timeText = dt.ToString("HH:mm");
        }
        catch { }

        return new ChatMessageData
        {
            viewIndex = viewIndex,
            sender = m.sender,
            nickname = m.nickname ?? "",
            message = m.text ?? "",
            timeText = timeText,
            isMine = (runner != null && me != PlayerRef.None && m.sender == me),
            // seq를 쓰면 유니크 보장 쉬움
            messageId = $"{m.sender}_{m.seq}",
            animate = animate,
            channel = m.channel,
            isTeamMode = isTeamMode,
            team = m.team
        };
    }

    private void RebuildScroll()
    {
        if (chatScroll == null) return;

        GpmInfiniteScrollCompat.ClearData(chatScroll);

        for (int i = 0; i < _dataList.Count; i++)
            GpmInfiniteScrollCompat.InsertData(chatScroll, _dataList[i], i);

        GpmInfiniteScrollCompat.UpdateAllData(chatScroll);
    }

    /// <summary>
    /// (중요) GPM InfiniteScroll은 레이아웃 갱신 타이밍이 있어서
    /// 1프레임(또는 EndOfFrame) 기다리고 Scroll을 내려야 안전함.
    /// </summary>
    private void ScrollToBottomNextFrame()
    {
        if (scrollRect == null) return;
        StopCoroutine(nameof(CoScrollToBottom)); // 중복 방지
        StartCoroutine(CoScrollToBottom());
    }

    private IEnumerator CoScrollToBottom()
    {
        // GPM이 아이템 배치/컨텐츠 사이즈를 바꾸는 타이밍 보정
        yield return null;
        Canvas.ForceUpdateCanvases();
        yield return new WaitForEndOfFrame();

        if (scrollRect != null)
            scrollRect.verticalNormalizedPosition = 0f; // 맨 아래 고정
    }

    #region 스크롤 위치가 제일 하단에 위치하고 있는지 아닌지 판단
    private bool IsNearBottom()
    {
        if (scrollRect == null) return true;

        // Unity ScrollRect: bottom = 0, top = 1
        return scrollRect.verticalNormalizedPosition <= bottomThreshold;
    }
    #endregion

    #region 채널 변경 전체 채팅, 파티 채팅
    private void SwitchChannel(ChatChannel ch)
    {
        //팀 모드
        if (ch == ChatChannel.Party)
        {
            ch = ChatChannel.Party;
        }
        else
        {
            ch = ChatChannel.Global;
        }
        RebuildFromLocalHistory();
        TxtChattingSetting(ch);
    }

    private void TxtChattingSetting(ChatChannel ch)
    {
        var lang = (LaguageManager.Instance != null)
            ? LaguageManager.Instance.currentLang
            : Lauaguage.Kor;

        if (ch == ChatChannel.Party)
        {
            if (lang == Lauaguage.Kor) txtChattSetting.text = "팀 채팅";
            else txtChattSetting.text = "Team Chatt";
        }
        else
        {
            if (lang == Lauaguage.Kor) txtChattSetting.text = "전체 채팅";
            else txtChattSetting.text = "All Chatt";
        }
    }
    #endregion

    #region 내 팀 구하는 함수

    private TeamSide GetMyTeamSide()
    {
        if (Members == null || Members.Runner == null) return TeamSide.None;
        var me = Members.Runner.LocalPlayer;
        if (me == PlayerRef.None) return TeamSide.None;

        return Members.GetTeamByPlayer(me);
    }

    #endregion

    #region 전체 채팅 파티 채팅 버튼의 대한 함수

    public void OnUiTeamChatting(bool isTeamMode)
    {        
        btnAllChat.gameObject.SetActive(isTeamMode);
        btnTeamChat.gameObject.SetActive(isTeamMode);
    }

    #endregion

    #region 금지어 출력시 토스트 메시지 뿌려주는 함수

    private void ShowProfanityToast()
    {
        string text = GetProfanityText();

        if (ToastMessageManager.instance != null)
            ToastMessageManager.instance.ShowToast(text);
        else
            Debug.LogWarning($"[Toast missing] {text}");
    }

    private string GetProfanityText()
    {
        var lang = (LaguageManager.Instance != null)
            ? LaguageManager.Instance.currentLang
            : Lauaguage.Kor;

        // 문구는 원하는대로 바꿔도 됨
        if (lang == Lauaguage.Kor)
            return "금지어가 포함되어 전송할 수 없습니다.";
        else
            return "Your message contains prohibited words and cannot be sent.";
    }

    #endregion
}
