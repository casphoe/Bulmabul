using Fusion;
using Gpm.Ui;

public class ChatMessageData : InfiniteScrollData
{
    public int viewIndex;
    public PlayerRef sender;
    public string nickname;
    public string message;
    public string timeText;
    public bool isMine;

    public string messageId;   // 유니크 키 (재활용/리빌드에도 동일하게)
    public bool animate;       // 최신 메시지만 타자 효과
}