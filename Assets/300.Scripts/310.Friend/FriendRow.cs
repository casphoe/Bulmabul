using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;
using UnityEngine;

[Serializable]
public class FriendRow
{
    public string uid;       // friendUid (키)
    public string nick;      // friends/{myUid}/{friendUid}/nick
    public string photoUrl;  // friends/{myUid}/{friendUid}/photoUrl
    public long createdAt;   // friends/{myUid}/{friendUid}/createdAt

    public bool isOnline;
    public long lastSeenUnix; // 온라인이면 "최근 ping 시간" 또는 "접속 갱신 시간"
}
