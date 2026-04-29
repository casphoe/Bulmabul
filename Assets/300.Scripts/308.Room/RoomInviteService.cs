using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Auth;
using Firebase.Database;
using UnityEngine;

/// <summary>
/// 방 초대 데이터 모델.
/// Firebase의 roomInvites, roomInviteOutbox 양쪽에서 같은 구조로 사용한다.
/// </summary>
public class RoomInviteData
{
    public string inviteId; // 초대 고유 ID

    public string fromUid;  // 초대한 사람 UID
    public string fromNick; // 초대한 사람 닉네임

    public string toUid;    // 초대받은 사람 UID
    public string toNick;   // 초대받은 사람 닉네임

    public string roomName; // 입장할 Fusion 방 이름
    public int roomMode;    // MatchMode enum을 int로 저장
    public int map;         // 선택된 맵 번호
    public int maxPlayers;  // 방 최대 인원

    public long createdAt;  // Firebase ServerValue.Timestamp
    public long expireAt;   // 초대 만료 시간

    public string status;   // pending / accepted / declined / expired
}

/// <summary>
/// 방 초대 관련 Firebase 입출력 담당 클래스.
/// 
/// 역할:
/// 1. 방 초대 전송
/// 2. 내가 받은 pending 초대 조회
/// 3. 초대 상태 변경
/// 4. roomInvites와 roomInviteOutbox 동시 갱신
/// </summary>
public static class RoomInviteService
{
    private static FirebaseAuth Auth => FirebaseAuth.DefaultInstance;
    private static DatabaseReference Root => FirebaseDatabase.DefaultInstance.RootReference;

    /// <summary>
    /// 현재 로그인한 Firebase 유저 UID.
    /// 로그인되어 있지 않으면 예외 발생.
    /// </summary>
    private static string MyUid
    {
        get
        {
            var user = Auth.CurrentUser;
            if (user == null) throw new Exception("로그인이 필요합니다.");
            return user.UserId;
        }
    }

    /// <summary>
    /// 방 초대 전송.
    /// 
    /// 저장 경로:
    /// 1. roomInvites/{toUid}/{inviteId}
    ///    - 초대받은 사람이 읽는 경로
    /// 
    /// 2. roomInviteOutbox/{fromUid}/{inviteId}
    ///    - 초대한 사람이 수락/거절/만료 상태를 확인하는 경로
    /// 
    /// 반환:
    /// - 생성된 inviteId
    /// </summary>
    public static async Task<string> SendRoomInviteAsync(
        string toUid,
        string toNick,
        string roomName,
        MatchMode mode,
        int map,
        int maxPlayers)
    {
        // 초대 대상 UID 검사
        if (string.IsNullOrWhiteSpace(toUid))
            throw new Exception("초대할 친구 UID가 없습니다.");

        // 방 이름 검사
        if (string.IsNullOrWhiteSpace(roomName))
            throw new Exception("방 이름이 없습니다.");

        string myUid = MyUid;
        string myNick = "Player";

        // 현재 로그인 계정의 닉네임 가져오기
        if (FireBaseAuthManager.Instance != null &&
            FireBaseAuthManager.Instance.CurrentAccount != null &&
            !string.IsNullOrWhiteSpace(FireBaseAuthManager.Instance.CurrentAccount.NickName))
        {
            myNick = FireBaseAuthManager.Instance.CurrentAccount.NickName;
        }

        // 초대받는 친구 닉네임이 없으면 기본값 사용
        if (string.IsNullOrWhiteSpace(toNick))
            toNick = "Friend";

        // 초대 만료 시간: 현재 시간 기준 5분 후
        long expireAt = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds();

        // 초대 ID 생성
        string inviteId = Root
            .Child("roomInvites")
            .Child(toUid)
            .Push()
            .Key;

        if (string.IsNullOrWhiteSpace(inviteId))
            throw new Exception("초대 ID 생성 실패");

        // Firebase에 저장할 초대 데이터
        var data = new Dictionary<string, object>
        {
            ["inviteId"] = inviteId,

            ["fromUid"] = myUid,
            ["fromNick"] = myNick,

            ["toUid"] = toUid,
            ["toNick"] = toNick,

            ["roomName"] = roomName,
            ["roomMode"] = (int)mode,
            ["map"] = map,
            ["maxPlayers"] = Mathf.Clamp(maxPlayers, 1, 4),

            ["createdAt"] = ServerValue.Timestamp,
            ["expireAt"] = expireAt,

            ["status"] = "pending"
        };

        // 멀티 경로 업데이트.
        // 두 경로가 동시에 저장되어야 방장도 결과를 확인할 수 있다.
        var updates = new Dictionary<string, object>
        {
            [$"roomInvites/{toUid}/{inviteId}"] = data,
            [$"roomInviteOutbox/{myUid}/{inviteId}"] = data
        };

        await Root.UpdateChildrenAsync(updates);

        return inviteId;
    }

    /// <summary>
    /// 기존 코드 호환용 오버로드.
    /// toNick 없이 호출할 경우 toNick은 Friend로 저장된다.
    /// 
    /// 새 코드에서는 가능하면 toNick까지 넘기는 버전을 사용하는 것이 좋다.
    /// </summary>
    public static Task<string> SendRoomInviteAsync(
        string toUid,
        string roomName,
        MatchMode mode,
        int map,
        int maxPlayers)
    {
        return SendRoomInviteAsync(
            toUid,
            "Friend",
            roomName,
            mode,
            map,
            maxPlayers
        );
    }

    /// <summary>
    /// 내가 받은 pending 상태의 초대 목록을 가져온다.
    /// 
    /// 사용 예:
    /// - 폴링 방식으로 초대 확인할 때
    /// - 실시간 리스너 대신 수동 새로고침할 때
    /// </summary>
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

            // pending 상태만 반환
            if (status != "pending") continue;

            long expireAt = TryLong(c.Child("expireAt").Value);

            // 만료된 초대는 expired로 변경하고 목록에 넣지 않음
            if (expireAt > 0 && expireAt < now)
            {
                var expiredInvite = SnapshotToInvite(c, myUid);
                await SetInviteStatusAsync(expiredInvite, "expired");
                continue;
            }

            var data = SnapshotToInvite(c, myUid);

            if (!string.IsNullOrWhiteSpace(data.roomName))
                list.Add(data);
        }

        // 최신 초대가 먼저 오도록 정렬
        list.Sort((a, b) => b.createdAt.CompareTo(a.createdAt));
        return list;
    }

    /// <summary>
    /// 초대받은 사람이 수락/거절/만료 상태를 처리한다.
    /// 
    /// 중요:
    /// 이 함수는 roomInvites와 roomInviteOutbox의 status를 동시에 갱신한다.
    /// 그래서 방장이 거절/수락 여부를 실시간으로 알 수 있다.
    /// </summary>
    public static async Task SetInviteStatusAsync(RoomInviteData invite, string status)
    {
        if (invite == null) return;

        if (string.IsNullOrWhiteSpace(invite.inviteId))
            return;

        // 허용된 상태값만 사용
        if (status != "pending" &&
            status != "accepted" &&
            status != "declined" &&
            status != "expired")
        {
            throw new Exception("잘못된 초대 상태입니다.");
        }

        string myUid = MyUid;

        // 내가 받은 초대 경로 상태 변경
        var updates = new Dictionary<string, object>
        {
            [$"roomInvites/{myUid}/{invite.inviteId}/status"] = status
        };

        // 초대한 사람의 Outbox 경로도 함께 변경
        if (!string.IsNullOrWhiteSpace(invite.fromUid))
        {
            updates[$"roomInviteOutbox/{invite.fromUid}/{invite.inviteId}/status"] = status;
        }

        await Root.UpdateChildrenAsync(updates);
    }

    /// <summary>
    /// 이전 코드 호환용 함수.
    /// 
    /// 주의:
    /// 이 함수는 roomInvites 쪽 status만 변경한다.
    /// roomInviteOutbox는 변경하지 않으므로,
    /// 거절/수락 알림이 방장에게 전달되어야 하는 상황에서는
    /// 반드시 SetInviteStatusAsync(RoomInviteData, string)을 사용해야 한다.
    /// </summary>
    public static async Task SetInviteStatusAsync(string inviteId, string status)
    {
        if (string.IsNullOrWhiteSpace(inviteId)) return;

        // 허용된 상태값만 사용
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

    /// <summary>
    /// Firebase DataSnapshot을 RoomInviteData로 변환한다.
    /// </summary>
    private static RoomInviteData SnapshotToInvite(DataSnapshot c, string myUid)
    {
        return new RoomInviteData
        {
            inviteId = c.Child("inviteId").Value?.ToString() ?? c.Key,

            fromUid = c.Child("fromUid").Value?.ToString() ?? "",
            fromNick = c.Child("fromNick").Value?.ToString() ?? "",

            toUid = c.Child("toUid").Value?.ToString() ?? myUid,
            toNick = c.Child("toNick").Value?.ToString() ?? "",

            roomName = c.Child("roomName").Value?.ToString() ?? "",
            roomMode = TryInt(c.Child("roomMode").Value, 0),
            map = TryInt(c.Child("map").Value, 0),
            maxPlayers = TryInt(c.Child("maxPlayers").Value, 4),

            createdAt = TryLong(c.Child("createdAt").Value),
            expireAt = TryLong(c.Child("expireAt").Value),

            status = c.Child("status").Value?.ToString() ?? ""
        };
    }

    /// <summary>
    /// object 값을 int로 안전하게 변환한다.
    /// Firebase 값이 null이거나 숫자 변환에 실패하면 기본값을 반환한다.
    /// </summary>
    private static int TryInt(object v, int def = 0)
    {
        if (v == null) return def;
        if (int.TryParse(v.ToString(), out int n)) return n;
        return def;
    }

    /// <summary>
    /// object 값을 long으로 안전하게 변환한다.
    /// Firebase Timestamp나 expireAt 값을 읽을 때 사용한다.
    /// </summary>
    private static long TryLong(object v)
    {
        if (v == null) return 0;
        if (long.TryParse(v.ToString(), out long n)) return n;
        return 0;
    }
}