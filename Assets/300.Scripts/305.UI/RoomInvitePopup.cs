using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RoomInvitePopup : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject root;
    [SerializeField] private Button btnClose;

    [Header("Text")]
    [SerializeField] private TMP_Text txtTitle;
    [SerializeField] private TMP_Text txtEmpty;

    [Header("List")]
    [SerializeField] private Transform contentRoot;
    [SerializeField] private RoomInviteFriendItem itemPrefab;

    private readonly List<GameObject> _spawned = new();

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
    }

    public async void Open(string roomName, MatchMode mode, int map, int maxPlayers)
    {
        _roomName = roomName;
        _mode = mode;
        _map = map;
        _maxPlayers = maxPlayers;

        if (root != null)
            root.SetActive(true);

        if (txtTitle != null)
        {
            var lang = LaguageManager.Instance != null
                ? LaguageManager.Instance.currentLang
                : Lauaguage.Kor;

            txtTitle.text = lang == Lauaguage.Kor
                ? "친구 초대"
                : "Invite Friends";
        }

        await LoadOnlineFriendsAsync();
    }

    public void Close()
    {
        if (root != null)
            root.SetActive(false);

        ClearItems();
    }

    private async Task LoadOnlineFriendsAsync()
    {
        if (_loading) return;
        _loading = true;

        ClearItems();

        if (txtEmpty != null)
        {
            txtEmpty.gameObject.SetActive(true);
            txtEmpty.text = "불러오는 중...";
        }

        try
        {
            var friends = await FriendService.GetMyFriendsAsync();

            // 온라인 친구만 표시
            friends = friends.FindAll(f => f != null && f.isOnline);

            if (friends.Count == 0)
            {
                if (txtEmpty != null)
                {
                    txtEmpty.gameObject.SetActive(true);
                    txtEmpty.text = "온라인 상태인 친구가 없습니다.";
                }

                return;
            }

            if (txtEmpty != null)
                txtEmpty.gameObject.SetActive(false);

            foreach (var friend in friends)
            {
                if (itemPrefab == null || contentRoot == null) continue;

                var item = Instantiate(itemPrefab, contentRoot);
                _spawned.Add(item.gameObject);

                item.SetData(friend, OnClickInvite);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[RoomInvitePopup] Load failed: {e}");

            if (txtEmpty != null)
            {
                txtEmpty.gameObject.SetActive(true);
                txtEmpty.text = "친구 목록을 불러오지 못했습니다.";
            }
        }
        finally
        {
            _loading = false;
        }
    }

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

    private void ClearItems()
    {
        for (int i = 0; i < _spawned.Count; i++)
        {
            if (_spawned[i] != null)
                Destroy(_spawned[i]);
        }

        _spawned.Clear();
    }
}