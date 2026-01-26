using Firebase.Auth;
using Firebase.Database;
using Gpm.Ui;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Concurrent;

public class FrinedUiManager : MonoBehaviour
{
    #region 변수
    [Header("Panel")]
    [SerializeField] GameObject friendPanel;

    [Header("Search Inputs")]
    public TMP_InputField inputInviteSearch;
    public TMP_InputField inputCurrentFriend;

    [Header("Tabs")]
    public Button btnInviteTab;   // "초대"
    public Button btnFriendsTab;  // "내 친구"
    public Button btnFriendSearch; //검색 버튼(탭에 따라 다른 검색 실행)
    public Button btnFriendCancel;

    [Header("InfiniteScroll")]
    public InfiniteScroll friendScroll;           // GPM InfiniteScroll
    public GameObject scrollContent;    // (선택) content root

    [Header("Friend Request Toast")]
    [SerializeField] FriendRequestToastUI requestToast; // 인스펙터 연결

    DatabaseReference _reqInRef;


    bool _deleteConfirmShowing = false;

    struct IncomingReq
    {
        public string fromUid;
        public string fromNick;
    }

    // 들어온 요청 큐
    readonly Queue<IncomingReq> _incomingQueue = new();
    readonly HashSet<string> _queuedSet = new(); // 중복 방지
    bool _toastShowing = false;

    string _myUid;
    string _showingFromUid = null;

    bool isFriendOpen = false;

    #region  알림 리스너 

    DatabaseReference _notiRef;
    readonly HashSet<string> _notiHandled = new(); // 중복 방지(옵션)
    #endregion
    [SerializeField] enum TabMode { Invite, Friends }
    [Header("친구 탭")]
    [SerializeField] TabMode _mode = TabMode.Friends;

    CancellationTokenSource _cts;

    public static FrinedUiManager instance;


    #region Presence Watch (친구 온라인/오프라인 실시간 반영)

    // uid -> (ref, handler)
    private readonly Dictionary<string, (DatabaseReference r, EventHandler<ValueChangedEventArgs> h)> _presenceSubs
    = new();

    // 현재 Friends 탭에서 보여주는 데이터 (UID로 바로 찾아서 업데이트)
    private readonly Dictionary<string, FriendListItemData> _friendByUid
    = new();

    // Queue는 스레드 세이프한 ConcurrentQueue로 
    private readonly ConcurrentQueue<Action> _mainThreadQueue = new();

    // presence 변경이 들어왔을 때 UI 갱신을  몇초마다로 제한
    [SerializeField] private bool _presenceDirty = false;

    // UI 갱신 주기(원하는 값으로 조절: 0.3f~2f 추천)
    [SerializeField] private float presenceUiRefreshInterval = 0.5f;
    [SerializeField ]private float _nextPresenceUiRefresh = 0f;
    #endregion

    #endregion


    private void Awake()
    {
        instance = this;
        friendPanel.SetActive(false);
        isFriendOpen = false;

        if (btnInviteTab) btnInviteTab.onClick.AddListener(() => SwitchTab(TabMode.Invite));
        if (btnFriendsTab) btnFriendsTab.onClick.AddListener(() => SwitchTab(TabMode.Friends));

        if (btnFriendSearch) btnFriendSearch.onClick.AddListener(() => _ = SearchByCurrentTabAsync());

        if (btnFriendCancel) btnFriendCancel.onClick.AddListener(() => FriendPanelOff());

        ApplyTabUI();
    }

    private void Update()
    {
        // 메인스레드 큐 처리(스레드 세이프)
        while (_mainThreadQueue.TryDequeue(out var a))
        {
            try { a?.Invoke(); } catch (Exception e) { Debug.LogWarning(e); }
        }

        // Friends 탭 + 패널 열림 상태에서만 UI 갱신
        if (isFriendOpen && _mode == TabMode.Friends)
        {
            if (_presenceDirty && Time.unscaledTime >= _nextPresenceUiRefresh)
            {
                _presenceDirty = false;
                _nextPresenceUiRefresh = Time.unscaledTime + presenceUiRefreshInterval;
                SwitchTab(TabMode.Friends);
            }
        }

        if (Input.GetKeyDown(KeyCode.F))
        {
            isFriendOpen = !isFriendOpen;

            BtnFriendPanelOnOff(isFriendOpen);
        }
    }

    async void OnEnable()
    {
        var user = FirebaseAuth.DefaultInstance.CurrentUser;
        if (user == null) return;

        _myUid = user.UserId;

        HookIncomingListener();      // 친구요청 토스트
        HookNotificationListener();  // 수락/거절 토스트

        if (isFriendOpen)
            await ReloadAsync();
    }

    void OnDisable()
    {
        UnhookIncomingListener();
        UnhookNotificationListener();
        UnhookPresenceListeners();
    }

    public void BtnFriendPanelOnOff(bool isActive)
    {
        isFriendOpen = isActive;

        friendPanel.SetActive(isFriendOpen);

        if (!isFriendOpen)
        {
            // 패널 닫히면 presence 구독 끊기
            UnhookPresenceListeners();
            return;
        }

        _ = ReloadAsync(); // 패널 열리면 현재 탭 기준으로 로드 (Friends면 구독도 시작됨)
    }

    void FriendPanelOff()
    {
        isFriendOpen = false;
        friendPanel.SetActive(isFriendOpen);

        UnhookPresenceListeners();
    }

    void SwitchTab(TabMode m)
    {
        _mode = m;

        ApplyTabUI();

        if (_mode != TabMode.Friends)
            UnhookPresenceListeners();

        _ = ReloadAsync(); // 탭 바꾸면 그 탭 기준으로 로드
    }

    /// <summary>
    /// 탭에 따라 어떤 입력창을 보여줄지 결정
    /// - Invite 탭: inputInviteSearch 켜짐
    /// - Friends 탭: inputCurrentFriend 켜짐
    /// </summary>
    void ApplyTabUI()
    {
        if (inputInviteSearch) inputInviteSearch.gameObject.SetActive(_mode == TabMode.Invite);
        if (inputCurrentFriend) inputCurrentFriend.gameObject.SetActive(_mode == TabMode.Friends);
    }

    async Task SearchByCurrentTabAsync()
    {
        // 검색 버튼 눌렀을 때만 검색 실행
        await ReloadAsync();
    }

    async Task ReloadAsync()
    {
        // 이전 로드 취소
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        try
        {
            if (_mode == TabMode.Invite)
                await LoadInviteListAsync();   //초대 후보 검색/전체
            else
                await LoadFriendsListAsync();  // 내 친구 검색/전체
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[FriendPanel] Reload failed: {e.Message}");
        }
    }

    public async void OnClickInviteButton(FriendListItemData d)
    {
        if (d == null) return;

        // 이미 친구면 아무것도 안함
        if (d.inviteState == FriendListItemData.InviteState.AlreadyFriend) return;

        // None이면 "요청 보내기"
        if (d.inviteState == FriendListItemData.InviteState.None)
        {
            await FriendService.SendFriendRequestAsync(d.uid, d.nick);
            ToastMessageManager.instance.ShowToast(
            $"{d.nick} 님에게 친구 요청을 보냈습니다.",
            $"Friend request sent to {d.nick}."
            );

            await ReloadAsync();
        }

    }

    public void OnClickDeleteButton(FriendListItemData d)
    {
        if (d == null) return;

        if (requestToast == null)
        {
            // 토스트 UI 없으면 기존처럼 바로 삭제(보험)
            _ = DeleteFriendNowAsync(d);
            return;
        }

        if (_toastShowing || _deleteConfirmShowing) return;

        _deleteConfirmShowing = true;
        string nick = string.IsNullOrWhiteSpace(d.nick) ? "알 수 없음" : d.nick;

        requestToast.ShowConfirm(
        messageKor: $"{nick} 님을 친구에서 삭제할까요?",
        messageEng: $"Remove {nick} from your friends?",
        onConfirm: () => _ = OnConfirmDeleteAsync(d),
        onCancel: () => OnCancelDelete()
        );
    }

    async Task OnConfirmDeleteAsync(FriendListItemData d)
    {
        try
        {
            requestToast.Hide();

            // 여기서만 실제 삭제 수행
            await FriendService.RemoveFriendBothAsync(d.uid);

            ToastMessageManager.instance.ShowToast(
            $"{d.nick} 님을 친구에서 삭제했습니다.",
            $"Removed {d.nick} from friends."
            );

            await ReloadAsync();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[FriendDelete] fail: {e}");
            ToastMessageManager.instance.ShowToast(
            "친구 삭제 실패",
            "Failed to remove friend"
            );
        }
        finally
        {
            _deleteConfirmShowing = false;

            // 친구요청 큐 토스트가 대기중이면 이어서 띄우기
            _toastShowing = false;
            _showingFromUid = null;
            await TryShowNextToastAsync();
        }
    }


    void OnCancelDelete()
    {
        requestToast.Hide();
        _deleteConfirmShowing = false;
        // 친구요청 토스트 대기중이면 이어서 띄우기
        _ = TryShowNextToastAsync();
    }


    // 보험용: requestToast가 null일 때 호출
    async Task DeleteFriendNowAsync(FriendListItemData d)
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

    #region 초대 후보 리스트

    async Task LoadInviteListAsync()
    {
        UnhookPresenceListeners();

        string keyword = inputInviteSearch ? inputInviteSearch.text : "";
        keyword = (keyword ?? "").Trim();

        // 1) nicknames에서 후보 가져오기
        var rows = string.IsNullOrEmpty(keyword)
           ? await FriendService.GetInviteCandidatesDefaultAsync(100)
           : await FriendService.SearchInviteCandidatesByPrefixAsync(keyword, 50);


        // 2) 상태용: friends / out / in 을 한 번씩 읽어서 HashSet 만들기
        var myUid = FirebaseAuth.DefaultInstance.CurrentUser.UserId;

        var friendsSnapTask = FirebaseDatabase.DefaultInstance.GetReference($"friends/{myUid}").GetValueAsync();
        var outSnapTask = FirebaseDatabase.DefaultInstance.GetReference($"friendRequestsOut/{myUid}").GetValueAsync();
        var inSnapTask = FirebaseDatabase.DefaultInstance.GetReference($"friendRequestsIn/{myUid}").GetValueAsync();

        await Task.WhenAll(friendsSnapTask, outSnapTask, inSnapTask);

        var friendSet = new HashSet<string>();
        var outSet = new HashSet<string>();
        var inSet = new HashSet<string>();

        var friendsSnap = friendsSnapTask.Result;
        if (friendsSnap != null && friendsSnap.Exists)
            foreach (var c in friendsSnap.Children) friendSet.Add(c.Key); // key = friendUid

        var outSnap = outSnapTask.Result;
        if (outSnap != null && outSnap.Exists)
            foreach (var c in outSnap.Children) outSet.Add(c.Key); // key = targetUid

        var inSnap = inSnapTask.Result;
        if (inSnap != null && inSnap.Exists)
            foreach (var c in inSnap.Children) inSet.Add(c.Key); // key = fromUid

        // 3) 리스트 만들기 + 프로필 채우기
        var dataList = new List<FriendListItemData>();
        var tasks = new List<Task>();
        foreach (var r in rows)
        {

            var state = FriendListItemData.InviteState.None;
            if (friendSet.Contains(r.uid)) state = FriendListItemData.InviteState.AlreadyFriend;
            else if (outSet.Contains(r.uid)) state = FriendListItemData.InviteState.Outgoing;
            else if (inSet.Contains(r.uid)) state = FriendListItemData.InviteState.Incoming;

            if (friendSet.Contains(r.uid))
                continue;


            var item = new FriendListItemData
            {
                mode = FriendListItemData.RowMode.InviteCandidate,
                uid = r.uid,
                nick = r.nickKey,    // 일단 임시 표시
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

    static async Task LoadProfileIntoItemAsync(FriendListItemData item)
    {
        // 너는 지금 users/{uid}를 읽는데 rules상 남의 users는 못 읽음.
        // userPublic을 만들었다면 여기서 userPublic/{uid}로 읽는 게 맞아.
        // (일단 네 FriendService.GetUserProfileBasicAsync가 users 읽는 버전이라면 거기부터 바꿔야 함)

        var (nick, photoUrl) = await FriendService.GetUserProfileBasicAsync(item.uid);
        if (!string.IsNullOrWhiteSpace(nick)) item.nick = nick;
        item.photoUrl = (photoUrl ?? "").Trim();
    }

    #endregion

    #region 내 친구 리스트
    async Task LoadFriendsListAsync()
    {
        // 내 친구 탭 검색어
        string keyword = inputCurrentFriend ? inputCurrentFriend.text : "";
        keyword = (keyword ?? "").Trim();

        var friends = await FriendService.GetMyFriendsAsync();

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

            // 이게 중요: 스크롤에 넣는 data 인스턴스를 map에 등록
            _friendByUid[data.uid] = data;
            uids.Add(data.uid);

            FriendInfiniteScrollUtil.Insert(friendScroll, data, i);
        }

        FriendInfiniteScrollUtil.UpdateAll(friendScroll);

        SyncPresenceSubscriptions(uids);
    }
    #endregion

    // ===== UI 버튼 액션(초대/삭제) =====
    public async Task OnClickRowAction(FriendListItemData d)
    {
        if (d.mode == FriendListItemData.RowMode.InviteCandidate)
        {
            // "초대" = 친구요청 보내기
            await FriendService.SendFriendRequestAsync(d.uid, d.nick);
            Debug.Log($"Friend request sent to {d.nick} ({d.uid})");
        }
        else
        {
            // "삭제" = 양쪽 삭제
            await FriendService.RemoveFriendBothAsync(d.uid);
            Debug.Log($"Friend removed {d.nick} ({d.uid})");
            await ReloadAsync();
        }
    }

    #region 친구 요청 시 토스트 메시지 띄여주는 기능

    void HookIncomingListener()
    {
        var user = FirebaseAuth.DefaultInstance.CurrentUser;
        if (user == null) return;

        if (_reqInRef != null) return; // 이미 훅돼있으면 중복 방지

        _myUid = user.UserId;

        _reqInRef = FirebaseDatabase.DefaultInstance.GetReference($"friendRequestsIn/{_myUid}");
        _reqInRef.ValueChanged += OnIncomingChanged;

        if (requestToast) requestToast.Hide();

        // 최초 1회도 읽어서 큐에 넣어두기(앱 켰을 때 이미 와있던 요청 처리)
        _ = EnqueueExistingIncomingAsync();
    }

    void UnhookIncomingListener()
    {
        if (_reqInRef != null)
            _reqInRef.ValueChanged -= OnIncomingChanged;
        _reqInRef = null;
    }

    async Task EnqueueExistingIncomingAsync()
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

    // ValueChanged 이벤트
    void OnIncomingChanged(object sender, ValueChangedEventArgs e)
    {
        if (e.DatabaseError != null) return;
        if (e.Snapshot == null) return;

        // 현재 들어온 요청 전체를 기준으로 큐에 추가(중복은 _queuedSet이 막음)
        if (e.Snapshot.Exists)
        {
            foreach (var c in e.Snapshot.Children)
            {
                string fromUid = c.Child("fromUid").Value?.ToString();
                if (string.IsNullOrEmpty(fromUid))
                    fromUid = c.Key; // 키=fromUid 구조면 fallback

                string fromNick = c.Child("fromNick").Value?.ToString() ?? "";
                EnqueueIncoming(fromUid, fromNick);
            }
        }

        // 메인스레드에서 토스트 띄우기 시도
        _ = TryShowNextToastAsync();
    }

    void EnqueueIncoming(string fromUid, string fromNick)
    {
        if (string.IsNullOrEmpty(fromUid)) return;

        if (_showingFromUid == fromUid) return;

        // 이미 큐에 있거나, 지금 띄우는 중이면 스킵
        if (_queuedSet.Contains(fromUid)) return;

        // 이미 친구인 경우는 토스트 안 띄우고 싶으면 여기서 친구 여부 체크 가능(옵션)
        _incomingQueue.Enqueue(new IncomingReq
        {
            fromUid = fromUid,
            fromNick = fromNick
        });
        _queuedSet.Add(fromUid);
    }

    #endregion

    #region 알림 훅 / 언훅

    void HookNotificationListener()
    {
        if (string.IsNullOrEmpty(_myUid)) return;
        if (_notiRef != null) return;

        _notiRef = FirebaseDatabase.DefaultInstance.GetReference($"notifications/{_myUid}");
        _notiRef.ChildAdded += OnNotiChildAdded;
    }

    void UnhookNotificationListener()
    {
        if (_notiRef != null)
            _notiRef.ChildAdded -= OnNotiChildAdded;
        _notiRef = null;
        _notiHandled.Clear();
    }

    #region 알림 도착시 토스트 + 즉시 삭제

    void OnNotiChildAdded(object sender, ChildChangedEventArgs e)
    {
        if (e.DatabaseError != null) return;
        if (e.Snapshot == null || !e.Snapshot.Exists) return;

        string id = e.Snapshot.Key;
        if (string.IsNullOrEmpty(id)) return;

        // 중복 방지(옵션)
        if (_notiHandled.Contains(id)) return;
        _notiHandled.Add(id);

        string type = e.Snapshot.Child("type").Value?.ToString() ?? "";
        string byNick = e.Snapshot.Child("byNick").Value?.ToString() ?? "";

        // 메시지 만들기
        switch (type)
        {
            case "friend_accepted":
                ToastMessageManager.instance.ShowToast(
                    $"{byNick} 님이 친구 요청을 수락했습니다.",
                    $"{byNick} accepted your friend request."
                );
                break;

            case "friend_declined":
                ToastMessageManager.instance.ShowToast(
                    $"{byNick} 님이 친구 요청을 거절했습니다.",
                    $"{byNick} declined your friend request."
                );
                break;

            case "friend_canceled":
                ToastMessageManager.instance.ShowToast(
                    $"{byNick} 님이 친구 요청을 취소했습니다.",
                    $"{byNick} canceled the friend request."
                );
                break;
            case "friend_removed":
                ToastMessageManager.instance.ShowToast(
                    $"{byNick} 님이 친구를 삭제했습니다.",
                    $"{byNick} removed you from friends."
                );
                break;
        }

        // "확인 처리" = DB에서 삭제 (다음 접속 때 안 뜸)
        _ = FirebaseDatabase.DefaultInstance
            .GetReference($"notifications/{_myUid}/{id}")
            .RemoveValueAsync();

        StartCoroutine(FriendUiCorotue(0.5f));
    }
    #endregion

    #endregion

    #region 토스트 메시지에서 수락 , 거절 처리

    async Task TryShowNextToastAsync()
    {
        if (_toastShowing) return;
        if (requestToast == null) return;
        if (_incomingQueue.Count == 0) return;

        _toastShowing = true;

        var req = _incomingQueue.Dequeue();
        _queuedSet.Remove(req.fromUid);

        _showingFromUid = req.fromUid;

        // 1) fromNick 우선 사용
        string nick = req.fromNick;

        // 2) 비어있으면 userPublic로 보완(옵션)
        string photoUrl = "";
        if (string.IsNullOrWhiteSpace(nick))
        {
            var p = await FriendService.GetUserProfileBasicAsync(req.fromUid);
            nick = p.nick;
            photoUrl = p.photoUrl;
        }

        Texture tex = null;
        if (!string.IsNullOrWhiteSpace(photoUrl))
            tex = await FriendProfileImageCache.GetAsync(photoUrl);

        requestToast.Show(
            nick: string.IsNullOrWhiteSpace(nick) ? "알 수 없음" : nick,
            photo: tex,
            onAccept: () => _ = OnAcceptAsync(req.fromUid),
            onDecline: () => _ = OnDeclineAsync(req.fromUid)
        );
    }

    async Task OnAcceptAsync(string fromUid)
    {
        requestToast.Hide();

        await FriendService.AcceptFriendRequestAsync(fromUid);

        _showingFromUid = null;
        _toastShowing = false;

        await TryShowNextToastAsync();
    }

    async Task OnDeclineAsync(string fromUid)
    {
        requestToast.Hide();

        await FriendService.DeclineFriendRequestAsync(fromUid);

        _showingFromUid = null;
        _toastShowing = false;

        await TryShowNextToastAsync();
    }

    IEnumerator FriendUiCorotue(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (_mode == TabMode.Invite)
        {
            SwitchTab(TabMode.Invite);
        }
        else
            SwitchTab(TabMode.Friends);
    }
    #endregion

    #region Presence Hook/Unhook/Apply

    private void SyncPresenceSubscriptions(HashSet<string> uids)
    {
        if (!isFriendOpen || _mode != TabMode.Friends)
        {
            UnhookPresenceListeners();
            return;
        }

        // 1) 필요 없는 구독 제거
        var removeList = new List<string>();
        foreach (var kv in _presenceSubs)
        {
            if (!uids.Contains(kv.Key))
                removeList.Add(kv.Key);
        }
        foreach (var uid in removeList)
            UnsubscribePresence(uid);


        // 2) 신규 구독 추가
        foreach (var uid in uids)
        {
            if (_presenceSubs.ContainsKey(uid)) continue;
            SubscribePresence(uid);
        }
    }


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


    private void UnsubscribePresence(string uid)
    {
        if (!_presenceSubs.TryGetValue(uid, out var sub)) return;
        try { sub.r.ValueChanged -= sub.h; } catch { }
        _presenceSubs.Remove(uid);
    }


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


    private void ApplyPresence(string uid, bool online, long lastSeen)
    {
        // 지금 표시중인 친구 목록에 없으면 무시
        if (!_friendByUid.TryGetValue(uid, out var data) || data == null)
            return;
        // 값이 실제로 바뀌었을 때만 dirty
        if (data.isOnline != online)
        {
            data.isOnline = online;
            _presenceDirty = true; // 몇 초마다 UpdateAll 하도록 트리거
        }
    }


    static bool TryBool(object v)
    {
        if (v == null) return false;
        if (v is bool b) return b;
        if (bool.TryParse(v.ToString(), out var bb)) return bb;
        // 0/1 형태 처리
        if (int.TryParse(v.ToString(), out var n)) return n != 0;
        return false;
    }

    static long TryLong(object v)
    {
        if (v == null) return 0;
        if (long.TryParse(v.ToString(), out var n)) return n;
        return 0;
    }

    #endregion
}
