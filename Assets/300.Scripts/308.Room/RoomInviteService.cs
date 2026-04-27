using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Auth;
using Firebase.Database;
using UnityEngine;

public class RoomInviteData
{
    public string inviteId;
    public string fromUid;
    public string fromNick;
    public string roomName;
    public int roomMode;
    public int map;
    public int maxPlayers;
    public long createdAt;
    public long expireAt;
    public string status;
}

public static class RoomInviteService
{
    private static FirebaseAuth Auth => FirebaseAuth.DefaultInstance;
    private static DatabaseReference Root => FirebaseDatabase.DefaultInstance.RootReference;

    private static string MyUid
    {
        get
        {
            var user = Auth.CurrentUser;
            if (user == null) throw new Exception("로그인이 필요합니다.");
            return user.UserId;
        }
    }

    public static async Task SendRoomInviteAsync(
        string toUid,
        string roomName,
        MatchMode mode,
        int map,
        int maxPlayers)
    {
        if (string.IsNullOrWhiteSpace(toUid))
            throw new Exception("초대할 친구 UID가 없습니다.");

        if (string.IsNullOrWhiteSpace(roomName))
            throw new Exception("방 이름이 없습니다.");

        string myUid = MyUid;
        string myNick = "Player";

        if (FireBaseAuthManager.Instance != null &&
            FireBaseAuthManager.Instance.CurrentAccount != null &&
            !string.IsNullOrWhiteSpace(FireBaseAuthManager.Instance.CurrentAccount.NickName))
        {
            myNick = FireBaseAuthManager.Instance.CurrentAccount.NickName;
        }

        long expireAt = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds();

        string inviteId = Root
            .Child("roomInvites")
            .Child(toUid)
            .Push()
            .Key;

        var data = new Dictionary<string, object>
        {
            ["fromUid"] = myUid,
            ["fromNick"] = myNick,
            ["roomName"] = roomName,
            ["roomMode"] = (int)mode,
            ["map"] = map,
            ["maxPlayers"] = Mathf.Clamp(maxPlayers, 1, 4),
            ["createdAt"] = ServerValue.Timestamp,
            ["expireAt"] = expireAt,
            ["status"] = "pending"
        };

        await Root
            .Child("roomInvites")
            .Child(toUid)
            .Child(inviteId)
            .SetValueAsync(data);
    }

    public static async Task<List<RoomInviteData>> GetMyPendingInvitesAsync()
    {
        string myUid = MyUid;

        var snap = await Root
            .Child("roomInvites")
            .Child(myUid)
            .GetValueAsync();

        var list = new List<RoomInviteData>();
        if (snap == null || !snap.Exists) return list;

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        foreach (var c in snap.Children)
        {
            string status = c.Child("status").Value?.ToString() ?? "";
            if (status != "pending") continue;

            long expireAt = TryLong(c.Child("expireAt").Value);
            if (expireAt > 0 && expireAt < now)
            {
                await SetInviteStatusAsync(c.Key, "expired");
                continue;
            }

            var data = new RoomInviteData
            {
                inviteId = c.Key,
                fromUid = c.Child("fromUid").Value?.ToString() ?? "",
                fromNick = c.Child("fromNick").Value?.ToString() ?? "",
                roomName = c.Child("roomName").Value?.ToString() ?? "",
                roomMode = TryInt(c.Child("roomMode").Value, 0),
                map = TryInt(c.Child("map").Value, 0),
                maxPlayers = TryInt(c.Child("maxPlayers").Value, 4),
                createdAt = TryLong(c.Child("createdAt").Value),
                expireAt = expireAt,
                status = status
            };

            if (!string.IsNullOrWhiteSpace(data.roomName))
                list.Add(data);
        }

        list.Sort((a, b) => b.createdAt.CompareTo(a.createdAt));
        return list;
    }

    public static async Task SetInviteStatusAsync(string inviteId, string status)
    {
        if (string.IsNullOrWhiteSpace(inviteId)) return;

        if (status != "pending" &&
            status != "accepted" &&
            status != "declined" &&
            status != "expired")
        {
            throw new Exception("잘못된 초대 상태입니다.");
        }

        string myUid = MyUid;

        await Root
            .Child("roomInvites")
            .Child(myUid)
            .Child(inviteId)
            .Child("status")
            .SetValueAsync(status);
    }

    private static int TryInt(object v, int def = 0)
    {
        if (v == null) return def;
        if (int.TryParse(v.ToString(), out int n)) return n;
        return def;
    }

    private static long TryLong(object v)
    {
        if (v == null) return 0;
        if (long.TryParse(v.ToString(), out long n)) return n;
        return 0;
    }
}