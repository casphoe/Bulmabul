using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Firebase.Auth;
using Firebase.Database;
using UnityEngine;

/// <summary>
/// FriendService
/// 
/// 1) 검색창이 비어있으면: nicknames 전체/일부를 가져와 "초대 리스트"로 보여준다.
/// 2) 검색창에 입력이 있으면: 해당 prefix만 필터링해서 보여준다.
/// 3) "내 친구" 버튼을 누르면: friends/{myUid} 목록만 보여준다.
/// 4) 친구 삭제는 한쪽에서 누르면 양쪽 friends를 동시에 삭제한다(Cloud Functions 없이 클라에서 Multi-location update).
///
/// </summary>
/// 
public static class FriendService
{
    // ====== Firebase helpers ======
    static FirebaseAuth Auth => FirebaseAuth.DefaultInstance;
    static DatabaseReference Root => FirebaseDatabase.DefaultInstance.RootReference;


    static string MyUid
    {
        get
        {
            var u = Auth.CurrentUser;
            if (u == null) throw new Exception("Not logged in.");
            return u.UserId;
        }
    }

    /// <summary>
    /// 닉네임을 rules에서 사용하는 key 형태로 정규화(너의 NicknameService와 맞춰야 함)
    /// - 예: lower + trim
    /// - 너 프로젝트에서 이미 ToNickKey가 있으면 그걸 호출하도록 바꿔라.
    /// </summary>
    public static string ToNickKey(string nick)
        => (nick ?? "").Trim().ToLowerInvariant();

    #region 친구 초대

    /// <summary>
    /// 검색창이 비어있을 때: nicknames 일부를 가져온다.
    /// 
    /// limitCount:
    /// - 전체 유저가 많을 수 있으니 UI 성능상 50~200 정도로 제한 추천
    /// - InfiniteScroll이라도 "DB에서 한번에 너무 많이" 가져오면 느려짐
    /// </summary>
    public static async Task<List<(string uid, string nickKey)>> GetInviteCandidatesDefaultAsync(int limitCount = 100)
    {
        // nicknames/{nickKey} = uid
        var q = FirebaseDatabase.DefaultInstance
            .GetReference("nicknames")
            .OrderByKey()
            .LimitToFirst(limitCount);

        var snap = await q.GetValueAsync();
        return ParseNickIndexSnapshot(snap);
    }

    /// <summary>
    /// 검색창에 입력이 있을 때: nickKey prefix로 검색한다.
    /// - RTDB는 "contains" 검색이 안 되고, prefix(시작 문자열) 검색이 일반적.
    /// - prefix 검색 트릭: startAt(prefix) ~ endAt(prefix + "\uf8ff")
    /// </summary>
    public static async Task<List<(string uid, string nickKey)>> SearchInviteCandidatesByPrefixAsync(string input, int limitCount = 50)
    {
        string prefix = ToNickKey(input);
        if (string.IsNullOrEmpty(prefix))
            return await GetInviteCandidatesDefaultAsync(limitCount);

        var q = FirebaseDatabase.DefaultInstance
            .GetReference("nicknames")
            .OrderByKey()
            .StartAt(prefix)
            .EndAt(prefix + "\uf8ff")
            .LimitToFirst(limitCount);

        var snap = await q.GetValueAsync();
        return ParseNickIndexSnapshot(snap);
    }

    static List<(string uid, string nickKey)> ParseNickIndexSnapshot(DataSnapshot snap)
    {
        var list = new List<(string uid, string nickKey)>();
        if (snap == null || !snap.Exists) return list;

        foreach (var child in snap.Children)
        {
            string nickKey = child.Key;
            string uid = child.Value?.ToString() ?? "";
            if (string.IsNullOrEmpty(uid)) continue;

            // 내 계정은 초대 리스트에서 제외하고 싶으면 여기서 제외
            if (uid == MyUid) continue;

            list.Add((uid, nickKey));
        }

        // 정렬(원하면)
        list.Sort((a, b) => string.CompareOrdinal(a.nickKey, b.nickKey));
        return list;
    }
    #endregion

    #region 내 친구 목록

    /// <summary>
    /// 내 친구 목록을 읽는다: friends/{myUid}
    /// </summary>
    public static async Task<List<FriendRow>> GetMyFriendsAsync()
    {
        string myUid = MyUid;
        // 1) friends/{myUid} 읽기
        var snap = await FirebaseDatabase.DefaultInstance
            .GetReference($"friends/{myUid}")
            .GetValueAsync();

        var list = new List<FriendRow>();
        if (snap == null || !snap.Exists) return list;

        foreach (var c in snap.Children)
        {
            // key = friendUid
            string friendUid = c.Key;

            var row = new FriendRow
            {
                uid = friendUid,
                nick = c.Child("nick").Value?.ToString() ?? "",
                photoUrl = c.Child("photoUrl").Value?.ToString() ?? "",
                createdAt = TryLong(c.Child("createdAt").Value),
                isOnline = false,
                lastSeenUnix = 0
            };
            list.Add(row);
        }

        // 2) presence를 가져와서 채우기 (친구 수가 많으면 요청이 많아짐)
        //    - 일단 가장 단순/확실한 방식: uid별로 읽기
        var tasks = new List<Task>();

        foreach (var f in list)
        {
            tasks.Add(FillPresenceAsync(f));
        }

        await Task.WhenAll(tasks);

        // 3) 정렬:
        //    - online 먼저
        //    - 같은 그룹 내에서는 lastSeenUnix 내림차순(최신 먼저)
        list.Sort((a, b) =>
        {
            // online 우선
            int onlineCmp = b.isOnline.CompareTo(a.isOnline); // true가 먼저
            if (onlineCmp != 0) return onlineCmp;

            // 접속 시간 최신순
            int seenCmp = b.lastSeenUnix.CompareTo(a.lastSeenUnix);
            if (seenCmp != 0) return seenCmp;

            // 마지막 보조키: 닉네임(안정 정렬)
            return string.CompareOrdinal(a.nick, b.nick);
        });

        return list;
    }

    static async Task FillPresenceAsync(FriendRow row)
    {
        try
        {
            var pSnap = await FirebaseDatabase.DefaultInstance
                .GetReference($"presence/{row.uid}")
                .GetValueAsync();

            if (pSnap != null && pSnap.Exists)
            {
                row.isOnline = TryBool(pSnap.Child("online").Value);
                row.lastSeenUnix = TryLong(pSnap.Child("lastSeen").Value);
            }
        }
        catch
        {
            // presence 읽기 실패하면 그냥 오프라인/0 유지
            row.isOnline = false;
            row.lastSeenUnix = 0;
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

    #region 초대 후보 리스트 Row로 완성해서 반환 (친구/초대중/요청옴/나 자신 제외)

    public static async Task<List<InviteCandidateRow>> GetInviteCandidateRowsAsync(
    string inputOrNull,
    int limitCount = 80
)
    {
        // 1) nicknames에서 후보 uid 목록 가져오기(기존 로직 재사용)
        List<(string uid, string nickKey)> candidates =
            string.IsNullOrWhiteSpace(inputOrNull)
                ? await GetInviteCandidatesDefaultAsync(limitCount)
                : await SearchInviteCandidatesByPrefixAsync(inputOrNull, limitCount);

        string myUid = MyUid;

        // 2) 상태 판단용 세트 읽기
        var friendsTask = GetChildKeySetAsync($"friends/{myUid}");
        var outTask = GetChildKeySetAsync($"friendRequestsOut/{myUid}");
        var inTask = GetChildKeySetAsync($"friendRequestsIn/{myUid}");
        await Task.WhenAll(friendsTask, outTask, inTask);

        var friends = friendsTask.Result;   // 이미 친구 uid 집합
        var outReq = outTask.Result;        // 내가 초대한 uid 집합
        var inReq = inTask.Result;          // 나에게 요청 온 uid 집합

        // 3) 필터링 + 프로필(userPublic) 채우기
        var rows = new List<InviteCandidateRow>();
        var tasks = new List<Task>();

        foreach (var (uid, nickKey) in candidates)
        {
            if (string.IsNullOrEmpty(uid)) continue;

            //나 자신 제외
            if (uid == myUid) continue;

            var row = new InviteCandidateRow
            {
                uid = uid,
                nickKey = nickKey,
                nick = "",
                photoUrl = ""
            };

            if (friends.Contains(uid))
                row.state = InviteCandidateRow.InviteState.AlreadyFriend;
            else if (outReq.Contains(uid))
                row.state = InviteCandidateRow.InviteState.Inviting;
            else if (inReq.Contains(uid))
                row.state = InviteCandidateRow.InviteState.RequestedMe;
            else
                row.state = InviteCandidateRow.InviteState.CanInvite;

            rows.Add(row);

            // 공개 프로필(userPublic) 채우기
            tasks.Add(FillInviteRowPublicProfileAsync(row));
        }

        await Task.WhenAll(tasks);

        // 표시 정렬(원하면 nickKey 기준)
        rows.Sort((a, b) => string.CompareOrdinal(a.nickKey, b.nickKey));
        return rows;
    }

    private static async Task FillInviteRowPublicProfileAsync(InviteCandidateRow row)
    {
        var (nick, photoUrl) = await GetUserProfileBasicAsync(row.uid);
        row.nick = string.IsNullOrWhiteSpace(nick) ? row.nickKey : nick;
        row.photoUrl = photoUrl ?? "";
    }

    private static async Task<HashSet<string>> GetChildKeySetAsync(string path)
    {
        var set = new HashSet<string>();
        try
        {
            var snap = await FirebaseDatabase.DefaultInstance.GetReference(path).GetValueAsync();
            if (snap == null || !snap.Exists) return set;

            foreach (var c in snap.Children)
            {
                if (!string.IsNullOrEmpty(c.Key))
                    set.Add(c.Key);
            }
        }
        catch { }
        return set;
    }

    #endregion

    #region 친구 요청 보내기(수락 / 거절)

    /// <summary>
    /// 친구 요청 보내기:
    /// - friendRequestsIn/{targetUid}/{myUid} 생성
    /// - friendRequestsOut/{myUid}/{targetUid} 생성
    /// 
    /// UI에서 "초대" 버튼 누르면 호출
    /// </summary>
    public static async Task SendFriendRequestAsync(string targetUid, string targetNick)
    {
        string myUid = MyUid;

        if (string.IsNullOrEmpty(targetUid) || targetUid == myUid) return;

        // 이미 친구 / 이미 초대중 / 요청옴이면 무시
        var friendsTask = GetChildKeySetAsync($"friends/{myUid}");
        var outTask = GetChildKeySetAsync($"friendRequestsOut/{myUid}");
        var inTask = GetChildKeySetAsync($"friendRequestsIn/{myUid}");
        await Task.WhenAll(friendsTask, outTask, inTask);

        if (friendsTask.Result.Contains(targetUid)) return;
        if (outTask.Result.Contains(targetUid)) return;
        if (inTask.Result.Contains(targetUid)) return;

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // 내 닉네임(보내는 사람 닉)도 같이 적어주면 받은쪽 UI 만들기 쉬움
        // (너 프로젝트의 CurrentAccount.NickName을 쓰면 더 정확)
        string myNick = "";
        try
        {
            // users/{myUid}/nick 읽기(룰 느슨하게 했다는 전제)
            var meSnap = await FirebaseDatabase.DefaultInstance.GetReference($"users/{myUid}/nick").GetValueAsync();
            myNick = meSnap?.Value?.ToString() ?? "";
        }
        catch { }

        var updates = new Dictionary<string, object>();

        updates[$"friendRequestsIn/{targetUid}/{myUid}"] = new Dictionary<string, object>
        {
            ["fromUid"] = myUid,
            ["fromNick"] = myNick,
            ["createdAt"] = now
        };

        updates[$"friendRequestsOut/{myUid}/{targetUid}"] = new Dictionary<string, object>
        {
            ["toUid"] = targetUid,
            ["toNick"] = targetNick ?? "",
            ["createdAt"] = now
        };

        await Root.UpdateChildrenAsync(updates);
    }

    /// <summary>
    /// 친구 요청 수락:
    /// - friends 양쪽에 동시에 생성 (핵심)
    /// - In/Out 요청 노드 삭제
    /// </summary>
    public static async Task AcceptFriendRequestAsync(string fromUid)
    {
        string myUid = MyUid;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (string.IsNullOrWhiteSpace(fromUid) || fromUid == myUid)
            return;

        // 0) 실제로 "내게 온 요청"이 있는지 확인 (없으면 중복 수락/오류 방지)
        var reqSnap = await FirebaseDatabase.DefaultInstance
            .GetReference($"friendRequestsIn/{myUid}/{fromUid}")
            .GetValueAsync();

        if (reqSnap == null || !reqSnap.Exists)
        {
            Debug.LogWarning($"[AcceptFriendRequest] request not found. myUid={myUid} fromUid={fromUid}");
            return;
        }

        // 1) userPublic에서 내/상대 프로필 읽기 (rules에 맞게)
        var meTask = FirebaseDatabase.DefaultInstance
            .GetReference($"userPublic/{myUid}")
            .GetValueAsync();

        var frTask = FirebaseDatabase.DefaultInstance
            .GetReference($"userPublic/{fromUid}")
            .GetValueAsync();
        await Task.WhenAll(meTask, frTask);

        var meSnap = meTask.Result;
        var frSnap = frTask.Result;

        string myNick = meSnap.Child("nick").Value?.ToString() ?? "";
        string myPhoto = meSnap.Child("photoUrl").Value?.ToString() ?? "";

        string frNick = frSnap.Child("nick").Value?.ToString() ?? "";
        string frPhoto = frSnap.Child("photoUrl").Value?.ToString() ?? "";

        // 2) 멀티 업데이트 구성
        var updates = new Dictionary<string, object>();

        // friends 양쪽 생성
        updates[$"friends/{myUid}/{fromUid}"] = new Dictionary<string, object>
        {
            ["uid"] = fromUid,
            ["nick"] = frNick,
            ["photoUrl"] = frPhoto,
            ["createdAt"] = now
        };

        updates[$"friends/{fromUid}/{myUid}"] = new Dictionary<string, object>
        {
            ["uid"] = myUid,
            ["nick"] = myNick,
            ["photoUrl"] = myPhoto,
            ["createdAt"] = now
        };

        // 요청 제거
        updates[$"friendRequestsIn/{myUid}/{fromUid}"] = null;
        updates[$"friendRequestsOut/{fromUid}/{myUid}"] = null;

        // 3) 상대에게 "수락됨" 알림 생성 (pushId 자동 생성)
        // notifications/{toUid}/{pushId}
        var pushId = FirebaseDatabase.DefaultInstance
            .GetReference($"notifications/{fromUid}")
            .Push().Key;

        updates[$"notifications/{fromUid}/{pushId}"] = new Dictionary<string, object>
        {
            ["type"] = "friend_accepted",
            ["byUid"] = myUid,
            ["byNick"] = myNick,
            ["createdAt"] = now
        };

        await Root.UpdateChildrenAsync(updates);
    }
    #region 친구 거절

    public static async Task DeclineFriendRequestAsync(string fromUid)
    {
        string myUid = MyUid;

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // 내 닉네임(거절한 사람 닉)
        string myNick = "";
        try
        {
            var meSnap = await FirebaseDatabase.DefaultInstance
     .GetReference($"users/{myUid}")
     .GetValueAsync();
            myNick = meSnap.Child("nick").Value?.ToString() ?? "";
        }
        catch { }


        var updates = new Dictionary<string, object>();
        // 요청 제거
        updates[$"friendRequestsIn/{myUid}/{fromUid}"] = null;
        updates[$"friendRequestsOut/{fromUid}/{myUid}"] = null;

        // 거절 알림 (보낸 사람에게)
        string pushKey = Root.Child($"notifications/{fromUid}").Push().Key;
        updates[$"notifications/{fromUid}/{pushKey}"] = new Dictionary<string, object>
        {
            ["type"] = "friend_declined",
            ["byUid"] = myUid,
            ["byNick"] = myNick,
            ["createdAt"] = now
        };

        await Root.UpdateChildrenAsync(updates);
    }

    public static async Task CancelOutgoingRequestAsync(string targetUid)
    {
        string myUid = MyUid;

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // 내 닉
        var meSnap = await FirebaseDatabase.DefaultInstance
            .GetReference($"users/{myUid}")
            .GetValueAsync();
        string myNick = meSnap.Child("nick").Value?.ToString() ?? "";

        var updates = new Dictionary<string, object>();
        updates[$"friendRequestsOut/{myUid}/{targetUid}"] = null;
        updates[$"friendRequestsIn/{targetUid}/{myUid}"] = null;

        // 상대에게 "취소" 알림
        var pushId = FirebaseDatabase.DefaultInstance.GetReference($"notifications/{targetUid}").Push().Key;
        updates[$"notifications/{targetUid}/{pushId}"] = new Dictionary<string, object>
        {
            ["type"] = "friend_canceled",
            ["byUid"] = myUid,
            ["byNick"] = myNick,
            ["createdAt"] = now
        };

        await Root.UpdateChildrenAsync(updates);
    }

    #endregion

    #endregion

    #region 친구 삭제

    /// <summary>
    /// 친구 삭제:
    /// - 한쪽에서 삭제 버튼을 눌러도
    /// - friends/{me}/{you} 와 friends/{you}/{me} 를 동시에 null 처리한다.
    /// 
    /// Cloud Functions 없이 "양쪽 삭제"를 보장하려면:
    /// - Rules에서 상대 friends 경로 write가 허용되어야 함(보안 희생)
    /// </summary>
    public static async Task RemoveFriendBothAsync(string friendUid)
    {
        string myUid = MyUid;

        var updates = new Dictionary<string, object>();
        updates[$"friends/{myUid}/{friendUid}"] = null;
        updates[$"friends/{friendUid}/{myUid}"] = null;

        await Root.UpdateChildrenAsync(updates);
    }

    #endregion

    #region 프로필 이미지 적용

    public static async Task<(string nick, string photoUrl)> GetUserProfileBasicAsync(string uid)
    {
        try
        {
            var snap = await FirebaseDatabase.DefaultInstance
                .GetReference($"userPublic/{uid}")
                .GetValueAsync();

            if (snap == null || !snap.Exists) return ("", "");

            string nick = snap.Child("nick").Value?.ToString() ?? "";
            string photoUrl = snap.Child("photoUrl").Value?.ToString() ?? "";
            return (nick, photoUrl);
        }
        catch
        {
            return ("", "");
        }
    }

    #endregion
}
