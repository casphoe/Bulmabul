using Firebase.Auth;
using Firebase.Database;
using Gpm.Ui;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 방 안에서 빈 슬롯 + 버튼을 눌렀을 때 열리는 친구 초대 팝업.
/// 
/// 역할:
/// 1. 온라인 상태인 친구만 목록에 표시
/// 2. GPM InfiniteScroll로 친구 목록 표시
/// 3. 초대 버튼 클릭 시 Firebase에 초대 데이터 저장
/// 4. roomInviteOutbox를 감시해서 친구가 수락/거절/만료했는지 확인
/// 5. 거절/만료 시 해당 친구의 초대 버튼을 다시 활성화
/// </summary>
public class RoomInvitePopup : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject root; // 팝업 전체 Root
    [SerializeField] private Button btnClose; // 닫기 버튼

    [Header("Panels")]
    [SerializeField] private GameObject listParent;   // 친구 목록 영역, 예: Root의 0번째 자식
    [SerializeField] private GameObject emptyParent;  // Empty 안내 영역, 예: Root의 1번째 자식

    [Header("Text")]
    [SerializeField] private TMP_Text txtTitle; // 팝업 제목
    [SerializeField] private TMP_Text txtEmpty; // 친구 없음/로딩/에러 안내 텍스트

    [Header("GPM InfiniteScroll")]
    [SerializeField] private InfiniteScroll friendScroll; // 온라인 친구 목록 InfiniteScroll

    // 현재 초대할 방 정보
    private string _roomName;
    private MatchMode _mode;
    private int _map;
    private int _maxPlayers;

    // 방장이 보낸 초대의 상태를 확인하기 위한 Outbox Firebase 참조
    private DatabaseReference _outboxRef;

    // Outbox 리스너 중복 등록 방지
    private bool _listeningOutbox;

    // 친구 UID로 현재 화면에 표시된 InfiniteScroll 아이템을 찾기 위한 캐시
    // 거절/만료되었을 때 해당 친구의 버튼을 다시 활성화하기 위해 사용
    private readonly Dictionary<string, RoomInviteFriendItem> _itemByFriendUid = new();

    // 토스트 메시지 표시용 친구 닉네임 캐시
    private readonly Dictionary<string, string> _nickByFriendUid = new();

    // 친구 목록 중복 로딩 방지
    private bool _loading;

    private void Awake()
    {
        // 시작 시 팝업 숨김
        if (root != null)
            root.SetActive(false);

        // 닫기 버튼 연결
        if (btnClose != null)
        {
            btnClose.onClick.RemoveAllListeners();
            btnClose.onClick.AddListener(Close);
        }

        // 처음에는 목록 영역과 Empty 영역을 모두 꺼둔다
        SetListAndEmpty(false, false);
    }

    private void OnDisable()
    {
        // 오브젝트가 꺼질 때 Firebase 리스너 제거
        StopOutboxListen();
    }

    /// <summary>
    /// 친구 초대 팝업 열기.
    /// RoomManager에서 빈 슬롯 + 버튼을 눌렀을 때 호출된다.
    /// </summary>
    public async void Open(string roomName, MatchMode mode, int map, int maxPlayers)
    {
        _roomName = roomName;
        _mode = mode;
        _map = map;
        _maxPlayers = maxPlayers;

        if (root != null)
            root.SetActive(true);

        RefreshTitle();

        // 방장이 보낸 초대의 수락/거절 상태를 감시
        StartOutboxListen();

        // 온라인 친구 목록 로드
        await LoadOnlineFriendsAsync();
    }

    /// <summary>
    /// 친구 초대 팝업 닫기.
    /// 닫을 때 Outbox 리스너와 Scroll 데이터를 정리한다.
    /// </summary>
    public void Close()
    {
        StopOutboxListen();

        ClearScroll();

        SetListAndEmpty(false, false);

        _itemByFriendUid.Clear();
        _nickByFriendUid.Clear();

        if (root != null)
            root.SetActive(false);
    }

    /// <summary>
    /// 팝업 제목을 현재 언어에 맞게 갱신한다.
    /// </summary>
    private void RefreshTitle()
    {
        if (txtTitle == null) return;

        var lang = LaguageManager.Instance != null
            ? LaguageManager.Instance.currentLang
            : Lauaguage.Kor;

        txtTitle.text = lang == Lauaguage.Kor
            ? "친구 초대"
            : "Invite Friends";
    }

    /// <summary>
    /// 온라인 친구만 불러와서 GPM InfiniteScroll에 넣는다.
    /// 
    /// 처리 순서:
    /// 1. 기존 목록 초기화
    /// 2. FriendService에서 친구 목록 가져오기
    /// 3. isOnline == true인 친구만 필터링
    /// 4. InfiniteScroll에 RoomInviteFriendData 삽입
    /// </summary>
    private async Task LoadOnlineFriendsAsync()
    {
        if (_loading) return;
        _loading = true;

        ClearScroll();

        _itemByFriendUid.Clear();
        _nickByFriendUid.Clear();

        // 로딩 중에는 Empty 영역을 켜고 안내 문구 표시
        SetEmptyState("불러오는 중...", "Loading...");

        try
        {
            // 기존 친구 시스템에서 내 친구 목록 가져오기
            var friends = await FriendService.GetMyFriendsAsync();

            // 온라인 친구만 필터링
            friends = friends.FindAll(f => f != null && f.isOnline);

            // 온라인 친구가 없으면 Empty 영역 표시
            if (friends.Count == 0)
            {
                SetEmptyState(
                    "온라인 상태인 친구가 없습니다.",
                    "No online friends."
                );
                return;
            }

            // 온라인 친구가 있으면 목록 영역 표시
            SetListState();

            int insertIndex = 0;

            // InfiniteScroll 데이터 삽입
            for (int i = 0; i < friends.Count; i++)
            {
                var friend = friends[i];

                if (friend == null || string.IsNullOrWhiteSpace(friend.uid))
                    continue;

                // 닉네임 캐시 저장
                _nickByFriendUid[friend.uid] = friend.nick;

                var data = new RoomInviteFriendData
                {
                    friend = friend,

                    // 초대 버튼 클릭 시 호출
                    onClickInvite = OnClickInvite,

                    // InfiniteScroll 아이템이 바인딩될 때 호출
                    // UID -> Item 매핑을 저장하기 위해 필요
                    onBindItem = OnBindFriendItem
                };

                RoomInfiniteScrollUtil.Insert(friendScroll, data, insertIndex);
                insertIndex++;
            }

            RoomInfiniteScrollUtil.UpdateAll(friendScroll);
        }
        catch (Exception e)
        {
            Debug.LogError($"[RoomInvitePopup] Load failed: {e}");

            // 에러가 나도 Empty 영역에 안내 표시
            SetEmptyState(
                "친구 목록을 불러오지 못했습니다.",
                "Failed to load friends."
            );
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// InfiniteScroll 아이템이 실제 UI에 바인딩될 때 호출된다.
    /// 
    /// GPM InfiniteScroll은 아이템을 재사용하기 때문에,
    /// 친구가 거절했을 때 해당 친구의 버튼을 다시 활성화하려면
    /// 현재 화면의 아이템을 UID 기준으로 캐싱해둘 필요가 있다.
    /// </summary>
    private void OnBindFriendItem(FriendRow friend, RoomInviteFriendItem item)
    {
        if (friend == null || item == null) return;
        if (string.IsNullOrWhiteSpace(friend.uid)) return;

        // 친구 UID로 현재 표시 중인 아이템을 찾기 위한 캐시
        _itemByFriendUid[friend.uid] = item;

        // 토스트 메시지용 닉네임 캐시
        _nickByFriendUid[friend.uid] = friend.nick;
    }

    /// <summary>
    /// 초대 버튼 클릭.
    /// 
    /// 처리 순서:
    /// 1. Firebase roomInvites/{친구UID}/{inviteId}에 저장
    /// 2. Firebase roomInviteOutbox/{내UID}/{inviteId}에도 저장
    /// 3. 해당 친구 아이템의 버튼을 "초대 완료"로 비활성화
    /// </summary>
    private async void OnClickInvite(FriendRow friend, RoomInviteFriendItem item)
    {
        if (friend == null) return;

        try
        {
            await RoomInviteService.SendRoomInviteAsync(
                friend.uid,
                friend.nick,
                _roomName,
                _mode,
                _map,
                _maxPlayers
            );

            // 초대 전송 후에는 중복 초대를 막기 위해 버튼 비활성화
            item?.SetInvited();

            ToastMessageManager.instance?.ShowToast(
                $"{friend.nick}님에게 초대를 보냈습니다.",
                $"Invitation sent to {friend.nick}."
            );
        }
        catch (Exception e)
        {
            Debug.LogError($"[RoomInvitePopup] Invite failed: {e}");

            ToastMessageManager.instance?.ShowToast(
                "초대 전송에 실패했습니다.",
                "Failed to send invitation."
            );
        }
    }

    /// <summary>
    /// 내가 보낸 초대 상태를 확인하기 위해
    /// roomInviteOutbox/{내 UID} 경로를 감시한다.
    /// 
    /// 친구가 수락/거절/만료 처리하면 이 경로의 status도 변경된다.
    /// </summary>
    private void StartOutboxListen()
    {
        if (_listeningOutbox) return;

        var user = FirebaseAuth.DefaultInstance.CurrentUser;
        if (user == null)
        {
            Debug.LogWarning("[RoomInvitePopup] 로그인 유저가 없습니다.");
            return;
        }

        string myUid = user.UserId;

        _outboxRef = FirebaseDatabase.DefaultInstance
            .RootReference
            .Child("roomInviteOutbox")
            .Child(myUid);

        // 내가 보낸 초대 중 특정 초대의 상태가 바뀌면 호출
        _outboxRef.ChildChanged += OnOutboxChildChanged;
        _listeningOutbox = true;

        Debug.Log($"[RoomInvitePopup] Outbox listen start. uid={myUid}");
    }

    /// <summary>
    /// Outbox Firebase 리스너 제거.
    /// 팝업을 닫거나 오브젝트가 꺼질 때 반드시 호출한다.
    /// </summary>
    private void StopOutboxListen()
    {
        if (!_listeningOutbox) return;

        if (_outboxRef != null)
            _outboxRef.ChildChanged -= OnOutboxChildChanged;

        _outboxRef = null;
        _listeningOutbox = false;
    }

    /// <summary>
    /// 내가 보낸 초대의 상태가 변경되었을 때 호출된다.
    /// 
    /// status:
    /// - declined : 상대가 거절함
    /// - accepted : 상대가 수락함
    /// - expired  : 초대가 만료됨
    /// </summary>
    private void OnOutboxChildChanged(object sender, ChildChangedEventArgs e)
    {
        // Firebase 오류 처리
        if (e.DatabaseError != null)
        {
            Debug.LogWarning($"[RoomInvitePopup] Outbox error: {e.DatabaseError.Message}");
            return;
        }

        if (e.Snapshot == null || !e.Snapshot.Exists)
            return;

        string status = e.Snapshot.Child("status").Value?.ToString() ?? "";
        string toUid = e.Snapshot.Child("toUid").Value?.ToString() ?? "";
        string toNick = e.Snapshot.Child("toNick").Value?.ToString() ?? "";

        // toNick이 비어 있으면 로컬 캐시에서 찾아봄
        if (string.IsNullOrWhiteSpace(toNick) &&
            !string.IsNullOrWhiteSpace(toUid) &&
            _nickByFriendUid.TryGetValue(toUid, out var cachedNick))
        {
            toNick = cachedNick;
        }

        if (string.IsNullOrWhiteSpace(toNick))
            toNick = "Friend";

        if (status == "declined")
        {
            // 상대가 거절했으므로 다시 초대 가능하도록 버튼 복구
            RestoreInviteButton(toUid);

            ToastMessageManager.instance?.ShowToast(
                $"{toNick}님이 방 초대를 거절했습니다.",
                $"{toNick} declined your room invitation."
            );
        }
        else if (status == "accepted")
        {
            ToastMessageManager.instance?.ShowToast(
                $"{toNick}님이 방 초대를 수락했습니다.",
                $"{toNick} accepted your room invitation."
            );
        }
        else if (status == "expired")
        {
            // 초대가 만료되었으므로 다시 초대 가능하도록 버튼 복구
            RestoreInviteButton(toUid);

            ToastMessageManager.instance?.ShowToast(
                $"{toNick}님에게 보낸 초대가 만료되었습니다.",
                $"Invitation to {toNick} expired."
            );
        }
    }

    /// <summary>
    /// 특정 친구의 초대 버튼을 다시 활성화한다.
    /// 
    /// 사용 상황:
    /// - 상대가 초대를 거절했을 때
    /// - 초대가 만료되었을 때
    /// </summary>
    private void RestoreInviteButton(string friendUid)
    {
        if (string.IsNullOrWhiteSpace(friendUid)) return;

        if (_itemByFriendUid.TryGetValue(friendUid, out var item) &&
            item != null)
        {
            item.SetInviteAvailable();
        }
    }

    /// <summary>
    /// 친구 목록이 있을 때 UI 상태.
    /// - 목록 영역 켬
    /// - Empty 영역 끔
    /// </summary>
    private void SetListState()
    {
        SetListAndEmpty(true, false);

        if (txtEmpty != null)
            txtEmpty.text = "";
    }

    /// <summary>
    /// 친구 목록이 없거나 로딩/에러일 때 UI 상태.
    /// - 목록 영역 끔
    /// - Empty 영역 켬
    /// </summary>
    private void SetEmptyState(string kor, string eng)
    {
        SetListAndEmpty(false, true);

        if (txtEmpty == null) return;

        var lang = LaguageManager.Instance != null
            ? LaguageManager.Instance.currentLang
            : Lauaguage.Kor;

        txtEmpty.text = lang == Lauaguage.Kor ? kor : eng;
    }

    /// <summary>
    /// 목록 영역과 Empty 영역을 동시에 제어한다.
    /// 
    /// showList:
    /// - true면 친구 목록 영역 표시
    /// 
    /// showEmpty:
    /// - true면 Empty 안내 영역 표시
    /// </summary>
    private void SetListAndEmpty(bool showList, bool showEmpty)
    {
        if (listParent != null)
            listParent.SetActive(showList);

        if (emptyParent != null)
            emptyParent.SetActive(showEmpty);
    }

    /// <summary>
    /// GPM InfiniteScroll 데이터를 모두 제거하고 화면을 갱신한다.
    /// </summary>
    private void ClearScroll()
    {
        if (friendScroll == null) return;

        RoomInfiniteScrollUtil.ClearAll(friendScroll);
        RoomInfiniteScrollUtil.UpdateAll(friendScroll);
    }
}