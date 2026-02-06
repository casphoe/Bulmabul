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

public class FrinedUiManager : MonoBehaviour
{
    #region Singleton / Inspector

    public static FrinedUiManager instance;

    [Header("Panel")]
    [SerializeField] private GameObject friendPanel;

    [Header("Search Inputs")]
    public TMP_InputField inputInviteSearch;
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

    private string _myUid;
    private bool isFriendOpen = false;

    [SerializeField] private enum TabMode { Invite, Friends }
    [Header("현재 탭")]
    [SerializeField] private TabMode _mode = TabMode.Friends;

    private CancellationTokenSource _cts;

    // 메인 스레드로 UI 처리 넘기기
    private readonly ConcurrentQueue<Action> _mainThreadQueue = new();

    #endregion

    #region Friend Request (Incoming) Toast Queue

    private DatabaseReference _reqInRef;

    private struct IncomingReq
    {
        public string fromUid;
        public string fromNick;
    }

    private readonly Queue<IncomingReq> _incomingQueue = new();
    private readonly HashSet<string> _queuedSet = new(); // 중복 방지
    private bool _toastShowing = false;
    private string _showingFromUid = null;

    private bool _deleteConfirmShowing = false;

    #endregion

    #region Notifications Listener

    private DatabaseReference _notiRef;
    private readonly HashSet<string> _notiHandled = new(); // 중복 방지

    #endregion

    #region Presence Watch (Friends 탭에서 온라인 표시)

    // uid -> (ref, handler)
    private readonly Dictionary<string, (DatabaseReference r, EventHandler<ValueChangedEventArgs> h)> _presenceSubs
        = new();

    // 현재 Friends 탭에서 사용중인 itemData 참조(UID로 바로 업데이트)
    private readonly Dictionary<string, FriendListItemData> _friendByUid
        = new();

    [SerializeField] private bool _presenceDirty = false;
    [SerializeField] private float presenceUiRefreshInterval = 0.5f;
    private float _nextPresenceUiRefresh = 0f;

    #endregion

    #region Friend Chat

    [Header("Friend Chat Windows")]
    public Transform chatWindowParent;
    public FriendChatWindow chatWindowPrefab;

    // chatKey(=MakeChatId) -> window
    private readonly Dictionary<string, FriendChatWindow> _chatWindows = new();

    // chatKey -> history
    private readonly Dictionary<string, List<FriendChatMessageData>> _chatHistories = new();

    private int _openedChatWindowCount = 0;
    public bool IsAnyChatWindowOpen => _openedChatWindowCount > 0;

    // 토스트/자동오픈은 chatIndex가 아니라 "messages 직접 구독"으로 처리한다.
    // chatKey -> (query, handler)
    private readonly Dictionary<string, (Query q, EventHandler<ChildChangedEventArgs> h)> _chatToastSubs
        = new();

    // chatKey -> 마지막 본 msgId (초기 1회/중복 토스트 방지)
    private readonly Dictionary<string, string> _lastToastMsgIdByChat
        = new();

    private bool _chatToastPrimed = false;
    private readonly HashSet<string> _knownFriendUids = new(); // 로그아웃 전 친구 uid 캐시

    private string _lastAuthUid = null;

    private bool _chatCleanupOnDisconnectRegistered = false;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        instance = this;

        if (friendPanel) friendPanel.SetActive(false);
        isFriendOpen = false;

        if (btnInviteTab) btnInviteTab.onClick.AddListener(() => SwitchTab(TabMode.Invite));
        if (btnFriendsTab) btnFriendsTab.onClick.AddListener(() => SwitchTab(TabMode.Friends));
        if (btnFriendSearch) btnFriendSearch.onClick.AddListener(() => _ = SearchByCurrentTabAsync());
        if (btnFriendCancel) btnFriendCancel.onClick.AddListener(() => FriendPanelOff());

        ApplyTabUI();
    }

    private void Update()
    {
        // ===== 메인스레드 큐 처리 =====
        while (_mainThreadQueue.TryDequeue(out var a))
        {
            try { a?.Invoke(); } catch (Exception e) { Debug.LogWarning(e); }
        }

        // ===== Presence UI 갱신(부하 제한) =====
        if (isFriendOpen && _mode == TabMode.Friends)
        {
            if (_presenceDirty && Time.unscaledTime >= _nextPresenceUiRefresh)
            {
                _presenceDirty = false;
                _nextPresenceUiRefresh = Time.unscaledTime + presenceUiRefreshInterval;

                // Friends 리스트 다시 뿌리기
                _ = ReloadAsync();
            }
        }

        // F 키로 친구 패널 토글(너 기존 로직 유지)
        if (Input.GetKeyDown(KeyCode.F))
        {
            if (IsAnyChatWindowOpen) return;
            isFriendOpen = !isFriendOpen;
            BtnFriendPanelOnOff(isFriendOpen);
        }
    }

    private void OnEnable()
    {
        FirebaseAuth.DefaultInstance.StateChanged += OnAuthStateChanged;
        TryInitAfterLogin(); // 이미 로그인 상태면 즉시 초기화
    }

    private void OnDisable()
    {
        FirebaseAuth.DefaultInstance.StateChanged -= OnAuthStateChanged;

        // 모든 리스너/구독 해제
        UnhookIncomingListener();
        UnhookNotificationListener();
        UnhookPresenceListeners();
        UnhookChatToastSubs();
    }

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

        _notiHandled.Clear();

        // Prime 상태 리셋
        _chatToastPrimed = false;
        _knownFriendUids.Clear();

        _myUid = null;
    }

    /// <summary>
    /// 로그인된 유저가 생기면 uid 확보 + 리스너 훅
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

    private static async Task LoadProfileIntoItemAsync(FriendListItemData item)
    {
        // userPublic 기반으로 읽도록 FriendService 내부가 되어 있어야 베스트
        var (nick, photoUrl) = await FriendService.GetUserProfileBasicAsync(item.uid);
        if (!string.IsNullOrWhiteSpace(nick)) item.nick = nick;
        item.photoUrl = (photoUrl ?? "").Trim();
    }

    #endregion

    #region Friends List

    private async Task LoadFriendsListAsync()
    {
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

    private async Task OnConfirmDeleteAsync(FriendListItemData d)
    {
        try
        {
            // 확인창 닫기
            requestToast.Hide();

            // 실제 삭제 수행(양쪽 friends에서 제거)
            await FriendService.RemoveFriendBothAsync(d.uid);

            ToastMessageManager.instance?.ShowToast(
                $"{d.nick} 님을 친구에서 삭제했습니다.",
                $"Removed {d.nick} from friends."
            );

            // 리스트 갱신
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

    private void OnCancelDelete()
    {
        requestToast.Hide();
        _deleteConfirmShowing = false;

        // 삭제 취소 후, 대기중인 요청 토스트 이어서 띄우기
        _ = TryShowNextToastAsync();
    }

    // requestToast가 없을 때의 보험용
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
        if (!_friendByUid.TryGetValue(uid, out var data) || data == null) return;

        if (data.isOnline != online)
        {
            data.isOnline = online;
            _presenceDirty = true;
        }
    }

    private static bool TryBool(object v)
    {
        if (v == null) return false;
        if (v is bool b) return b;
        if (bool.TryParse(v.ToString(), out var bb)) return bb;
        if (int.TryParse(v.ToString(), out var n)) return n != 0;
        return false;
    }

    private static long TryLong(object v)
    {
        if (v == null) return 0;
        if (long.TryParse(v.ToString(), out var n)) return n;
        return 0;
    }

    #endregion

    #region Friend Request / Notifications

    private void HookIncomingListener()
    {
        var user = FirebaseAuth.DefaultInstance.CurrentUser;
        if (user == null) return;

        if (_reqInRef != null) return;

        _myUid = user.UserId;

        _reqInRef = FirebaseDatabase.DefaultInstance.GetReference($"friendRequestsIn/{_myUid}");
        _reqInRef.ValueChanged += OnIncomingChanged;

        if (requestToast) requestToast.Hide();

        _ = EnqueueExistingIncomingAsync();
    }

    private void UnhookIncomingListener()
    {
        if (_reqInRef != null)
            _reqInRef.ValueChanged -= OnIncomingChanged;
        _reqInRef = null;
    }

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

    private void EnqueueIncoming(string fromUid, string fromNick)
    {
        if (string.IsNullOrEmpty(fromUid)) return;
        if (_showingFromUid == fromUid) return;
        if (_queuedSet.Contains(fromUid)) return;

        _incomingQueue.Enqueue(new IncomingReq { fromUid = fromUid, fromNick = fromNick });
        _queuedSet.Add(fromUid);
    }

    private void HookNotificationListener()
    {
        if (string.IsNullOrEmpty(_myUid)) return;
        if (_notiRef != null) return;

        _notiRef = FirebaseDatabase.DefaultInstance.GetReference($"notifications/{_myUid}");
        _notiRef.ChildAdded += OnNotiChildAdded;
    }

    private void UnhookNotificationListener()
    {
        if (_notiRef != null)
            _notiRef.ChildAdded -= OnNotiChildAdded;
        _notiRef = null;
        _notiHandled.Clear();
    }

    private void OnNotiChildAdded(object sender, ChildChangedEventArgs e)
    {
        if (e.DatabaseError != null) return;
        if (e.Snapshot == null || !e.Snapshot.Exists) return;

        string id = e.Snapshot.Key;
        if (string.IsNullOrEmpty(id)) return;

        if (_notiHandled.Contains(id)) return;
        _notiHandled.Add(id);

        string type = e.Snapshot.Child("type").Value?.ToString() ?? "";
        string byNick = e.Snapshot.Child("byNick").Value?.ToString() ?? "";

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
                break;
        }

        _ = FirebaseDatabase.DefaultInstance
            .GetReference($"notifications/{_myUid}/{id}")
            .RemoveValueAsync();

        StartCoroutine(FriendUiCoroutine(0.5f));
        _ = PrimeChatToastSubscriptionsAsync();
    }

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

    private async Task OnAcceptAsync(string fromUid)
    {
        requestToast.Hide();
        await FriendService.AcceptFriendRequestAsync(fromUid);

        _showingFromUid = null;
        _toastShowing = false;

        await TryShowNextToastAsync();
    }

    private async Task OnDeclineAsync(string fromUid)
    {
        requestToast.Hide();
        await FriendService.DeclineFriendRequestAsync(fromUid);

        _showingFromUid = null;
        _toastShowing = false;

        await TryShowNextToastAsync();
    }

    private IEnumerator FriendUiCoroutine(float delay)
    {
        yield return new WaitForSeconds(delay);
        _ = ReloadAsync();
    }

    #endregion

    #region Friend Chat: Open Window / History / Index

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
    /// FriendChatWindow에서 호출: "내 chatIndex만" 갱신 (rules상 안전)
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
    /// 친구 액션에서 채팅 열기
    /// </summary>
    public void OnFriendActionSelected(FriendListItemData d, int actionInt)
    {
        if (d == null) return;

        // 0=None, 1=Chat, 2=Invite, 3=Profile
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

            case 2: // Invite
                if (!d.isOnline)
                {
                    ToastMessageManager.instance?.ShowToast("오프라인 상태입니다.", "User is offline.");
                    return;
                }
                Debug.Log($"[FriendAction] Invite party to {d.nick} ({d.uid})");
                break;

            case 3: // Profile
                Debug.Log($"[FriendAction] View profile {d.nick} ({d.uid})");
                break;
        }
    }

    /// <summary>
    /// FriendListItemData 없이도 열 수 있게 friendUid/nick만 받는 버전
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

    internal void NotifyChatWindowOpened()
    {
        _openedChatWindowCount++;
        if (_openedChatWindowCount < 0) _openedChatWindowCount = 0;
    }

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

                string fromUid = e.Snapshot.Child("fromUid").Value?.ToString() ?? "";
                if (fromUid == _myUid) return; // 내가 보낸 건 토스트 X

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
    /// 로그인 직후(친구창 안 열어도) 채팅 토스트/자동오픈이 동작하도록
    /// 친구 목록을 1회 읽어 messages 구독을 "미리" 걸어둔다.
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
        }

        await Task.WhenAll(tasks);
    }



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
