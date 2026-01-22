using Firebase.Auth;
using Firebase.Database;
using Gpm.Ui;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class FrinedUiManager : MonoBehaviour
{
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

    bool isAccept = false;
    bool isDecine = false;

    private float _nextAcceptLightTick = 0f;
    private float _nextDecineLightTick = 0f;

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

    readonly Queue<System.Action> _mainThreadQueue = new();
    #endregion
    [SerializeField] enum TabMode { Invite, Friends }
    [Header("친구 탭")]
    [SerializeField] TabMode _mode = TabMode.Friends;

    CancellationTokenSource _cts;

    public static FrinedUiManager instance;


    private void Awake()
    {
        instance = this;
        friendPanel.SetActive(false);
        isFriendOpen = false;

        if (btnInviteTab) btnInviteTab.onClick.AddListener(() => SwitchTab(TabMode.Invite));
        if (btnFriendsTab) btnFriendsTab.onClick.AddListener(() => SwitchTab(TabMode.Friends));

        if (btnFriendSearch) btnFriendSearch.onClick.AddListener(() => _ = SearchByCurrentTabAsync());

        if (btnFriendCancel) btnFriendCancel.onClick.AddListener(() => FriendPanelOff());

        isAccept = false;
        isDecine = false;

        ApplyTabUI();
    }

    private void Update()
    {
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
        await ReloadAsync();      // 그 다음 (user 있을 때만)
    }

    void OnDisable()
    {
        UnhookIncomingListener();
        UnhookNotificationListener();
    }

    public void BtnFriendPanelOnOff(bool isActive)
    {
        isFriendOpen = isActive;

        friendPanel.SetActive(isFriendOpen);

        if (isFriendOpen)
            _ = ReloadAsync(); // 열리면 현재 탭 기준으로 전체/검색 로드
    }

    void FriendPanelOff()
    {
        isFriendOpen = false;
        friendPanel.SetActive(isFriendOpen);
    }

    void SwitchTab(TabMode m)
    {
        _mode = m;

        ApplyTabUI();
        if (isFriendOpen)
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
        // 이미 친구면 아무것도 안함
        if (d.inviteState == FriendListItemData.InviteState.AlreadyFriend) return;

        // None이면 "요청 보내기"
        if (d.inviteState == FriendListItemData.InviteState.None)
        {
            await FriendService.SendFriendRequestAsync(d.uid, d.nick);
            ToastMessageManager.instance.ShowToast($"{d.nick} 님에게 친구 요청을 보냈습니다.", $"Friend request sent to {d.nick}.");
            await ReloadAsync();
            return;
        }

    }

    public async void OnClickDeleteButton(FriendListItemData d)
    {
        await FriendService.RemoveFriendBothAsync(d.uid);
        await ReloadAsync();
    }

    #region 초대 후보 리스트

    async Task LoadInviteListAsync()
    {
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

        var dataList = new List<FriendListItemData>();
        foreach (var f in friends)
        {
            dataList.Add(new FriendListItemData
            {
                mode = FriendListItemData.RowMode.Friend,
                uid = f.uid,
                nick = f.nick,
                photoUrl = f.photoUrl,
                isOnline = f.isOnline
            });
        }

        FriendInfiniteScrollUtil.ClearAll(friendScroll);

        for (int i = 0; i < dataList.Count; i++)
        {
            var mF = dataList[i];

            var data = new FriendListItemData
            {
                mode = FriendListItemData.RowMode.Friend,
                uid = mF.uid,
                nick = mF.nick,
                photoUrl = mF.photoUrl,
                isOnline = mF.isOnline
            };
            FriendInfiniteScrollUtil.Insert(friendScroll, data, i);
        }

        FriendInfiniteScrollUtil.UpdateAll(friendScroll);
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
}
