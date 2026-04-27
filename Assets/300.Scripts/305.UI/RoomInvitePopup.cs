using System;
using System.Threading.Tasks;
using Gpm.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 방 안에서 빈 슬롯 + 버튼을 눌렀을 때 열리는 친구 초대 팝업
/// - 온라인 상태인 친구만 보여줌
/// - 목록은 GPM InfiniteScroll로 표시
/// </summary>
public class RoomInvitePopup : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject root;
    [SerializeField] private Button btnClose;

    [Header("Panels")]
    [SerializeField] private GameObject listParent;   // 친구 목록 영역, 예: Root의 0번째 자식
    [SerializeField] private GameObject emptyParent;  // Empty 안내 영역, 예: Root의 1번째 자식

    [Header("Text")]
    [SerializeField] private TMP_Text txtTitle;
    [SerializeField] private TMP_Text txtEmpty;

    [Header("GPM InfiniteScroll")]
    [SerializeField] private InfiniteScroll friendScroll;

    private string _roomName;
    private MatchMode _mode;
    private int _map;
    private int _maxPlayers;

    private bool _loading;

    private void Awake()
    {
        if (root != null)
            root.SetActive(false);

        if (btnClose != null)
        {
            btnClose.onClick.RemoveAllListeners();
            btnClose.onClick.AddListener(Close);
        }

        // 처음에는 둘 다 꺼두는 것이 안전함
        SetListAndEmpty(false, false);
    }

    /// <summary>
    /// 친구 초대 팝업 열기
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

        await LoadOnlineFriendsAsync();
    }

    /// <summary>
    /// 친구 초대 팝업 닫기
    /// </summary>
    public void Close()
    {
        ClearScroll();

        SetListAndEmpty(false, false);

        if (root != null)
            root.SetActive(false);
    }

    /// <summary>
    /// 제목 언어 적용
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
    /// 온라인 친구만 불러와서 InfiniteScroll에 넣음
    /// </summary>
    private async Task LoadOnlineFriendsAsync()
    {
        if (_loading) return;
        _loading = true;

        ClearScroll();

        // 불러오는 중에는 Empty 영역을 켜고 안내 문구 표시
        SetEmptyState("불러오는 중...", "Loading...");

        try
        {
            // 기존 친구 시스템에서 내 친구 목록 가져오기
            var friends = await FriendService.GetMyFriendsAsync();

            // 온라인 친구만 필터링
            friends = friends.FindAll(f => f != null && f.isOnline);

            // 온라인 친구가 없으면
            // 0번째 목록 부모는 끄고, 1번째 Empty 부모는 켬
            if (friends.Count == 0)
            {
                SetEmptyState(
                    "온라인 상태인 친구가 없습니다.",
                    "No online friends."
                );
                return;
            }

            // 온라인 친구가 있으면
            // 0번째 목록 부모는 켜고, 1번째 Empty 부모는 끔
            SetListState();

            // GPM InfiniteScroll에 데이터 삽입
            for (int i = 0; i < friends.Count; i++)
            {
                var data = new RoomInviteFriendData
                {
                    friend = friends[i],
                    onClickInvite = OnClickInvite
                };

                RoomInfiniteScrollUtil.Insert(friendScroll, data, i);
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
    /// 초대 버튼 클릭
    /// </summary>
    private async void OnClickInvite(FriendRow friend, RoomInviteFriendItem item)
    {
        if (friend == null) return;

        try
        {
            await RoomInviteService.SendRoomInviteAsync(
                friend.uid,
                _roomName,
                _mode,
                _map,
                _maxPlayers
            );

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
    /// 친구 목록이 있을 때 UI 상태
    /// - 0번째 부모, 즉 목록 영역 켜기
    /// - 1번째 부모, 즉 Empty 영역 끄기
    /// </summary>
    private void SetListState()
    {
        SetListAndEmpty(true, false);

        if (txtEmpty != null)
            txtEmpty.text = "";
    }

    /// <summary>
    /// 친구 목록이 없거나 로딩/에러일 때 UI 상태
    /// - 0번째 부모, 즉 목록 영역 끄기
    /// - 1번째 부모, 즉 Empty 영역 켜기
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
    /// 목록 부모와 Empty 부모 ON/OFF
    /// </summary>
    private void SetListAndEmpty(bool showList, bool showEmpty)
    {
        if (listParent != null)
            listParent.SetActive(showList);

        if (emptyParent != null)
            emptyParent.SetActive(showEmpty);
    }

    /// <summary>
    /// InfiniteScroll 데이터 제거
    /// </summary>
    private void ClearScroll()
    {
        if (friendScroll == null) return;

        RoomInfiniteScrollUtil.ClearAll(friendScroll);
        RoomInfiniteScrollUtil.UpdateAll(friendScroll);
    }
}