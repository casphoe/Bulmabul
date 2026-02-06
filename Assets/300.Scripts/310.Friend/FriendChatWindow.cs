using Firebase.Database;
using Gpm.Ui;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class FriendChatMessageData : InfiniteScrollData
{
    public string msgId;
    public string fromUid;
    public string text;
    public string fromNick;
    public long ts;
    public bool isMine;
    public bool useTyping;
}

public class FriendChatWindow : MonoBehaviour
{
    [Header("Root")]
    public GameObject root;
    public Button btnClose;

    [Header("Header")]
    public Text txtHeader;
    public Image dragHandleImage;

    [Header("Scroll")]
    public InfiniteScroll chatScroll;
    public ScrollRect chatScrollRect; // ✅ 인스펙터 연결(없으면 자동 탐색)

    [Header("Input")]
    public TMP_InputField inputMsg;
    public Button btnSend;

    // 세션 정보
    string _myUid;
    string _friendUid;
    string _friendNick;

    // Firebase 경로 (chats/{a}/{b})
    string _chatRootPath;
    string _chatKey; // 로컬 딕셔너리 key로도 사용 가능

    FrinedUiManager _mgr;

    DatabaseReference _msgRef;
    EventHandler<ChildChangedEventArgs> _onChildAdded;

    bool _opened;

    public void Setup(FrinedUiManager mgr, string myUid, string friendUid, string friendNick)
    {
        _mgr = mgr;
        _myUid = myUid;
        _friendUid = friendUid;
        _friendNick = friendNick;

        // 정렬된 채팅 경로/키 생성
        BuildChatPath(_myUid, _friendUid, out _chatKey, out _chatRootPath);

        if (btnClose) btnClose.onClick.RemoveAllListeners();
        if (btnClose) btnClose.onClick.AddListener(Close);

        if (btnSend) btnSend.onClick.RemoveAllListeners();
        if (btnSend) btnSend.onClick.AddListener(() => _ = SendAsync());

        if (dragHandleImage != null)
        {
            var dragger = dragHandleImage.GetComponent<UIDragger>();
            if (dragger == null) dragger = dragHandleImage.gameObject.AddComponent<UIDragger>();
            dragger.target = transform as RectTransform;
        }

        if (chatScrollRect == null)
            chatScrollRect = GetComponentInChildren<ScrollRect>(true);
    }

    public void Open()
    {
        if (root) root.SetActive(true);
        transform.SetAsLastSibling();
        _opened = true;

        _mgr?.NotifyChatWindowOpened();
        ApplyHeaderLanguage();

        // 메모리 히스토리 뿌리기
        FriendInfiniteScrollUtil.ClearAll(chatScroll);
        var history = _mgr.GetOrCreateChatHistory(_chatKey);
        for (int i = 0; i < history.Count; i++)
        {
            var h = history[i];
            var clone = new FriendChatMessageData
            {
                msgId = h.msgId,
                fromUid = h.fromUid,
                fromNick = h.fromNick,
                text = h.text,
                ts = h.ts,
                isMine = h.isMine,
                useTyping = false
            };
            FriendInfiniteScrollUtil.Insert(chatScroll, clone, i);
        }
        FriendInfiniteScrollUtil.UpdateAll(chatScroll);

        StartCoroutine(CoScrollToBottom());

        SubscribeIfNeeded();
    }

    public void Close()
    {
        _opened = false;
        Unsubscribe();
        if (root) root.SetActive(false);
        _mgr?.NotifyChatWindowClosed();
    }

    public void ApplyHeaderLanguage()
    {
        if (txtHeader == null) return;

        var lang = (LaguageManager.Instance != null)
            ? LaguageManager.Instance.currentLang
            : Lauaguage.Kor;

        string nick = string.IsNullOrWhiteSpace(_friendNick) ? "알 수 없음" : _friendNick;
        txtHeader.text = (lang == Lauaguage.Kor) ? $"{nick} 채팅중..." : $"Chatting with {nick}...";
    }

    void SubscribeIfNeeded()
    {
        Unsubscribe();

        _msgRef = FirebaseDatabase.DefaultInstance.GetReference($"{_chatRootPath}/messages");

        _onChildAdded = (s, e) =>
        {
            if (!_opened) return;
            if (e.DatabaseError != null) return;
            if (e.Snapshot == null || !e.Snapshot.Exists) return;

            var snap = e.Snapshot;
            string msgId = snap.Key;

            string fromUid = snap.Child("fromUid").Value?.ToString() ?? "";
            string text = snap.Child("text").Value?.ToString() ?? "";
            string fromNick = snap.Child("fromNick").Value?.ToString() ?? "";
            long ts = TryLong(snap.Child("ts").Value);

            bool isMine = (fromUid == _myUid);

            if (string.IsNullOrWhiteSpace(fromNick))
            {
                if (isMine) fromNick = SafeNick(_mgr?.GetMyNick(), "나");
                else fromNick = SafeNick(_friendNick, ShortUid(fromUid));
            }

            var data = new FriendChatMessageData
            {
                msgId = msgId,
                fromUid = fromUid,
                fromNick = fromNick,
                text = text,
                ts = ts,
                isMine = isMine,
                useTyping = true
            };

            var history = _mgr.GetOrCreateChatHistory(_chatKey);
            if (history.Exists(x => x.msgId == msgId)) return;

            history.Add(data);

            int idx = history.Count - 1;
            FriendInfiniteScrollUtil.Insert(chatScroll, data, idx);
            FriendInfiniteScrollUtil.UpdateAll(chatScroll);

            // 새 메시지 오면 자동 아래로
            StartCoroutine(CoScrollToBottom());
        };

        _msgRef.ChildAdded += _onChildAdded;
    }

    void Unsubscribe()
    {
        if (_msgRef != null && _onChildAdded != null)
            _msgRef.ChildAdded -= _onChildAdded;

        _msgRef = null;
        _onChildAdded = null;
    }

    async Task SendAsync()
    {
        if (inputMsg == null) return;

        string text = (inputMsg.text ?? "").Trim();
        if (string.IsNullOrEmpty(text)) return;

        inputMsg.text = "";

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string myNick = SafeNick(_mgr?.GetMyNick(), "나");

        var push = FirebaseDatabase.DefaultInstance
            .GetReference($"{_chatRootPath}/messages")
            .Push();

        string msgId = push.Key;

        //로컬 즉시 반영
        var data = new FriendChatMessageData
        {
            msgId = msgId,
            fromUid = _myUid,
            fromNick = myNick,
            text = text,
            ts = now,
            isMine = true,
            useTyping = true
        };

        var history = _mgr.GetOrCreateChatHistory(_chatKey);
        history.Add(data);
        FriendInfiniteScrollUtil.Insert(chatScroll, data, history.Count - 1);
        FriendInfiniteScrollUtil.UpdateAll(chatScroll);
        StartCoroutine(CoScrollToBottom());

        // DB 전송
        var payload = new Dictionary<string, object>
        {
            ["fromUid"] = _myUid,
            ["fromNick"] = myNick,
            ["text"] = text,
            ["ts"] = now
        };

        await push.SetValueAsync(payload);

        // chatIndex는 "내 것만" 갱신하도록 변경(상대꺼 쓰지 마)
        await _mgr.UpdateMyChatIndexAsync(_friendUid, _friendNick, text, _myUid, now);
    }

    IEnumerator CoScrollToBottom()
    {
        // UI 업데이트 1프레임 기다렸다가 내림
        yield return null;
        if (chatScrollRect != null)
            chatScrollRect.verticalNormalizedPosition = 0f;
    }

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

    static long TryLong(object v)
    {
        if (v == null) return 0;
        if (long.TryParse(v.ToString(), out var n)) return n;
        return 0;
    }

    static string SafeNick(string nick, string fallback)
        => string.IsNullOrWhiteSpace(nick) ? fallback : nick;

    static string ShortUid(string uid)
    {
        if (string.IsNullOrWhiteSpace(uid)) return "unknown";
        return uid.Length >= 6 ? uid.Substring(0, 6) : uid;
    }

    void OnDisable() => Unsubscribe();
}
