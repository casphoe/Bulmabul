using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class InviteCandidateRow
{
    public enum InviteState
    {
        CanInvite = 0,      // 아무 상태 아님(초대 가능)
        Inviting = 1,       // 내가 초대 보냄 (friendRequestsOut/myUid/targetUid 존재)
        RequestedMe = 2,    // 상대가 나에게 요청 보냄 (friendRequestsIn/myUid/fromUid 존재)
        AlreadyFriend = 3   // 이미 친구 (friends/myUid/uid 존재)
    }

    public string uid;
    public string nick;     // 표시용 닉네임(공개 프로필)
    public string nickKey;  // 검색용/보조용
    public string photoUrl; // 무조건 채우기(없으면 "")

    public InviteState state;
}
