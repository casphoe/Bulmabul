using Firebase.Auth;
using Firebase.Database;
using Gpm.Ui;
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

    // 들어온 요청 큐
    readonly Queue<string> _incomingQueue = new();
    readonly HashSet<string> _queuedSet = new(); // 중복 방지
    bool _toastShowing = false;

    string _myUid;
    string _showingFromUid = null;

    bool isFriendOpen = false;

    enum TabMode { Invite, Friends }
    TabMode _mode = TabMode.Friends;

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

        ApplyTabUI();
    }

    private void Update()
    {
        if(Input.GetKeyDown(KeyCode.F))
        {
            isFriendOpen = !isFriendOpen;

            friendPanel.SetActive(isFriendOpen);

            if (isFriendOpen)
                _ = ReloadAsync(); // 열리면 현재 탭 기준으로 전체/검색 로드
        }
    }

    async void OnEnable()
    {
        var user = FirebaseAuth.DefaultInstance.CurrentUser;
        if (user == null) return;

        _myUid = user.UserId;

        HookIncomingListener();   // 먼저
        await ReloadAsync();      // 그 다음 (user 있을 때만)
    }

    void OnDisable()
    {
        UnhookIncomingListener();
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
            ToastMessageManager.instance.ShowToast("친구 요청을 보냈습니다.", "Friend request sent.");
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

        // 비어있으면 전체(일부) / 입력 있으면 prefix 검색
        var rows = string.IsNullOrEmpty(keyword)
           ? await FriendService.GetInviteCandidatesDefaultAsync(100)
           : await FriendService.SearchInviteCandidatesByPrefixAsync(keyword, 50);

        // 2) 상태용: friends / out / in 을 한 번씩 읽어서 HashSet 만들기
        var myUid = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser.UserId;

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
        ApplyToScroll(dataList);
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

        ApplyToScroll(dataList);
    }
    #endregion

    void ApplyToScroll(List<FriendListItemData> dataList)
    {
        // 기존 데이터 제거 후 재세팅
        friendScroll.ClearData();

        // 아이템 클릭 행동 연결:
        // 방법1) 프리팹의 FriendListItem이 onClickAction을 가지고 있으니,
        // scroll의 item 생성 시점에 꽂아야 하는데,
        // GPM InfiniteScroll은 생성된 Item을 가져오기가 구조마다 달라.
        //
        // 가장 쉬운 방법은:
        // FriendListItem.UpdateData 안에서 static event를 호출하거나,
        // 혹은 itemPrefabRef.onClickAction 같은 전역 참조를 쓰는 방식.
        //
        // 여기서는 간단하게 "프리팹이 공용 static 이벤트"를 쓰는 방식 대신,
        // 너 프로젝트 스타일에 맞게 item 쪽에서 FriendPanelController.Instance를 호출하는 식으로 처리해도 됨.

        for (int i = 0; i < dataList.Count; i++)
            friendScroll.InsertData(dataList[i]);
    }

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
                EnqueueIncoming(fromUid);
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
                string fromUid = c.Key;
                EnqueueIncoming(fromUid);
            }
        }

        // 메인스레드에서 토스트 띄우기 시도
        _ = TryShowNextToastAsync();
    }

    void EnqueueIncoming(string fromUid)
    {
        if (string.IsNullOrEmpty(fromUid)) return;

        if (_showingFromUid == fromUid) return;

        // 이미 큐에 있거나, 지금 띄우는 중이면 스킵
        if (_queuedSet.Contains(fromUid)) return;

        // 이미 친구인 경우는 토스트 안 띄우고 싶으면 여기서 친구 여부 체크 가능(옵션)
        _incomingQueue.Enqueue(fromUid);
        _queuedSet.Add(fromUid);
    }

    #endregion

    #region 토스트 메시지에서 수락 , 거절 처리

    async Task TryShowNextToastAsync()
    {
        if (_toastShowing) return;
        if (requestToast == null) return;
        if (_incomingQueue.Count == 0) return;

        _toastShowing = true;

        string fromUid = _incomingQueue.Dequeue();
        _queuedSet.Remove(fromUid);

        _showingFromUid = fromUid;

     // 공개 프로필(userPublic)에서 닉/사진 읽기 (너가 만든 userPublic rules 전제)
     var (nick, photoUrl) = await FriendService.GetUserProfileBasicAsync(fromUid);

        // 사진 텍스처 (옵션)
        Texture tex = null;
        if (!string.IsNullOrWhiteSpace(photoUrl))
        {
            tex = await FriendProfileImageCache.GetAsync(photoUrl);
        }

        // 토스트 표시
        requestToast.Show(
            nick: string.IsNullOrWhiteSpace(nick) ? "알 수 없음" : nick,
            photo: tex,
            onAccept: () => _ = OnAcceptAsync(fromUid),
            onDecline: () => _ = OnDeclineAsync(fromUid)
        );
    }

    async Task OnAcceptAsync(string fromUid)
    {
        requestToast.Hide();

        try
        {
            await FriendService.AcceptFriendRequestAsync(fromUid);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Accept failed: {e.Message}");
        }
        _showingFromUid = null;
        _toastShowing = false;

        // 친구/초대 탭 갱신도 원하면
        if (isFriendOpen) await ReloadAsync();

        await TryShowNextToastAsync();
    }

    async Task OnDeclineAsync(string fromUid)
    {
        requestToast.Hide();

        try
        {
            await FriendService.DeclineFriendRequestAsync(fromUid);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Decline failed: {e.Message}");
        }

        _toastShowing = false;

        if (isFriendOpen) await ReloadAsync();

        await TryShowNextToastAsync();
    }

    #endregion
}
