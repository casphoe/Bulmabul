using System;

[Serializable]
public class FriendProfileData
{
    public string uid;
    public string nick;
    public string photoUrl;
    public int accountLevel;
    public string equippedDiceKey;

    public bool isOnline;
    public long lastSeenUnix;
}