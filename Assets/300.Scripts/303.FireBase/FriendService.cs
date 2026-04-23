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

    /// <summary>
    /// nick 인덱스 스냅샷(DataSnapshot)을 파싱해서
    /// (uid, nickKey) 튜플 리스트로 변환한다.
    ///
    /// 예상 스냅샷 구조 예시:
    /// nickIndex
    ///   ├─ "abc" : "UID_1"
    ///   ├─ "bcd" : "UID_2"
    ///   └─ "zzz" : "UID_3"
    ///
    /// 즉,
    /// - child.Key   = nickKey (정규화된 닉네임 키, 검색/정렬용)
    /// - child.Value = uid     (해당 닉키를 가진 유저 uid)
    ///
    /// 반환:
    /// - 내 uid(myUid)는 제외
    /// - uid가 비어있는 항목은 제외
    /// - (uid, nickKey) 리스트를 nickKey 기준으로 오름차순 정렬하여 반환
    /// </summary>
    static List<(string uid, string nickKey)> ParseNickIndexSnapshot(DataSnapshot snap)
    {
        // 최종 반환할 결과 리스트(튜플 형태: (uid, nickKey))
        var list = new List<(string uid, string nickKey)>();

        // 스냅샷이 null이거나 존재하지 않으면(해당 노드 자체가 없으면)
        // 빈 리스트를 반환해서 호출 측에서 NRE 없이 처리 가능하도록 한다.
        if (snap == null || !snap.Exists) return list;

        // 내 uid를 캐싱해 둔다.
        string myUid = MyUid;

        // - 현재 스냅샷 아래에 있는 모든 자식 노드를 순회
        foreach (var child in snap.Children)
        {
            // 인덱스 구조에서 Key로 저장된 닉네임 키(정렬/검색용 문자열)
            string nickKey = child.Key;
            // DB에 저장된 값이 object 형태로 들어오므로 ToString()으로 문자열 변환
            string uid = child.Value?.ToString() ?? "";
            // uid가 비어있으면 정상 데이터가 아니므로 스킵
            if (string.IsNullOrEmpty(uid)) continue;
            // 나 제외
            if (uid == myUid) continue;
            // 유효한 데이터만 결과 리스트에 추가
            list.Add((uid, nickKey));
        }

        //nickKey 기준으로 오름차순 정렬
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
            if (string.IsNullOrEmpty(friendUid)) continue;

            var row = new FriendRow
            {
                uid = friendUid,
                nick = c.Child("nick").Value?.ToString() ?? "",
                photoUrl = c.Child("photoUrl").Value?.ToString() ?? "",
                createdAt = TryLong(c.Child("createdAt").Value),
                accountLevel = TryInt(c.Child("accountLevel").Value, 1),
                equippedDiceKey = c.Child("equippedDiceKey").Value?.ToString() ?? "",
                isOnline = false,
                lastSeenUnix = 0
            };
            list.Add(row);
        }

        // 2) presence를 가져와서 채우기 (친구 수가 많으면 요청이 많아짐)
        //    - 일단 가장 단순/확실한 방식: uid별로 읽기
        var tasks = new List<Task>(list.Count * 2);

        foreach (var f in list)
        {
            tasks.Add(FillPublicProfileAsync(f));
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

    /// <summary>
    /// 친구 리스트 한 줄(FriendRow)에 "공개 프로필(닉네임/프로필 이미지 URL)"을 채워 넣는다.
    /// - DB의 userPublic(또는 public profile)에서 가져온 값을 우선 적용
    /// - public이 비어있으면 FriendRow에 남아있던 값(친구 테이블에 저장된 값)을 "최후 fallback"으로 유지
    /// - 그래도 닉네임이 비면 UI가 완전히 비어 보이지 않도록 uid 앞 6글자를 임시 표시
    /// </summary>
    static async Task FillPublicProfileAsync(FriendRow row)
    {
        var (nick, photoUrl) = await GetUserProfileBasicAsync(row.uid);

        // userPublic이 비어있으면 friends에 남아있는 값이라도 사용(최후 fallback)
        if (!string.IsNullOrWhiteSpace(nick))
            row.nick = nick;

        if (!string.IsNullOrWhiteSpace(photoUrl))
            row.photoUrl = photoUrl;

        // 그래도 닉이 비면 uid 앞부분이라도 보여주기(완전 빈 UI 방지)
        if (string.IsNullOrWhiteSpace(row.nick))
            row.nick = row.uid.Length >= 6 ? row.uid.Substring(0, 6) : row.uid;
    }

    /// <summary>
    /// FriendRow에 "온라인 상태(presence)"를 채워 넣는다.
    /// - presence/{uid} 경로에서 online(bool), lastSeen(long) 값을 읽는다.
    /// - 권한 문제(PermissionDenied) / 네트워크 오류 / 데이터 없음 등을 모두 안전하게 처리
    /// - 실패 시에는 온라인 false / lastSeen 0으로 초기화하여 UI가 예측 가능하게 유지되도록 한다.
    /// </summary>
    static async Task FillPresenceAsync(FriendRow row)
    {

        var path = $"presence/{row.uid}";
        Debug.Log($"[Presence] START uid={row.uid} path={path}");


        try
        {
            var refp = FirebaseDatabase.DefaultInstance.GetReference(path);
            var t = refp.GetValueAsync();

            await t; // 여기서 PermissionDenied면 아래 로그 못 감

            var pSnap = t.Result;
            Debug.Log($"[Presence] OK uid={row.uid} exists={(pSnap != null && pSnap.Exists)} json={pSnap?.GetRawJsonValue()}");

            if (pSnap != null && pSnap.Exists)
            {
                row.isOnline = TryBool(pSnap.Child("online").Value);
                row.lastSeenUnix = TryLong(pSnap.Child("lastSeen").Value);
            }
            else
            {
                row.isOnline = false;
                row.lastSeenUnix = 0;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Presence] FAIL uid={row.uid} path={path} err={e}");
            row.isOnline = false;
            row.lastSeenUnix = 0;
        }
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

        // 표시 정렬
        rows.Sort((a, b) => string.CompareOrdinal(a.nick ?? "", b.nick ?? ""));
        return rows;
    }

    private static async Task FillInviteRowPublicProfileAsync(InviteCandidateRow row)
    {
        var (nick, photoUrl) = await GetUserProfileBasicAsync(row.uid);
        row.nick = !string.IsNullOrWhiteSpace(nick)
             ? nick
             : (row.uid.Length >= 6 ? row.uid.Substring(0, 6) : row.uid);

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

    static async Task<string> GetMyNickPublicAsync()
    {
        try
        {
            var snap = await FirebaseDatabase.DefaultInstance.GetReference($"userPublic/{MyUid}/nick").GetValueAsync();
            return snap?.Value?.ToString() ?? "";
        }
        catch { return ""; }
    }


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
        string myNick = await GetMyNickPublicAsync();

        int myLevel = Mathf.Max(1, FireBaseAuthManager.Instance.CurrentAccount.AccountLevel);
        string myEquippedDiceKey = FireBaseAuthManager.Instance.CurrentAccount.EquippedDiceKey ?? "";

        // targetNick이 비었으면 userPublic에서 한 번 읽어서 채워도 됨(선택)
        if (string.IsNullOrWhiteSpace(targetNick))
        {
            var (tn, _) = await GetUserProfileBasicAsync(targetUid);
            targetNick = tn;
        }

        var updates = new Dictionary<string, object>();

        updates[$"friendRequestsIn/{targetUid}/{myUid}"] = new Dictionary<string, object>
        {
            ["fromUid"] = myUid,
            ["fromNick"] = myNick,
            ["createdAt"] = now,
            ["fromLevel"] = myLevel,
            ["fromEquippedDiceKey"] = myEquippedDiceKey
        };

        updates[$"friendRequestsOut/{myUid}/{targetUid}"] = new Dictionary<string, object>
        {
            ["toUid"] = targetUid,
            ["toNick"] = targetNick ?? "",
            ["createdAt"] = now,
            ["fromLevel"] = myLevel,
            ["fromEquippedDiceKey"] = myEquippedDiceKey
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

        int frLevel = TryInt(reqSnap.Child("fromLevel").Value, 1);
        string frEquippedDiceKey = reqSnap.Child("fromEquippedDiceKey").Value?.ToString() ?? "";

        int myLevel = Mathf.Max(1, FireBaseAuthManager.Instance.CurrentAccount.AccountLevel);
        string myEquippedDiceKey = FireBaseAuthManager.Instance.CurrentAccount.EquippedDiceKey ?? "";

        // 2) 멀티 업데이트 구성
        var updates = new Dictionary<string, object>();

        // friends 양쪽 생성
        updates[$"friends/{myUid}/{fromUid}"] = new Dictionary<string, object>
        {
            ["uid"] = fromUid,
            ["nick"] = frNick,
            ["photoUrl"] = frPhoto,
            ["createdAt"] = now,
            ["accountLevel"] = frLevel,
            ["equippedDiceKey"] = frEquippedDiceKey
        };

        updates[$"friends/{fromUid}/{myUid}"] = new Dictionary<string, object>
        {
            ["uid"] = myUid,
            ["nick"] = myNick,
            ["photoUrl"] = myPhoto,
            ["createdAt"] = now,
            ["accountLevel"] = myLevel,
            ["equippedDiceKey"] = myEquippedDiceKey
        };

        // 요청 제거
        updates[$"friendRequestsIn/{myUid}/{fromUid}"] = null;
        updates[$"friendRequestsOut/{fromUid}/{myUid}"] = null;

        // 3) 상대에게 "수락됨" 알림 생성 (pushId 자동 생성)
        // notifications/{toUid}/{pushId}
        string pushId = Root.Child($"notifications/{fromUid}").Push().Key;

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

    /// <summary>
    /// 친구 요청 "거절" 처리.
    /// - 내게 들어온 요청(friendRequestsIn)과 상대가 보낸 요청(friendRequestsOut)을 동시에 제거한다.
    /// - 상대(fromUid)에게 "거절됨" 알림(notification)을 하나 생성한다.
    /// - 모든 변경을 UpdateChildrenAsync(멀티패스 업데이트)로 한 번에 처리하여
    ///   중간 상태(한쪽만 지워짐 등) 없이 원자적으로 반영되게 한다.
    /// </summary>
    public static async Task DeclineFriendRequestAsync(string fromUid)
    {
        string myUid = MyUid;

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // 내 닉네임(거절한 사람 닉)
        string myNick = await GetMyNickPublicAsync();

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
        string myNick = await GetMyNickPublicAsync();

        var updates = new Dictionary<string, object>();
        updates[$"friendRequestsOut/{myUid}/{targetUid}"] = null;
        updates[$"friendRequestsIn/{targetUid}/{myUid}"] = null;

        // 상대에게 "취소" 알림
        string pushId = Root.Child($"notifications/{targetUid}").Push().Key;

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
        if (string.IsNullOrEmpty(friendUid) || friendUid == myUid) return;

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        string myNick = await GetMyNickPublicAsync();
        // chats 경로는 항상 정렬 규칙으로 동일하게
        BuildChatPath(myUid, friendUid, out string chatKey, out string chatRootPath);

        var updates = new Dictionary<string, object>();
        // 1) friends 양쪽 삭제 (규칙상 auth.uid가 $uid 또는 $friendUid면 write 허용)
        updates[$"friends/{myUid}/{friendUid}"] = null;
        updates[$"friends/{friendUid}/{myUid}"] = null;

        // 2) 채팅 메시지 전체 삭제 (공유 messages라 양쪽 기록이 같이 사라짐)
        updates[$"{chatRootPath}/messages"] = null;

        // 3) 내 chatIndex만 삭제 (rules 때문에 상대 chatIndex는 내가 삭제 못함)
        updates[$"chatIndex/{myUid}/{chatKey}"] = null;

        // 4) 상대에게 "친구 삭제됨" 알림 보내기 (상대가 자기 chatIndex 정리하도록)
        string pushId = Root.Child($"notifications/{friendUid}").Push().Key;
        updates[$"notifications/{friendUid}/{pushId}"] = new Dictionary<string, object>
        {
            ["type"] = "friend_removed",   // rules에 추가 필요
            ["byUid"] = myUid,
            ["byNick"] = myNick,
            ["createdAt"] = now
        };

        await Root.UpdateChildrenAsync(updates);
    }

    /// <summary>
    /// "내 채팅 목록(chatIndex)에서만" 특정 상대(otherUid)와의 채팅 항목을 삭제한다.
    /// - 실제 채팅 메시지 데이터(chats/...)는 삭제하지 않는다.
    /// - 즉, UI에서 대화방 목록만 지우는 용도(내 쪽에서만 숨김/정리).
    ///
    /// 사용 시나리오:
    /// - 내가 채팅방을 '나가기/삭제' 눌렀을 때:
    ///   메시지는 남겨두고(상대/서버 기록 유지), 내 목록에서만 제거하고 싶을 때.
    /// </summary>
    public static async Task DeleteMyChatIndexOnlyAsync(string otherUid)
    {
        string myUid = MyUid;
        if (string.IsNullOrEmpty(otherUid) || otherUid == myUid) return;

        BuildChatPath(myUid, otherUid, out string chatKey, out _);
        await Root.Child($"chatIndex/{myUid}/{chatKey}").RemoveValueAsync();
    }

    /// <summary>
    /// 두 UID(a, b)로부터 "항상 동일한" 채팅 식별 키(chatKey)와
    /// 채팅 메시지 루트 경로(chatRootPath)를 생성한다.
    ///
    /// 핵심:
    /// - a,b 순서에 상관없이 결과가 같아야 한다.
    /// - 이를 위해 CompareOrdinal로 문자열을 비교해 작은 쪽을 앞에 둔다.
    ///
    /// 예)
    /// a=U2, b=U9 => chatKey="U2_U9", chatRootPath="chats/U2/U9"
    /// a=U9, b=U2 => chatKey="U2_U9", chatRootPath="chats/U2/U9"  (동일!)
    ///
    /// 이렇게 해야:
    /// - 한쪽은 "U2_U9", 다른쪽은 "U9_U2"로 저장되는 불일치 문제를 방지
    /// - 같은 대화방을 둘이 동일하게 참조 가능
    /// </summary>
    static void BuildChatPath(string a, string b, out string chatKey, out string chatRootPath)
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

    public static async Task SyncMyProfileToFriendsAsync()
    {
        string myUid = MyUid;
        var fb = FireBaseAuthManager.Instance;
        if (fb == null || fb.CurrentAccount == null) return;

        string myNick = fb.CurrentAccount.NickName?.Trim() ?? "";
        string myPhoto = fb.CurrentAccount.PhotoUrl?.Trim() ?? "";
        int myLevel = Mathf.Max(1, fb.CurrentAccount.AccountLevel);
        string myEquippedDiceKey = fb.CurrentAccount.EquippedDiceKey ?? "";

        var myFriendsSnap = await FirebaseDatabase.DefaultInstance
            .GetReference($"friends/{myUid}")
            .GetValueAsync();

        if (myFriendsSnap == null || !myFriendsSnap.Exists)
            return;

        var updates = new Dictionary<string, object>();

        foreach (var c in myFriendsSnap.Children)
        {
            string friendUid = c.Key;
            if (string.IsNullOrWhiteSpace(friendUid)) continue;

            updates[$"friends/{friendUid}/{myUid}/uid"] = myUid;
            updates[$"friends/{friendUid}/{myUid}/nick"] = myNick;
            updates[$"friends/{friendUid}/{myUid}/photoUrl"] = myPhoto;
            updates[$"friends/{friendUid}/{myUid}/accountLevel"] = myLevel;
            updates[$"friends/{friendUid}/{myUid}/equippedDiceKey"] = myEquippedDiceKey;

            // createdAt 없으면 validate 걸릴 수 있으니 유지 보정
            long createdAt = TryLong(c.Child("createdAt").Value);
            if (createdAt <= 0)
                createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            updates[$"friends/{friendUid}/{myUid}/createdAt"] = createdAt;
        }

        if (updates.Count > 0)
            await Root.UpdateChildrenAsync(updates);
    }

    public static async Task<FriendProfileData> GetFriendProfileAsync(string friendUid)
    {
        string myUid = MyUid;
        if (string.IsNullOrWhiteSpace(friendUid)) return null;

        var result = new FriendProfileData
        {
            uid = friendUid,
            nick = "",
            photoUrl = "",
            accountLevel = 1,
            equippedDiceKey = "",
            isOnline = false,
            lastSeenUnix = 0
        };

        try
        {
            var friendTask = FirebaseDatabase.DefaultInstance
                .GetReference($"friends/{myUid}/{friendUid}")
                .GetValueAsync();

            var presenceTask = FirebaseDatabase.DefaultInstance
                .GetReference($"presence/{friendUid}")
                .GetValueAsync();

            await Task.WhenAll(friendTask, presenceTask);

            var friendSnap = friendTask.Result;
            if (friendSnap != null && friendSnap.Exists)
            {
                result.nick = friendSnap.Child("nick").Value?.ToString() ?? "";
                result.photoUrl = friendSnap.Child("photoUrl").Value?.ToString() ?? "";
                result.accountLevel = TryInt(friendSnap.Child("accountLevel").Value, 1);
                result.equippedDiceKey = friendSnap.Child("equippedDiceKey").Value?.ToString() ?? "";
            }

            var presenceSnap = presenceTask.Result;
            if (presenceSnap != null && presenceSnap.Exists)
            {
                result.isOnline = TryBool(presenceSnap.Child("online").Value);
                result.lastSeenUnix = TryLong(presenceSnap.Child("lastSeen").Value);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[GetFriendProfileAsync] failed: {e.Message}");
        }

        return result;
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


    #region 유틸

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

    static int TryInt(object v, int fallback = 0)
    {
        if (v == null) return fallback;
        if (int.TryParse(v.ToString(), out var n)) return n;
        return fallback;
    }

    #endregion
}
