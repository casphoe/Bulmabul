using Fusion;
using Gpm.Ui;

public enum ChatChannel
{
    Global = 0,   // 전체
    Party = 1    // 파티
}

public class ChatMessageData : InfiniteScrollData
{
    public int viewIndex;
    public PlayerRef sender;
    public string nickname;
    public string message;
    public string timeText;
    public bool isMine;
    public ChatChannel channel;

    public string messageId;   // 유니크 키 (재활용/리빌드에도 동일하게)
    public bool animate;       // 최신 메시지만 타자 효과

    //현재 모드가 팀전인지
    public TeamSide team;        // Red/Blue/None
    public bool isTeamMode;      // true면 팀전 UI 스킨 적용
}