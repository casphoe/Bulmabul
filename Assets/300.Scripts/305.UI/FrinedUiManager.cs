using Firebase.Auth;
using Firebase.Database;
using Gpm.Ui;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;



/// <summary>
/// 친구 UI / 친구 요청 토스트 / 알림 / 프레즌스(온라인) / 친구 채팅 창 관리까지
/// 한 곳에서 담당하는 매니저.
///
/// 주요 책임(Responsibility)
/// 1) 친구 패널 On/Off + 탭 전환(초대 / 내 친구)
/// 2) 초대 후보 검색/표시 + 친구요청 보내기
/// 3) 내 친구 목록 표시 + 온라인 상태(프레즌스) 실시간 반영
/// 4) 친구 요청 수신(friendRequestsIn) 감지 → 수락/거절 토스트 큐 처리
/// 5) 알림(notifications) 감지 → 토스트 표시 후 서버에서 알림 삭제
/// 6) 친구 채팅: 채팅창 생성/관리 + 메시지 도착 시 토스트/자동오픈(채팅 인덱스 없이도 동작)
/// 7) 로그아웃/계정 변경 시 모든 리스너/상태 정리 + 필요 시 채팅 기록 삭제(OnDisconnect/수동)
///
///  주의 포인트
/// - Firebase 이벤트 콜백은 메인 스레드가 아닐 수 있으므로,
///   UI 변경은 _mainThreadQueue 를 통해 Update에서 안전하게 처리한다.
/// - 구독(리스너) 등록/해제 누락 시 중복 토스트, 메모리 누수, 이벤트 중복 호출이 발생할 수 있다.
/// </summary>
public class FrinedUiManager : MonoBehaviour
{
    #region Singleton / Inspector

    public static FrinedUiManager instance;

    [Header("Panel")]
    [SerializeField] private GameObject friendPanel;

    [Header("Friend Profile Popup")]
    [SerializeField] private FriendProfilePopup friendProfilePopup;

    [Header("Search Inputs")]
    /// <summary>
    /// "초대" 탭에서 후보 검색 키워드 입력창
    /// </summary>
    public TMP_InputField inputInviteSearch;

    /// <summary>
    /// "내 친구" 탭에서 친구 검색 키워드 입력창
    /// </summary>
    public TMP_InputField inputCurrentFriend;

    [Header("Tabs")]
    public Button btnInviteTab;      // "초대"
    public Button btnFriendsTab;     // "내 친구"
    public Button btnFriendSearch;   // 검색 버튼
    public Button btnFriendCancel;   // 닫기 버튼

    [Header("InfiniteScroll")]
    public InfiniteScroll friendScroll;   // GPM InfiniteScroll

    [Header("Friend Request Toast")]
    [SerializeField] private FriendRequestToastUI requestToast;

    #endregion

    #region Internal State

    /// <summary>
    /// 내 UID(로그인 된 계정)
    /// </summary>
    private string _myUid;

    /// <summary>
    /// 친구 패널이 현재 열려있는지 여부
    /// </summary>
    private bool isFriendOpen = false;

    /// <summary>
    /// 현재 탭 모드
    /// - Invite : 초대 후보
    /// - Friends : 내 친구
    /// </summary>
    [SerializeField] private enum TabMode { Invite, Friends }

    [Header("현재 탭")]
    [SerializeField] private TabMode _mode = TabMode.Friends;

    /// <summary>
    /// 목록 로드/검색 시 이전 요청을 취소하기 위한 토큰
    /// (빠르게 탭 전환/검색 연타할 때 마지막 요청만 살리기 위함)
    /// </summary>
    private CancellationTokenSource _cts;

    /// <summary>
    /// Firebase 콜백 스레드에서 UI를 직접 만지면 위험하므로
    /// Action을 큐에 넣고 Update에서 메인스레드로 실행한다.
    /// </summary>
    private readonly ConcurrentQueue<Action> _mainThreadQueue = new();

    #endregion

    #region Friend Request (Incoming) Toast Queue

    /// <summary>
    /// friendRequestsIn/{myUid} 참조 (친구 요청 "받은" 목록)
    /// </summary>
    private DatabaseReference _reqInRef;

    /// <summary>
    /// 수신된 친구요청 정보를 토스트 큐에 넣기 위한 구조체
    /// </summary>
    private struct IncomingReq
    {
        public string fromUid;
        public string fromNick;
    }

    /// <summary>
    /// "받은 친구요청" 토스트를 순서대로 보여주기 위한 큐
    /// </summary>
    private readonly Queue<IncomingReq> _incomingQueue = new();

    /// <summary>
    /// 같은 요청이 여러 번 들어오는 중복을 막기 위한 set(이미 큐에 넣은 fromUid 기록)
    /// </summary>
    private readonly HashSet<string> _queuedSet = new();

    /// <summary>
    /// 지금 친구요청 토스트가 화면에 떠있는지 여부
    /// </summary>
    private bool _toastShowing = false;

    /// <summary>
    /// 현재 토스트로 보여주는 요청의 fromUid
    /// (같은 uid가 연속으로 들어오면 중복 방지)
    /// </summary>
    private string _showingFromUid = null;

    /// <summary>
    /// 삭제 확인 토스트가 떠있는지 여부
    /// (친구요청 토스트와 동시에 뜨면 UX/상태 꼬임 방지)
    /// </summary>
    private bool _deleteConfirmShowing = false;

    #endregion

    #region Notifications Listener

    /// <summary>
    /// notifications/{myUid} 참조
    /// (친구 수락/거절/취소/삭제 등 서버가 남긴 알림)
    /// </summary>
    private DatabaseReference _notiRef;

    /// <summary>
    /// notifications ChildAdded가 네트워크 상태/재접속으로 중복 호출될 수 있어
    /// 처리한 알림 id를 기록하여 중복 토스트 방지
    /// </summary>
    private readonly HashSet<string> _notiHandled = new();

    #endregion

    #region Presence Watch (Friends 탭에서 온라인 표시)

    /// <summary>
    /// friends 탭에서만 필요한 온라인 상태 구독 목록
    /// uid -> (presence reference, event handler)
    /// </summary>
    private readonly Dictionary<string, (DatabaseReference r, EventHandler<ValueChangedEventArgs> h)> _presenceSubs
        = new();

    /// <summary>
    /// Friends 탭에 표시 중인 데이터 캐시
    /// uid -> FriendListItemData
    /// (presence 이벤트에서 해당 uid의 isOnline만 바로 업데이트하기 위함)
    /// </summary>
    private readonly Dictionary<string, FriendListItemData> _friendByUid
        = new();

    /// <summary>
    /// presence 변경을 감지했고 UI 갱신이 필요함을 표시하는 플래그
    /// </summary>
    [SerializeField] private bool _presenceDirty = false;

    /// <summary>
    /// presence 변경이 잦을 수 있으니 UI 리로드를 너무 자주 하지 않도록 최소 간격(초)
    /// </summary>
    [SerializeField] private float presenceUiRefreshInterval = 0.5f;

    /// <summary>
    /// 다음 UI 갱신 가능 시점(언스케일 타임 기준)
    /// </summary>
    private float _nextPresenceUiRefresh = 0f;

    #endregion


    #region Friend Chat

    [Header("Friend Chat Windows")]
    /// <summary>
    /// 채팅창들이 생성될 부모 Transform
    /// </summary>
    public Transform chatWindowParent;

    /// <summary>
    /// 채팅창 프리팹
    /// </summary>
    public FriendChatWindow chatWindowPrefab;

    /// <summary>
    /// chatKey(=MakeChatId) -> 열린 채팅창 인스턴스
    /// </summary>
    private readonly Dictionary<string, FriendChatWindow> _chatWindows = new();

    /// <summary>
    /// chatKey -> 메모리 채팅 히스토리(스크롤 표시/리로드용)
    /// </summary>
    private readonly Dictionary<string, List<FriendChatMessageData>> _chatHistories = new();

    /// <summary>
    /// 현재 열린 채팅창 개수(친구패널 토글 시 "채팅창 열려있으면 막기" 용도)
    /// </summary>
    private int _openedChatWindowCount = 0;

    /// <summary>
    /// 채팅창이 하나라도 열려있는지
    /// </summary>
    public bool IsAnyChatWindowOpen => _openedChatWindowCount > 0;

    /// <summary>
    /// 채팅 토스트 구독:
    /// chatKey -> (query, handler)
    /// - chats/{uidA}/{uidB}/messages 의 마지막 1개를 구독하여 새 메시지 감지
    /// </summary>
    private readonly Dictionary<string, (Query q, EventHandler<ChildChangedEventArgs> h)> _chatToastSubs
        = new();

    /// <summary>
    /// chatKey -> 마지막 토스트 처리한 msgId
    /// (중복 토스트 방지)
    /// </summary>
    private readonly Dictionary<string, string> _lastToastMsgIdByChat
        = new();

    /// <summary>
    /// 로그인 직후, 친구 목록을 한 번 읽어 "채팅 토스트 구독"을 미리 걸어두었는지
    /// </summary>
    private bool _chatToastPrimed = false;

    /// <summary>
    /// 로그아웃/강제종료 대비: 내 친구 uid 캐시
    /// (채팅 기록 삭제/OnDisconnect 등록 때 사용)
    /// </summary>
    private readonly HashSet<string> _knownFriendUids = new();

    /// <summary>
    /// 마지막으로 확인한 Auth UID (계정 변경/로그아웃 감지)
    /// </summary>
    private string _lastAuthUid = null;

    /// <summary>
    /// OnDisconnect 기반 채팅 기록 삭제 예약을 등록했는지(중복 방지)
    /// </summary>
    private bool _chatCleanupOnDisconnectRegistered = false;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        instance = this;

        if (friendPanel) friendPanel.SetActive(false);
        isFriendOpen = false;

        // 탭 전환 / 검색 / 닫기 버튼
        if (btnInviteTab) btnInviteTab.onClick.AddListener(() => SwitchTab(TabMode.Invite));
        if (btnFriendsTab) btnFriendsTab.onClick.AddListener(() => SwitchTab(TabMode.Friends));
        if (btnFriendSearch) btnFriendSearch.onClick.AddListener(() => _ = SearchByCurrentTabAsync());
        if (btnFriendCancel) btnFriendCancel.onClick.AddListener(() => FriendPanelOff());

        ApplyTabUI();
    }

    private void Update()
    {
        // 메인스레드 큐 처리
        while (_mainThreadQueue.TryDequeue(out var a))
        {
            try { a?.Invoke(); } catch (Exception e) { Debug.LogWarning(e); }
        }

        // Presence UI 갱신(부하 제한)
        if (isFriendOpen && _mode == TabMode.Friends)
        {
            // online 변경이 있었다면 일정 간격마다 ReloadAsync 호출
            if (_presenceDirty && Time.unscaledTime >= _nextPresenceUiRefresh)
            {
                _presenceDirty = false;
                _nextPresenceUiRefresh = Time.unscaledTime + presenceUiRefreshInterval;

                // Friends 리스트 다시 뿌리기
                _ = ReloadAsync();
            }
        }

        // F 키로 친구 패널 토글
        if (Input.GetKeyDown(KeyCode.F))
        {
            // 채팅창이 열려있으면 친구패널 토글하지 않음 (UX/입력 충돌 방지)
            if (IsAnyChatWindowOpen) return;
            isFriendOpen = !isFriendOpen;
            BtnFriendPanelOnOff(isFriendOpen);
        }
    }

    /// <summary>
    /// 오브젝트 활성화 시:
    /// - Firebase Auth StateChanged 구독
    /// - 이미 로그인 상태면 즉시 초기화(TryInitAfterLogin)
    /// </summary>
    private void OnEnable()
    {
        FirebaseAuth.DefaultInstance.StateChanged += OnAuthStateChanged;
        TryInitAfterLogin(); // 이미 로그인 상태면 즉시 초기화
    }

    /// <summary>
    /// 오브젝트 비활성화 시:
    /// - 모든 리스너/구독 해제(중복 호출/메모리 누수 방지)
    /// </summary>
    private void OnDisable()
    {
        FirebaseAuth.DefaultInstance.StateChanged -= OnAuthStateChanged;

        // 모든 리스너/구독 해제
        UnhookIncomingListener();
        UnhookNotificationListener();
        UnhookPresenceListeners();
        UnhookChatToastSubs();
    }

    private async void Start()
    {
        await DeleteAllChatMessagesBeforeLogoutAsync();
    }

    /// <summary>
    /// FirebaseAuth 상태 변화(로그인/로그아웃/계정 전환) 이벤트 핸들러
    /// - 계정이 바뀌거나 로그아웃되면 로컬 상태/리스너를 싹 정리
    /// - 로그인 상태가 되면 리스너 훅 + 채팅 토스트 Prime 등 초기화
    /// </summary>
    private void OnAuthStateChanged(object sender, EventArgs e)
    {
        var user = FirebaseAuth.DefaultInstance.CurrentUser;
        string nowUid = user != null ? user.UserId : null;

        // 로그아웃 or 계정 변경 감지
        if (!string.IsNullOrEmpty(_lastAuthUid) && nowUid != _lastAuthUid)
        {
            CleanupOnLogoutLocal(); // 로컬 흔적 제거
        }

        _lastAuthUid = nowUid;

        // 로그인 상태면 초기화
        TryInitAfterLogin();
    }

    /// <summary>
    /// 로그아웃/계정 전환 시, 로컬에서 유지하던 상태와 UI를 모두 정리한다.
    /// - Firebase 리스너 제거
    /// - 채팅창 파괴
    /// - 토스트 큐/중복 기록 초기화
    /// - Prime 상태/캐시 초기화
    /// </summary>
    private void CleanupOnLogoutLocal()
    {
        // UI
        isFriendOpen = false;
        if (friendPanel) friendPanel.SetActive(false);

        // 리스너/구독 끊기
        UnhookIncomingListener();
        UnhookNotificationListener();
        UnhookPresenceListeners();
        UnhookChatToastSubs();

        // 채팅창 전부 닫고 제거
        foreach (var kv in _chatWindows)
        {
            if (kv.Value != null) Destroy(kv.Value.gameObject);
        }
        _chatWindows.Clear();
        _openedChatWindowCount = 0;

        // 로컬 채팅 히스토리 제거
        _chatHistories.Clear();
        _lastToastMsgIdByChat.Clear();

        // 큐/중복 기록 리셋
        _incomingQueue.Clear();
        _queuedSet.Clear();
        _toastShowing = false;
        _showingFromUid = null;
        _deleteConfirmShowing = false;

        // 알림 중복 기록 초기화
        _notiHandled.Clear();

        // Prime 상태 리셋
        _chatToastPrimed = false;
        _knownFriendUids.Clear();

        // OnDisconnect 등록 플래그도 리셋
        _chatCleanupOnDisconnectRegistered = false;

        _myUid = null;
    }

    /// <summary>
    /// 로그인된 유저가 존재하면:
    /// 1) 내 UID 확보
    /// 2) 친구 요청 수신 리스너 연결(friendRequestsIn)
    /// 3) 알림 리스너 연결(notifications)
    /// 4) 친구 목록 1회 읽어서 채팅 토스트/자동오픈 구독을 "미리" 연결(Prime)
    /// 5) 패널이 열려있다면 즉시 리스트 Reload
    /// </summary>
    private void TryInitAfterLogin()
    {
        var user = FirebaseAuth.DefaultInstance.CurrentUser;
        if (user == null) return;

        _myUid = user.UserId;

        HookIncomingListener();
        HookNotificationListener();

        // 로그인 직후 친구목록을 1회 읽어 messages 구독을 걸어둔다.
        _ = PrimeChatToastSubscriptionsAsync();

        // 패널이 열려있으면 갱신
        if (isFriendOpen)
            _ = ReloadAsync();
    }

    #endregion

    #region Panel On/Off / Tabs

    /// <summary>
    /// 친구 패널 토글
    /// - 켜면 ReloadAsync로 현재 탭 데이터 로드
    /// - 끄면 presence 구독 해제(불필요한 실시간 리스너 제거)
    /// </summary>
    public void BtnFriendPanelOnOff(bool isActive)
    {
        isFriendOpen = isActive;

        if (friendPanel) friendPanel.SetActive(isFriendOpen);

        if (!isFriendOpen)
        {
            // 패널 닫히면 presence/채팅 토스트 구독 끊기
            UnhookPresenceListeners();         
            return;
        }

        _ = ReloadAsync();
    }

    private void FriendPanelOff()
    {
        isFriendOpen = false;
        if (friendPanel) friendPanel.SetActive(false);

        UnhookPresenceListeners();
    }

    /// <summary>
    /// 탭 전환:
    /// - _mode 변경
    /// - 입력창 표시 상태 갱신
    /// - Friends 탭이 아니면 presence 구독 해제
    /// - ReloadAsync로 현재 탭 데이터 로드
    /// </summary>
    private void SwitchTab(TabMode m)
    {
        _mode = m;
        ApplyTabUI();

        if (_mode != TabMode.Friends)
            UnhookPresenceListeners();

        _ = ReloadAsync();
    }

    /// <summary>
    /// 탭에 따라 어떤 검색 입력창을 보여줄지 결정
    /// </summary>
    private void ApplyTabUI()
    {
        if (inputInviteSearch) inputInviteSearch.gameObject.SetActive(_mode == TabMode.Invite);
        if (inputCurrentFriend) inputCurrentFriend.gameObject.SetActive(_mode == TabMode.Friends);
    }

    private async Task SearchByCurrentTabAsync()
    {
        await ReloadAsync();
    }

    /// <summary>
    /// 현재 탭 기준으로 목록 데이터를 다시 불러와 InfiniteScroll을 갱신한다.
    /// - 로그인 확인
    /// - 이전 로드 작업 취소(연타/전환 시 마지막만 반영)
    /// - Invite 탭이면 LoadInviteListAsync
    /// - Friends 탭이면 LoadFriendsListAsync
    /// </summary>
    private async Task ReloadAsync()
    {
        // 로그인 안되어있으면 아무 것도 못 함
        if (string.IsNullOrEmpty(_myUid))
        {
            var user = FirebaseAuth.DefaultInstance.CurrentUser;
            if (user == null) return;
            _myUid = user.UserId;
        }

        // 이전 로드 취소
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        try
        {
            if (_mode == TabMode.Invite) await LoadInviteListAsync();
            else await LoadFriendsListAsync();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[FriendPanel] Reload failed: {e.Message}");
        }
    }

    #endregion

    #region Invite Candidate List

    /// <summary>
    /// 초대 후보 리스트에서 "친구 요청 보내기" 버튼을 눌렀을 때 호출되는 함수.
    /// - AlreadyFriend이면 무시
    /// - None 상태면 FriendService.SendFriendRequestAsync 실행
    /// - 성공 토스트 표시 후 목록 리로드
    /// </summary>
    public async void OnClickInviteButton(FriendListItemData d)
    {
        if (d == null) return;

        if (d.inviteState == FriendListItemData.InviteState.AlreadyFriend) return;

        if (d.inviteState == FriendListItemData.InviteState.None)
        {
            await FriendService.SendFriendRequestAsync(d.uid, d.nick);

            ToastMessageManager.instance?.ShowToast(
                $"{d.nick} 님에게 친구 요청을 보냈습니다.",
                $"Friend request sent to {d.nick}."
            );

            await ReloadAsync();
        }
    }

    /// <summary>
    /// Invite 탭 목록 로드:
    /// 1) 검색 키워드가 있으면 prefix 기반 검색, 없으면 기본 후보 N명
    /// 2) 내 friends / friendRequestsOut / friendRequestsIn 를 읽어서 각 후보의 상태를 결정
    ///    - AlreadyFriend : 후보에서 제외(정책)
    ///    - Outgoing : 내가 요청 보낸 상태
    ///    - Incoming : 상대가 나에게 요청을 보낸 상태
    /// 3) 각 후보의 프로필(닉네임/사진 URL)을 비동기로 채움
    /// 4) InfiniteScroll에 데이터 삽입 후 갱신
    ///
    /// 또한 Invite 탭에서는
    /// - presence 구독 불필요 → UnhookPresenceListeners
    /// - 채팅 토스트 구독 정책상 불필요 → UnhookChatToastSubs
    /// </summary>
    private async Task LoadInviteListAsync()
    {
        UnhookPresenceListeners();
        UnhookChatToastSubs(); // Invite 탭에선 채팅 토스트 구독 필요 없음(정책)

        string keyword = inputInviteSearch ? inputInviteSearch.text : "";
        keyword = (keyword ?? "").Trim();

        // 1) 후보 조회
        var rows = string.IsNullOrEmpty(keyword)
            ? await FriendService.GetInviteCandidatesDefaultAsync(100)
            : await FriendService.SearchInviteCandidatesByPrefixAsync(keyword, 50);

        // 2) 내 상태 읽기 (friends/out/in)
        var myUid = _myUid;

        var friendsSnapTask = FirebaseDatabase.DefaultInstance.GetReference($"friends/{myUid}").GetValueAsync();
        var outSnapTask = FirebaseDatabase.DefaultInstance.GetReference($"friendRequestsOut/{myUid}").GetValueAsync();
        var inSnapTask = FirebaseDatabase.DefaultInstance.GetReference($"friendRequestsIn/{myUid}").GetValueAsync();
        await Task.WhenAll(friendsSnapTask, outSnapTask, inSnapTask);

        var friendSet = new HashSet<string>();
        var outSet = new HashSet<string>();
        var inSet = new HashSet<string>();

        var friendsSnap = friendsSnapTask.Result;
        if (friendsSnap != null && friendsSnap.Exists)
            foreach (var c in friendsSnap.Children) friendSet.Add(c.Key);

        var outSnap = outSnapTask.Result;
        if (outSnap != null && outSnap.Exists)
            foreach (var c in outSnap.Children) outSet.Add(c.Key);

        var inSnap = inSnapTask.Result;
        if (inSnap != null && inSnap.Exists)
            foreach (var c in inSnap.Children) inSet.Add(c.Key);

        // 3) 리스트 구성 + 프로필
        var dataList = new List<FriendListItemData>();
        var tasks = new List<Task>();

        foreach (var r in rows)
        {
            // 이미 친구면 후보에서 제외(너 기존 정책 유지)
            if (friendSet.Contains(r.uid)) continue;

            var state = FriendListItemData.InviteState.None;
            if (outSet.Contains(r.uid)) state = FriendListItemData.InviteState.Outgoing;
            else if (inSet.Contains(r.uid)) state = FriendListItemData.InviteState.Incoming;

            var item = new FriendListItemData
            {
                mode = FriendListItemData.RowMode.InviteCandidate,
                uid = r.uid,
                nick = r.nickKey, // 임시
                photoUrl = "",
                inviteState = state,
                isOnline = false
            };

            dataList.Add(item);
            tasks.Add(LoadProfileIntoItemAsync(item));
        }

        await Task.WhenAll(tasks);

        FriendInfiniteScrollUtil.ClearAll(friendScroll);
        for (int i = 0; i < dataList.Count; i++)
        {
            var mF = dataList[i];
            var data = new FriendListItemData
            {
                mode = FriendListItemData.RowMode.InviteCandidate,
                uid = mF.uid,
                nick = mF.nick,
                photoUrl = mF.photoUrl,
                inviteState = mF.inviteState,
                isOnline = false
            };
            FriendInfiniteScrollUtil.Insert(friendScroll, data, i);
        }
        FriendInfiniteScrollUtil.UpdateAll(friendScroll);
    }

    /// <summary>
    /// Invite 후보 item에 프로필 정보(닉, 사진URL)를 채운다.
    /// - FriendService.GetUserProfileBasicAsync(uid)를 통해 조회
    /// - item.nick, item.photoUrl 갱신
    /// </summary>
    private static async Task LoadProfileIntoItemAsync(FriendListItemData item)
    {
        // userPublic 기반으로 읽도록 FriendService 내부가 되어 있어야 베스트
        var (nick, photoUrl) = await FriendService.GetUserProfileBasicAsync(item.uid);
        if (!string.IsNullOrWhiteSpace(nick)) item.nick = nick;
        item.photoUrl = (photoUrl ?? "").Trim();
    }

    #endregion

    #region Friends List

    /// <summary>
    /// Friends 탭 목록 로드:
    /// 1) 내 친구 목록 FriendService.GetMyFriendsAsync 조회
    /// 2) 검색 키워드가 있으면 닉네임 포함 검색으로 필터
    /// 3) InfiniteScroll 갱신 + _friendByUid 캐시에 저장
    /// 4) presence 구독을 현재 친구 uid 목록 기준으로 Sync
    /// 5) 채팅 토스트/자동오픈을 위한 메시지 구독도 현재 친구 목록 기준으로 Sync
    /// </summary>
    private async Task LoadFriendsListAsync()
    {
        string keyword = inputCurrentFriend ? inputCurrentFriend.text : "";
        keyword = (keyword ?? "").Trim();

        var friends = await FriendService.GetMyFriendsAsync();

        // 닉네임 검색 필터
        if (!string.IsNullOrEmpty(keyword))
        {
            friends = friends.FindAll(f =>
                !string.IsNullOrEmpty(f.nick) &&
                f.nick.ToLower().Contains(keyword.ToLower()));
        }

        FriendInfiniteScrollUtil.ClearAll(friendScroll);

        _friendByUid.Clear();
        var uids = new HashSet<string>();

        for (int i = 0; i < friends.Count; i++)
        {
            var f = friends[i];

            var data = new FriendListItemData
            {
                mode = FriendListItemData.RowMode.Friend,
                uid = f.uid,
                nick = f.nick,
                photoUrl = f.photoUrl,
                isOnline = f.isOnline
            };

            _friendByUid[data.uid] = data;
            uids.Add(data.uid);

            FriendInfiniteScrollUtil.Insert(friendScroll, data, i);
        }

        FriendInfiniteScrollUtil.UpdateAll(friendScroll);

        // 1) presence 구독
        SyncPresenceSubscriptions(uids);

        // 2) 채팅 토스트/자동 오픈을 위한 메시지 구독
        SyncChatToastSubscriptions(friends);
    }

    #endregion

    #region Friend Delete (Confirm Toast)

    /// <summary>
    /// Friends 탭에서 "삭제" 버튼을 눌렀을 때 호출.
    /// - requestToast가 있으면 "삭제 확인" 토스트를 띄운다.
    /// - 토스트가 없다면 바로 삭제(DeleteFriendNowAsync)하는 보험 로직.
    /// - 이미 다른 토스트가 떠있으면 동작을 막아 상태 충돌 방지.
    /// </summary>
    public void OnClickDeleteButton(FriendListItemData d)
    {
        if (d == null) return;

        // 토스트 UI 없으면 바로 삭제(보험)
        if (requestToast == null)
        {
            _ = DeleteFriendNowAsync(d);
            return;
        }

        // 다른 토스트(요청 수락/거절)나 삭제 확인이 이미 떠있으면 막기
        if (_toastShowing || _deleteConfirmShowing) return;

        _deleteConfirmShowing = true;

        string nick = string.IsNullOrWhiteSpace(d.nick) ? "알 수 없음" : d.nick;

        // 확인창 띄우기
        requestToast.ShowConfirm(
            messageKor: $"{nick} 님을 친구에서 삭제할까요?",
            messageEng: $"Remove {nick} from your friends?",
            onConfirm: () => _ = OnConfirmDeleteAsync(d),
            onCancel: () => OnCancelDelete()
        );
    }

    /// <summary>
    /// 삭제 확인 토스트에서 "확인"을 눌렀을 때 실행되는 실제 삭제 처리
    ///
    /// 목적:
    /// - 친구 관계(friends) 양쪽 제거
    /// - "해당 친구와의 채팅방(messages)"만 삭제
    /// - 내 채팅 리스트용 인덱스(chatIndex)는 내 것만 삭제
    /// - 그 친구와 열려있던 채팅창(UI)도 즉시 닫고, 메모리 캐시/구독도 정리
    ///
    /// 처리 흐름:
    /// 1) 확인 토스트 닫기
    /// 2) 서버 데이터 삭제(친구/채팅/인덱스)
    /// 3) 내 로컬 UI/캐시/구독 즉시 정리(채팅창 강제 종료)
    /// 4) 성공 토스트 표시
    /// 5) 친구 리스트 리로드(화면 반영)
    ///
    /// finally:
    /// - 삭제 확인 상태 플래그 해제
    /// - 삭제 확인 때문에 멈춰있던 "친구요청 토스트 큐"가 있으면 이어서 표시
    /// </summary>
    private async Task OnConfirmDeleteAsync(FriendListItemData d)
    {
        try
        {
            // 1) 확인창 닫기
            requestToast.Hide();

            // 2) 서버 데이터 삭제(친구양쪽 + 채팅 messages + 내 chatIndex)
            await FriendService.RemoveFriendBothAsync(d.uid);

            // 3) 내 로컬 채팅 UI/캐시/구독 즉시 정리
            CloseAndClearChatWith(d.uid);

            // 4) 석옥 토스트 표시
            ToastMessageManager.instance?.ShowToast(
                $"{d.nick} 님을 친구에서 삭제했습니다.",
                $"Removed {d.nick} from friends."
            );

            // 5) 리스트 갱신
            await ReloadAsync();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[FriendDelete] fail: {e}");
            ToastMessageManager.instance?.ShowToast("친구 삭제 실패", "Failed to remove friend");
        }
        finally
        {
            _deleteConfirmShowing = false;

            // 삭제 확인 때문에 대기하던 친구요청 토스트 이어서 처리
            _toastShowing = false;
            _showingFromUid = null;

            await TryShowNextToastAsync();
        }
    }

    /// <summary>
    /// 특정 친구(friendUid)와의 채팅 관련 로컬 상태를 "그 친구 것만" 즉시 정리
    ///
    /// 정리 대상:
    /// 1) 열려있는 채팅창(UI) 닫기 + 파괴
    /// 2) 로컬 채팅 히스토리(_chatHistories) 제거
    /// 3) 채팅 토스트/자동오픈 구독(_chatToastSubs) 해제
    /// 4) 중복 토스트 방지용 마지막 msgId 기록(_lastToastMsgIdByChat) 제거
    ///
    /// 목적:
    /// - 친구 삭제 후에도 그 친구 채팅창이 떠있거나,
    /// - 과거 메시지가 로컬 캐시에 남아서 다시 보이거나,
    /// - 메시지 구독이 남아 토스트/자동오픈이 재발생하는 문제 방지
    /// </summary>
    private void CloseAndClearChatWith(string friendUid)
    {
        // (0) 방어: friendUid가 없으면 아무 것도 할 수 없음
        if (string.IsNullOrEmpty(friendUid)) return;

        // (1) 내 UID 보정
        if (string.IsNullOrEmpty(_myUid))
        {
            var user = FirebaseAuth.DefaultInstance.CurrentUser;
            if (user == null) return;
            _myUid = user.UserId;
        }
        // (2) "내 uid + 친구 uid"로 채팅방 고유 키(chatKey) 생성
        string chatKey = MakeChatId(_myUid, friendUid);

        // (3) 열려있는 채팅창이 있으면 닫고 제거
        // - Close() 내부에서:
        //   - Firebase ChildAdded 구독 해제(Unsubscribe)
        //   - _openedChatWindowCount 감소(입력 충돌 방지용 카운터)
        // - Destroy로 UI 오브젝트까지 파괴
        if (_chatWindows.TryGetValue(chatKey, out var win) && win != null)
        {
            // Close()에서 Unsubscribe + opened 카운트 감소 처리됨
            win.Close();
            Destroy(win.gameObject);
        }
        _chatWindows.Remove(chatKey);

        // (4) 로컬 히스토리 제거
        _chatHistories.Remove(chatKey);

        // (5) "채팅 토스트/자동오픈" 구독 해제
        if (_chatToastSubs.TryGetValue(chatKey, out var sub))
        {
            try { sub.q.ChildAdded -= sub.h; } catch { }
            _chatToastSubs.Remove(chatKey);
        }

        // (6) 마지막 토스트 msgId 기록 제거
        _lastToastMsgIdByChat.Remove(chatKey);
    }

    /// <summary>
    /// 삭제 확인에서 "취소" 눌렀을 때:
    /// - 확인 토스트 숨김
    /// - 플래그 해제
    /// - 대기 중인 친구요청 토스트가 있다면 이어서 표시
    /// </summary>
    private void OnCancelDelete()
    {
        requestToast.Hide();
        _deleteConfirmShowing = false;

        // 삭제 취소 후, 대기중인 요청 토스트 이어서 띄우기
        _ = TryShowNextToastAsync();
    }

    /// <summary>
    /// requestToast가 없을 때의 보험 로직:
    /// - 양쪽 friends에서 제거 후 리로드
    /// </summary>
    private async Task DeleteFriendNowAsync(FriendListItemData d)
    {
        try
        {
            await FriendService.RemoveFriendBothAsync(d.uid);
            await ReloadAsync();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[FriendDelete] fail(no toast): {e}");
        }
    }

    #endregion


    #region Presence

    /// <summary>
    /// Friends 탭에서 현재 표시 중인 uid 목록(uids) 기준으로 presence 구독을 동기화한다.
    /// - 리스트에서 사라진 uid는 구독 해제
    /// - 새로 등장한 uid는 구독 추가
    /// </summary>
    private void SyncPresenceSubscriptions(HashSet<string> uids)
    {
        // 필요없는 구독 제거
        var removeList = new List<string>();
        foreach (var kv in _presenceSubs)
            if (!uids.Contains(kv.Key)) removeList.Add(kv.Key);

        foreach (var uid in removeList)
            UnsubscribePresence(uid);

        // 신규 구독 추가
        foreach (var uid in uids)
        {
            if (_presenceSubs.ContainsKey(uid)) continue;
            SubscribePresence(uid);
        }
    }

    /// <summary>
    /// 특정 친구 uid의 presence/{uid}를 구독한다.
    /// - online / lastSeen 값을 읽어 ApplyPresence로 반영한다.
    /// - Firebase 이벤트는 메인 스레드가 아닐 수 있으므로 Queue로 넘긴다.
    /// </summary>
    private void SubscribePresence(string uid)
    {
        var r = FirebaseDatabase.DefaultInstance.GetReference($"presence/{uid}");

        EventHandler<ValueChangedEventArgs> h = (s, e) =>
        {
            if (e.DatabaseError != null) return;
            var snap = e.Snapshot;

            bool online = false;
            long lastSeen = 0;

            if (snap != null && snap.Exists)
            {
                online = TryBool(snap.Child("online").Value);
                lastSeen = TryLong(snap.Child("lastSeen").Value);
            }

            _mainThreadQueue.Enqueue(() => ApplyPresence(uid, online, lastSeen));
        };

        r.ValueChanged += h;
        _presenceSubs[uid] = (r, h);
    }

    /// <summary>
    /// 특정 uid의 presence 구독을 해제한다.
    /// </summary>
    private void UnsubscribePresence(string uid)
    {
        if (!_presenceSubs.TryGetValue(uid, out var sub)) return;
        try { sub.r.ValueChanged -= sub.h; } catch { }
        _presenceSubs.Remove(uid);
    }

    /// <summary>
    /// 현재 활성화된 presence 구독을 전부 해제한다.
    /// - 패널 닫힘 / 탭 전환(Invite) / Disable 시 호출
    /// </summary>
    private void UnhookPresenceListeners()
    {
        foreach (var kv in _presenceSubs)
        {
            try { kv.Value.r.ValueChanged -= kv.Value.h; } catch { }
        }
        _presenceSubs.Clear();
        _friendByUid.Clear();
        _presenceDirty = false;
    }

    /// <summary>
    /// presence 이벤트로 받은 online 상태를
    /// Friends 탭에 표시 중인 FriendListItemData에 반영한다.
    ///
    /// - online 값이 바뀌었으면 _presenceDirty=true로 표시
    /// - Update에서 일정 간격으로 ReloadAsync가 호출되어 UI가 갱신된다.
    /// </summary>
    private void ApplyPresence(string uid, bool online, long lastSeen)
    {
        if (!_friendByUid.TryGetValue(uid, out var data) || data == null) return;

        if (data.isOnline != online)
        {
            data.isOnline = online;
            _presenceDirty = true;
        }
    }

    /// <summary>
    /// Firebase에서 들어오는 값이 bool/int/string 등 다양할 수 있어
    /// 최대한 bool로 해석하는 유틸
    /// </summary>
    private static bool TryBool(object v)
    {
        if (v == null) return false;
        if (v is bool b) return b;
        if (bool.TryParse(v.ToString(), out var bb)) return bb;
        if (int.TryParse(v.ToString(), out var n)) return n != 0;
        return false;
    }

    /// <summary>
    /// Firebase 값(long/int/string)을 long으로 변환하는 유틸
    /// </summary>
    private static long TryLong(object v)
    {
        if (v == null) return 0;
        if (long.TryParse(v.ToString(), out var n)) return n;
        return 0;
    }

    #endregion

    #region Friend Request / Notifications

    /// <summary>
    /// friendRequestsIn/{myUid} 의 ValueChanged 리스너를 연결한다.
    /// - 받은 친구요청이 생기면 큐에 넣고 토스트를 순서대로 표시한다.
    /// - 이미 리스너가 연결돼 있으면 중복 등록하지 않는다.
    /// </summary>
    private void HookIncomingListener()
    {
        var user = FirebaseAuth.DefaultInstance.CurrentUser;
        if (user == null) return;

        if (_reqInRef != null) return;

        _myUid = user.UserId;

        _reqInRef = FirebaseDatabase.DefaultInstance.GetReference($"friendRequestsIn/{_myUid}");
        _reqInRef.ValueChanged += OnIncomingChanged;

        if (requestToast) requestToast.Hide();

        // 이미 들어와 있던 요청들도 큐에 넣어서 토스트 표시
        _ = EnqueueExistingIncomingAsync();
    }

    /// <summary>
    /// friendRequestsIn 리스너 해제
    /// </summary>
    private void UnhookIncomingListener()
    {
        if (_reqInRef != null)
            _reqInRef.ValueChanged -= OnIncomingChanged;
        _reqInRef = null;
    }

    /// <summary>
    /// 현재 DB에 존재하는 friendRequestsIn 을 1회 읽어서
    /// 기존 요청들도 토스트 큐에 넣고 표시한다.
    /// </summary>
    private async Task EnqueueExistingIncomingAsync()
    {
        var snap = await FirebaseDatabase.DefaultInstance.GetReference($"friendRequestsIn/{_myUid}").GetValueAsync();
        if (snap != null && snap.Exists)
        {
            foreach (var c in snap.Children)
            {
                string fromUid = c.Key;
                string fromNick = c.Child("fromNick").Value?.ToString() ?? "";
                EnqueueIncoming(fromUid, fromNick);
            }
            await TryShowNextToastAsync();
        }
    }

    /// <summary>
    /// friendRequestsIn/{myUid} 값 변경 시 호출되는 콜백.
    /// - snapshot 전체를 훑으며 요청들을 큐에 넣는다.
    /// - 이후 TryShowNextToastAsync로 토스트 표시를 시도한다.
    /// </summary>
    private void OnIncomingChanged(object sender, ValueChangedEventArgs e)
    {
        if (e.DatabaseError != null) return;
        if (e.Snapshot == null) return;

        if (e.Snapshot.Exists)
        {
            foreach (var c in e.Snapshot.Children)
            {
                string fromUid = c.Child("fromUid").Value?.ToString();
                if (string.IsNullOrEmpty(fromUid))
                    fromUid = c.Key;

                string fromNick = c.Child("fromNick").Value?.ToString() ?? "";
                EnqueueIncoming(fromUid, fromNick);
            }
        }

        _ = TryShowNextToastAsync();
    }

    /// <summary>
    /// 받은 친구요청을 토스트 큐에 넣는다.
    /// - showing 중인 uid는 제외
    /// - 이미 큐에 넣은 uid는 제외(중복 방지)
    /// </summary>
    private void EnqueueIncoming(string fromUid, string fromNick)
    {
        if (string.IsNullOrEmpty(fromUid)) return;
        if (_showingFromUid == fromUid) return;
        if (_queuedSet.Contains(fromUid)) return;

        _incomingQueue.Enqueue(new IncomingReq { fromUid = fromUid, fromNick = fromNick });
        _queuedSet.Add(fromUid);
    }

    /// <summary>
    /// notifications/{myUid} 의 ChildAdded 리스너 연결.
    /// - 친구 수락/거절/취소/삭제 등의 알림을 토스트로 보여주고,
    ///   읽은 알림은 서버에서 RemoveValueAsync로 삭제한다.
    /// </summary>
    private void HookNotificationListener()
    {
        if (string.IsNullOrEmpty(_myUid)) return;
        if (_notiRef != null) return;

        _notiRef = FirebaseDatabase.DefaultInstance.GetReference($"notifications/{_myUid}");
        _notiRef.ChildAdded += OnNotiChildAdded;
    }

    /// <summary>
    /// notifications 리스너 해제 + 중복 처리 기록 초기화
    /// </summary>
    private void UnhookNotificationListener()
    {
        if (_notiRef != null)
            _notiRef.ChildAdded -= OnNotiChildAdded;
        _notiRef = null;
        _notiHandled.Clear();
    }

    /// <summary>
    /// notifications/{myUid}에 새 알림이 추가되면 호출.
    /// - type에 따라 토스트 표시
    /// - 알림 노드는 서버에서 삭제(읽음 처리)
    /// - 이후 UI 리로드, 채팅 토스트 Prime 갱신 시도
    /// </summary>
    private void OnNotiChildAdded(object sender, ChildChangedEventArgs e)
    {
        if (e.DatabaseError != null) return;
        if (e.Snapshot == null || !e.Snapshot.Exists) return;

        string id = e.Snapshot.Key;
        if (string.IsNullOrEmpty(id)) return;

        // 중복 처리 방지
        if (_notiHandled.Contains(id)) return;
        _notiHandled.Add(id);

        string type = e.Snapshot.Child("type").Value?.ToString() ?? "";
        string byNick = e.Snapshot.Child("byNick").Value?.ToString() ?? "";
        string byUid = e.Snapshot.Child("byUid").Value?.ToString() ?? "";

        // 알림 타입별 토스트 메시지
        switch (type)
        {
            case "friend_accepted":
                ToastMessageManager.instance?.ShowToast($"{byNick} 님이 친구 요청을 수락했습니다.", $"{byNick} accepted your friend request.");
                break;
            case "friend_declined":
                ToastMessageManager.instance?.ShowToast($"{byNick} 님이 친구 요청을 거절했습니다.", $"{byNick} declined your friend request.");
                break;
            case "friend_canceled":
                ToastMessageManager.instance?.ShowToast($"{byNick} 님이 친구 요청을 취소했습니다.", $"{byNick} canceled the friend request.");
                break;
            case "friend_removed":
                ToastMessageManager.instance?.ShowToast($"{byNick} 님이 친구를 삭제했습니다.", $"{byNick} removed you from friends.");

                // 상대가 날 삭제했으니, 내 chatIndex 삭제 + 채팅 UI/로컬 정리
                _ = FriendService.DeleteMyChatIndexOnlyAsync(byUid);

                _mainThreadQueue.Enqueue(() =>
                {
                    CloseAndClearChatWith(byUid);
                });
                break;
        }

        // 서버 알림 삭제(읽음 처리)
        _ = FirebaseDatabase.DefaultInstance
            .GetReference($"notifications/{_myUid}/{id}")
            .RemoveValueAsync();

        // UI 갱신 및 채팅 토스트 구독을 최신 친구 목록 기준으로 갱신
        StartCoroutine(FriendUiCoroutine(0.5f));
        _ = PrimeChatToastSubscriptionsAsync();
    }

    /// <summary>
    /// 대기 중인 "받은 친구요청" 토스트를 하나 꺼내 표시한다.
    /// - 이미 토스트가 떠있으면 리턴
    /// - requestToast가 없으면 리턴
    /// - 큐가 비어있으면 리턴
    ///
    /// 표시 흐름:
    /// 1) 큐에서 요청 pop
    /// 2) 닉/프로필이 없으면 FriendService로 조회
    /// 3) 프로필 이미지 다운로드(캐시)
    /// 4) requestToast.Show로 UI 표시
    /// 5) Accept/Decline 버튼 콜백에서 FriendService 처리 후 다음 토스트로 넘어감
    /// </summary>
    private async Task TryShowNextToastAsync()
    {
        if (_toastShowing) return;
        if (requestToast == null) return;
        if (_incomingQueue.Count == 0) return;

        _toastShowing = true;

        var req = _incomingQueue.Dequeue();
        _queuedSet.Remove(req.fromUid);

        _showingFromUid = req.fromUid;

        string nick = req.fromNick;
        string photoUrl = "";

        // 닉네임이 없다면 프로필을 추가 조회
        if (string.IsNullOrWhiteSpace(nick))
        {
            var p = await FriendService.GetUserProfileBasicAsync(req.fromUid);
            nick = p.nick;
            photoUrl = p.photoUrl;
        }

        // 프로필 이미지 다운로드(캐시)
        Texture tex = null;
        if (!string.IsNullOrWhiteSpace(photoUrl))
            tex = await FriendProfileImageCache.GetAsync(photoUrl);

        // 요청 토스트 표시 + 수락/거절 콜백 연결
        requestToast.Show(
            nick: string.IsNullOrWhiteSpace(nick) ? "알 수 없음" : nick,
            photo: tex,
            onAccept: () => _ = OnAcceptAsync(req.fromUid),
            onDecline: () => _ = OnDeclineAsync(req.fromUid)
        );
    }

    /// <summary>
    /// 친구 요청 수락:
    /// - 토스트 숨김
    /// - FriendService.AcceptFriendRequestAsync 실행
    /// - 토스트 상태 플래그 초기화
    /// - 다음 대기 토스트 표시
    /// </summary>
    private async Task OnAcceptAsync(string fromUid)
    {
        requestToast.Hide();
        await FriendService.AcceptFriendRequestAsync(fromUid);

        _showingFromUid = null;
        _toastShowing = false;

        await TryShowNextToastAsync();
    }

    /// <summary>
    /// 친구 요청 거절:
    /// - 토스트 숨김
    /// - FriendService.DeclineFriendRequestAsync 실행
    /// - 토스트 상태 플래그 초기화
    /// - 다음 대기 토스트 표시
    /// </summary>
    private async Task OnDeclineAsync(string fromUid)
    {
        requestToast.Hide();
        await FriendService.DeclineFriendRequestAsync(fromUid);

        _showingFromUid = null;
        _toastShowing = false;

        await TryShowNextToastAsync();
    }

    /// <summary>
    /// 약간의 지연 후 ReloadAsync 호출하는 코루틴(UX용)
    /// </summary>
    private IEnumerator FriendUiCoroutine(float delay)
    {
        yield return new WaitForSeconds(delay);
        _ = ReloadAsync();
    }

    #endregion

    #region Friend Chat: Open Window / History / Index

    /// <summary>
    /// 두 UID로부터 "항상 동일한 순서"의 chatKey를 만든다.
    /// - A_B 또는 B_A 형태
    /// - 정렬 규칙이 항상 같아야 같은 친구끼리 동일한 채팅방 키가 된다.
    /// </summary>
    public static string MakeChatId(string a, string b)
    {
        if (string.CompareOrdinal(a, b) < 0) return $"{a}_{b}";
        return $"{b}_{a}";
    }

    /// <summary>
    /// FriendChatWindow이 사용할 메모리 히스토리
    /// </summary>
    public List<FriendChatMessageData> GetOrCreateChatHistory(string chatKey)
    {
        if (!_chatHistories.TryGetValue(chatKey, out var list))
        {
            list = new List<FriendChatMessageData>(128);
            _chatHistories[chatKey] = list;
        }
        return list;
    }

    /// <summary>
    /// 내 닉네임 얻기(너 기존 계정 구조 유지)
    /// </summary>
    public string GetMyNick()
    {
        return FireBaseAuthManager.Instance.CurrentAccount.NickName;
    }

    /// <summary>
    /// chatIndex/{myUid}/{chatKey} 를 갱신한다.
    /// - 채팅 리스트(최근 메시지/상대/시간 표시)에 필요한 인덱스 데이터
    /// - 실제 메시지 내용을 전부 스캔하지 않아도 되게 보조하는 용도
    ///
    /// </summary>
    public async Task UpdateMyChatIndexAsync(string withUid, string withNick, string lastText, string lastFromUid, long ts)
    {
        if (string.IsNullOrEmpty(_myUid)) return;

        var root = FirebaseDatabase.DefaultInstance.RootReference;
        string chatKey = MakeChatId(_myUid, withUid);

        var updates = new Dictionary<string, object>
        {
            [$"chatIndex/{_myUid}/{chatKey}/withUid"] = withUid ?? "",
            [$"chatIndex/{_myUid}/{chatKey}/withNick"] = withNick ?? "",
            [$"chatIndex/{_myUid}/{chatKey}/lastText"] = lastText ?? "",
            [$"chatIndex/{_myUid}/{chatKey}/lastFromUid"] = lastFromUid ?? "",
            [$"chatIndex/{_myUid}/{chatKey}/lastTs"] = ts
        };

        await root.UpdateChildrenAsync(updates);
    }

    /// <summary>
    /// 친구 리스트 아이템에서 액션 선택(채팅/초대/프로필 등)
    /// - actionInt: 0=None, 1=Chat, 2=Invite, 3=Profile
    /// - Chat/Invite는 오프라인이면 막는 정책 적용
    /// </summary>
    public void OnFriendActionSelected(FriendListItemData d, int actionInt)
    {
        if (d == null) return;

        // 0=None, 1=Chat, 2=Profile
        switch (actionInt)
        {
            case 1: // Chat
                if (!d.isOnline)
                {
                    ToastMessageManager.instance?.ShowToast("오프라인 상태입니다.", "User is offline.");
                    return;
                }
                OpenFriendChatWindow(d.uid, d.nick);
                break;

            case 2: // Profile
                Debug.Log($"[FriendAction] View profile {d.nick} ({d.uid})");            
                break;
        }
    }

    /// <summary>
    /// 친구 리스트에서 선택한 친구의 프로필 팝업을 연다.
    /// 
    /// 역할:
    /// 1) 전달받은 친구 데이터가 유효한지 확인
    /// 2) 친구 리스트에 이미 표시 중인 프로필 이미지 텍스처를 팝업에 그대로 전달
    /// 3) 친구 uid를 기준으로 상세 프로필 정보(닉네임/레벨/마지막 접속일/장착 주사위)를 불러오도록 팝업 Open 호출
    /// 
    /// 왜 이렇게 하는가:
    /// - 친구 리스트에 이미 로드된 프로필 이미지를 재사용하면
    ///   다시 다운로드하지 않아도 되어 더 빠르고 자연스럽다.
    /// - 텍스트 정보는 uid 기준으로 별도 조회하고,
    ///   이미지는 현재 리스트에 보이는 것을 그대로 복사해서 사용한다.
    /// 
    /// 매개변수:
    /// - d : 선택한 친구의 데이터(uid, 닉네임 등 포함)
    /// - profileTexture : 친구 리스트 아이템에 현재 표시 중인 프로필 이미지 텍스처
    /// </summary>
    public void OpenFriendProfile(FriendListItemData d, Texture profileTexture)
    {
        if (friendProfilePopup == null || d == null) return;

        friendProfilePopup.SetProfileTexture(profileTexture);
        friendProfilePopup.Open(d.uid);
    }

    /// <summary>
    /// friendUid + friendNick만으로 채팅창을 연다.
    ///
    /// 동작:
    /// 1) 현재 로그인 uid 확보
    /// 2) chatKey 생성
    /// 3) 이미 열린 창이 있으면 그 창을 앞으로 가져오고 Open()
    /// 4) 없으면 prefab instantiate → Setup → Open()
    /// </summary>
    public void OpenFriendChatWindow(string friendUid, string friendNick)
    {
        if (string.IsNullOrEmpty(friendUid)) return;

        var user = FirebaseAuth.DefaultInstance.CurrentUser;
        if (user == null) return;

        _myUid = user.UserId;

        string chatKey = MakeChatId(_myUid, friendUid);

        // 이미 창 있으면 앞으로 + Open
        if (_chatWindows.TryGetValue(chatKey, out var win) && win != null)
        {
            win.Open();
            return;
        }

        if (chatWindowPrefab == null || chatWindowParent == null)
        {
            Debug.LogWarning("[Chat] Prefab/Parent not set.");
            return;
        }

        string safeNick = string.IsNullOrWhiteSpace(friendNick) ? "알 수 없음" : friendNick;

        var newWin = Instantiate(chatWindowPrefab, chatWindowParent);
        newWin.name = $"ChatWindow_{chatKey}";

        // FriendChatWindow 신버전 Setup 시그니처: (mgr, myUid, friendUid, friendNick)
        newWin.Setup(this, _myUid, friendUid, safeNick);

        _chatWindows[chatKey] = newWin;
        newWin.Open();
    }

    /// <summary>
    /// (FriendListItemData)에서 호출 편의용
    /// </summary>
    public void OpenFriendChatWindow(FriendListItemData d)
    {
        if (d == null) return;
        OpenFriendChatWindow(d.uid, d.nick);
    }

    /// <summary>
    /// FriendChatWindow가 "열릴 때" 호출해주도록 만들어둔 카운터 증가용 콜백
    /// </summary>
    internal void NotifyChatWindowOpened()
    {
        _openedChatWindowCount++;
        if (_openedChatWindowCount < 0) _openedChatWindowCount = 0;
    }

    /// <summary>
    /// FriendChatWindow가 "닫힐 때" 호출해주도록 만들어둔 카운터 감소용 콜백
    /// </summary>
    internal void NotifyChatWindowClosed()
    {
        _openedChatWindowCount--;
        if (_openedChatWindowCount < 0) _openedChatWindowCount = 0;
    }

    #endregion

    #region Chat Toast / Auto Open (핵심)

    /// <summary>
    /// Friends 탭에서 보여지는 친구 목록 기준으로
    /// chats/{uidA}/{uidB}/messages 의 "마지막 1개"를 구독한다.
    ///
    /// - 목적: chatIndex 없이도 새 메시지 도착 시
    ///   1) 토스트
    ///   2) 채팅창 자동 오픈
    ///
    /// - 주의: 구독 직후 기존 마지막 1개가 바로 들어오므로
    ///   msgId 기억해서 "초기 1회"는 무시한다.
    /// </summary>
    private void SyncChatToastSubscriptions(List<FriendRow> friends)
    {
        var need = new HashSet<string>();

        foreach (var f in friends)
        {
            if (string.IsNullOrEmpty(f.uid)) continue;

            BuildChatPath(_myUid, f.uid, out string chatKey, out string chatRootPath);
            need.Add(chatKey);

            if (_chatToastSubs.ContainsKey(chatKey))
                continue;

            // 마지막 1개만
            var q = FirebaseDatabase.DefaultInstance
                .GetReference($"{chatRootPath}/messages")
                .LimitToLast(1);

            // 초기 1회 무시 플래그
            bool primed = false;

            EventHandler<ChildChangedEventArgs> h = (s, e) =>
            {
                if (e.DatabaseError != null) return;
                if (e.Snapshot == null || !e.Snapshot.Exists) return;

                string msgId = e.Snapshot.Key;

                // 구독 직후 1회는 기존 메시지일 가능성이 큼 -> 토스트 X
                if (!primed)
                {
                    primed = true;
                    _lastToastMsgIdByChat[chatKey] = msgId;
                    return;
                }

                // msgId 중복 방지
                if (_lastToastMsgIdByChat.TryGetValue(chatKey, out var prev) && prev == msgId)
                    return;
                _lastToastMsgIdByChat[chatKey] = msgId;

                // 내가 보낸 메시지면 토스트 X
                string fromUid = e.Snapshot.Child("fromUid").Value?.ToString() ?? "";
                if (fromUid == _myUid) return; // 내가 보낸 건 토스트 X

                // 메시지 내용 / 보낸 사람 닉 / 타임스탬프
                string text = e.Snapshot.Child("text").Value?.ToString() ?? "";
                string fromNick = e.Snapshot.Child("fromNick").Value?.ToString() ?? (f.nick ?? "알 수 없음");

                long ts = 0;
                long.TryParse(e.Snapshot.Child("ts").Value?.ToString(), out ts);

                // 메인스레드에서 UI
                _mainThreadQueue.Enqueue(() =>
                {
                    ToastMessageManager.instance?.ShowToast(
                        $"{fromNick} : {text}",
                        $"{fromNick}: {text}"
                    );

                    // 채팅창 자동 오픈
                    OpenFriendChatWindow(f.uid, string.IsNullOrWhiteSpace(f.nick) ? fromNick : f.nick);
                });

                // (선택) 내 chatIndex도 갱신해두면 내 리스트 UI에 도움됨
                _ = UpdateMyChatIndexAsync(f.uid,
                                          string.IsNullOrWhiteSpace(f.nick) ? fromNick : f.nick,
                                          text,
                                          fromUid,
                                          ts);
            };

            q.ChildAdded += h;
            _chatToastSubs[chatKey] = (q, h);
        }

        // 필요 없는 구독 제거
        var remove = new List<string>();
        foreach (var k in _chatToastSubs.Keys)
            if (!need.Contains(k)) remove.Add(k);

        foreach (var k in remove)
        {
            var sub = _chatToastSubs[k];
            try { sub.q.ChildAdded -= sub.h; } catch { }
            _chatToastSubs.Remove(k);
            _lastToastMsgIdByChat.Remove(k);
        }
    }

    /// <summary>
    /// 채팅 토스트 구독을 전부 해제한다.
    /// - Disable / 로그아웃 / Invite 탭 전환 시 호출
    /// </summary>
    private void UnhookChatToastSubs()
    {
        foreach (var kv in _chatToastSubs)
        {
            try { kv.Value.q.ChildAdded -= kv.Value.h; } catch { }
        }
        _chatToastSubs.Clear();
        _lastToastMsgIdByChat.Clear();
    }

    /// <summary>
    /// chats/{uidA}/{uidB} 경로 만들기(항상 동일하게 정렬)
    /// </summary>
    private static void BuildChatPath(string a, string b, out string chatKey, out string chatRootPath)
    {
        if (string.CompareOrdinal(a, b) < 0)
        {
            chatKey = $"{a}_{b}";
            chatRootPath = $"chats/{a}/{b}";
        }
        else
        {
            chatKey = $"{b}_{a}";
            chatRootPath = $"chats/{b}/{a}";
        }
    }


    /// <summary>
    /// 로그인 직후(친구창을 열지 않아도) 채팅 토스트/자동오픈이 동작하도록
    /// 친구 목록을 1회 읽어 messages 구독을 "미리" 걸어둔다.
    ///
    /// - _chatToastPrimed && _chatToastSubs.Count>0 이면 중복 Prime 방지
    /// - 친구 uid를 _knownFriendUids에 캐시 (로그아웃/종료 시 정리용)
    /// - OnDisconnect 기반 채팅 기록 삭제 예약도 등록(RegisterChatCleanupOnDisconnectAsync)
    /// - SyncChatToastSubscriptions 로 메시지 구독 연결
    /// </summary>
    private async Task PrimeChatToastSubscriptionsAsync()
    {
        try
        {
            var user = FirebaseAuth.DefaultInstance.CurrentUser;
            if (user == null) return;

            _myUid = user.UserId;

            // 중복 Prime 방지 (이미 구독 걸렸으면 다시 안 건다)
            if (_chatToastPrimed && _chatToastSubs.Count > 0) return;

            var friends = await FriendService.GetMyFriendsAsync();
            if (friends == null) return;

            // 로그아웃/종료 때 서버 기록 삭제 필요하면 여기서 uid 캐시
            _knownFriendUids.Clear();
            foreach (var f in friends)
                if (!string.IsNullOrEmpty(f.uid)) _knownFriendUids.Add(f.uid);

            await RegisterChatCleanupOnDisconnectAsync();

            SyncChatToastSubscriptions(friends);

            _chatToastPrimed = true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ChatToast] Prime failed: {e.Message}");
        }
    }

    #endregion

    #region 로그아웃 시 채팅 기록 삭제

    /// <summary>
    /// (명시적으로 호출했을 때) 내 친구들과의 채팅 메시지(messages) 및 내 chatIndex를 삭제한다.
    ///
    /// - chats/{sortedA}/{sortedB}/messages 를 전부 RemoveValueAsync
    ///   → 양쪽 모두 채팅 메시지가 사라짐(공유 경로라면)
    /// - chatIndex/{myUid}/{chatKey} 를 RemoveValueAsync
    ///   → 내 채팅 리스트 인덱스만 삭제
    ///
    /// </summary>
    public async Task DeleteAllChatMessagesBeforeLogoutAsync()
    {
        var user = FirebaseAuth.DefaultInstance.CurrentUser;
        if (user == null) return;

        string myUid = user.UserId;

        // friend uid 캐시가 없으면 한번 읽어서 채운다
        if (_knownFriendUids.Count == 0)
        {
            var friends = await FriendService.GetMyFriendsAsync();
            if (friends != null)
            {
                foreach (var f in friends)
                    if (!string.IsNullOrEmpty(f.uid)) _knownFriendUids.Add(f.uid);
            }
        }

        var root = FirebaseDatabase.DefaultInstance.RootReference;
        var tasks = new List<Task>();

        foreach (var fid in _knownFriendUids)
        {
            if (string.IsNullOrEmpty(fid)) continue;

            // BuildChatPath랑 동일 정렬로 삭제
            string chatKey, chatRootPath;
            BuildChatPath(myUid, fid, out chatKey, out chatRootPath);

            // messages 전체 삭제 (양쪽 모두 기록 없어짐)
            tasks.Add(root.Child($"{chatRootPath}/messages").RemoveValueAsync());

            tasks.Add(root.Child($"chatIndex/{myUid}/{chatKey}").RemoveValueAsync());
        }

        await Task.WhenAll(tasks);
    }


    /// <summary>
    /// 앱 강제 종료/오프라인 상황에서도 서버가 자동으로 정리를 수행하도록
    /// Firebase OnDisconnect()를 이용해 messages/chatIndex 삭제를 예약한다.
    ///
    /// - messages : chats/{sortedA}/{sortedB}/messages → SetValue(null)
    /// - chatIndex: chatIndex/{myUid}/{chatKey} → SetValue(null)
    ///
    /// </summary>
    private async Task RegisterChatCleanupOnDisconnectAsync()
    {
        var user = FirebaseAuth.DefaultInstance.CurrentUser;
        if (user == null) return;

        _myUid = user.UserId;

        // 중복 등록 방지
        if (_chatCleanupOnDisconnectRegistered) return;

        // 친구 UID 확보
        if (_knownFriendUids.Count == 0)
        {
            var friends = await FriendService.GetMyFriendsAsync();
            if (friends != null)
            {
                foreach (var f in friends)
                    if (!string.IsNullOrEmpty(f.uid))
                        _knownFriendUids.Add(f.uid);
            }
        }

        var root = FirebaseDatabase.DefaultInstance.RootReference;
        var tasks = new List<Task>();

        foreach (var fid in _knownFriendUids)
        {
            if (string.IsNullOrEmpty(fid)) continue;

            BuildChatPath(_myUid, fid, out string chatKey, out string chatRootPath);

            // 채팅 메시지 "전체" 삭제 예약 (오프라인/강제종료 시 서버가 실행)
            var msgRef = root.Child($"{chatRootPath}/messages");

            // SDK에 RemoveValue()가 있으면 그걸 쓰고,
            // 없으면 SetValue(null)로 대체 가능
            // tasks.Add(msgRef.OnDisconnect().RemoveValue());
            tasks.Add(msgRef.OnDisconnect().SetValue(null));

            // (선택) 내 chatIndex도 삭제 예약 (내쪽만)
            var indexRef = root.Child($"chatIndex/{_myUid}/{chatKey}");
            // tasks.Add(indexRef.OnDisconnect().RemoveValue());
            tasks.Add(indexRef.OnDisconnect().SetValue(null));
        }

        await Task.WhenAll(tasks);
        _chatCleanupOnDisconnectRegistered = true;

        Debug.Log("[ChatCleanup] OnDisconnect delete registered");
    }

    #endregion
}
