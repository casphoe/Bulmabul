using Fusion;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// RoomScene에서 GameScene으로 넘어갈 때 방 참가자 정보를 임시 보관하는 캐시.
/// 
/// 팀 모드일 경우:
/// - RoomMembersState의 슬롯 기준 팀 정보를 저장한다.
/// - 0, 2번 슬롯 = Red
/// - 1, 3번 슬롯 = Blue
/// </summary>
public static class BulmabulGameStartCache
{
    [Serializable]
    public struct CachedPlayer
    {
        public int playerId;
        public string nickname;
        public int level;
        public bool isLeader;

        /// <summary>
        /// 0 = None, 1 = Red, 2 = Blue
        /// </summary>
        public int teamSideInt;

        /// <summary>
        /// RoomScene에서의 슬롯 인덱스.
        /// </summary>
        public int roomSlotIndex;

        public string photoUrl;
    }

    private static readonly List<CachedPlayer> _players = new List<CachedPlayer>();

    public static bool HasCache => _players.Count > 0;

    public static IReadOnlyList<CachedPlayer> Players => _players;

    /// <summary>
    /// 0 = Solo, 1 = Team
    /// </summary>
    public static int ModeInt { get; private set; }

    public static void Clear()
    {
        _players.Clear();
        ModeInt = 0;
    }

    /// <summary>
    /// RoomScene의 방 참가자 정보를 GameScene으로 넘기기 위해 저장한다.
    /// </summary>
    public static void CaptureFromRoom(RoomMembersState room)
    {
        Clear();

        if (room == null)
            return;

        ModeInt = room.ModeInt;

        for (int i = 0; i < RoomMembersState.MaxSlots; i++)
        {
            RoomMembersState.MemberSlot slot = room.Slots.Get(i);

            if (slot.occupied == 0)
                continue;

            if (slot.player == PlayerRef.None)
                continue;

            TeamSide team = TeamSide.None;

            if (room.ModeInt == (int)MatchMode.Team)
                team = room.GetTeamBySlotIndex(i);

            string nick = slot.nickname.ToString();

            if (string.IsNullOrWhiteSpace(nick))
                nick = $"Player {i + 1}";

            _players.Add(new CachedPlayer
            {
                playerId = slot.player.PlayerId,
                nickname = nick.Trim(),
                level = Mathf.Max(1, slot.level),
                isLeader = slot.player == room.Leader,
                teamSideInt = (int)team,
                roomSlotIndex = i,
                photoUrl = slot.photoUrl.ToString()
            });
        }

        Debug.Log($"[BulmabulGameStartCache] Captured players: {_players.Count}, ModeInt: {ModeInt}");
    }

    public static bool TryGetByPlayer(PlayerRef player, out CachedPlayer data)
    {
        for (int i = 0; i < _players.Count; i++)
        {
            if (_players[i].playerId == player.PlayerId)
            {
                data = _players[i];
                return true;
            }
        }

        data = default;
        return false;
    }
}