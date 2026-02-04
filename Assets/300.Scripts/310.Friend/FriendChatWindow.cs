using Firebase.Auth;
using Firebase.Database;
using Gpm.Ui;
using System;
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

    public bool useTyping; // 새로 들어오는 메시지에만 true 권장
}

public class FriendChatWindow : MonoBehaviour
{
    [Header("Root")]
    public GameObject root;
    public Button btnClose;

    [Header("Header")]
    public Text txtHeader; // "OOO 채팅중..." / "Chatting with OOO..."
    public Image dragHandleImage; // ← 여기(상단바)에 UIDragger 달면 됨

    [Header("Scroll")]
    public InfiniteScroll chatScroll;

    [Header("Input")]
    public TMP_InputField inputMsg;
    public Button btnSend;

    // 세션 정보
    string _myUid;
    string _friendUid;
    string _friendNick;
    string _chatId;

    // 매니저(히스토리 공유용)
    FrinedUiManager _mgr;

    // firebase
    DatabaseReference _msgRef;
    EventHandler<ChildChangedEventArgs> _onChildAdded;

    bool _opened;

    public void Setup(FrinedUiManager mgr, string myUid, string friendUid, string friendNick, string chatId)
    {
        _mgr = mgr;
        _myUid = myUid;
        _friendUid = friendUid;
        _friendNick = friendNick;
        _chatId = chatId;

        if (btnClose) btnClose.onClick.RemoveAllListeners();
        if (btnClose) btnClose.onClick.AddListener(Close);

        if (btnSend) btnSend.onClick.RemoveAllListeners();
        if (btnSend) btnSend.onClick.AddListener(() => _ = SendAsync());

        // 드래그 연결(선택)
        if (dragHandleImage != null)
        {
            var dragger = dragHandleImage.GetComponent<UIDragger>();
            if (dragger == null) dragger = dragHandleImage.gameObject.AddComponent<UIDragger>();
            dragger.target = transform as RectTransform;
        }
    }

    public void Open()
    {
        if (root) root.SetActive(true);
        transform.SetAsLastSibling(); // 맨 앞으로
        _opened = true;

        _mgr?.NotifyChatWindowOpened();

        ApplyHeaderLanguage();

        // UI 초기화 후, 메모리 기록 뿌리기
        FriendInfiniteScrollUtil.ClearAll(chatScroll);
        var history = _mgr.GetOrCreateChatHistory(_chatId);
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

        if (lang == Lauaguage.Kor) txtHeader.text = $"{nick} 채팅중...";
        else txtHeader.text = $"Chatting with {nick}...";
    }

    void SubscribeIfNeeded()
    {
        Unsubscribe();

        _msgRef = FirebaseDatabase.DefaultInstance.GetReference($"chats/{_chatId}/messages");

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
                if (isMine)
                {
                    fromNick = SafeNick(_mgr?.GetMyNick(), "나");
                }
                else
                {
                    // 친구 닉은 이미 세션에 있으니 이걸 우선 사용
                    fromNick = SafeNick(_friendNick, ShortUid(fromUid));
                }
            }

            var data = new FriendChatMessageData
            {
                msgId = msgId,
                fromUid = fromUid,
                fromNick = fromNick,
                text = text,
                ts = ts,
                isMine = isMine,

                // 새로 들어오는 메시지만 타이핑 애니메이션
                useTyping = true
            };


            // 메모리 히스토리에 추가(중복 방지)
            var history = _mgr.GetOrCreateChatHistory(_chatId);
            if (history.Exists(x => x.msgId == msgId)) return;

            history.Add(data);

            // UI 추가
            int idx = history.Count - 1;
            FriendInfiniteScrollUtil.Insert(chatScroll, data, idx);
            FriendInfiniteScrollUtil.UpdateAll(chatScroll);
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
            .GetReference($"chats/{_chatId}/messages")
            .Push();

        string msgId = push.Key;

        // 2) 로컬(메모리) + UI에 즉시 추가
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

        var history = _mgr.GetOrCreateChatHistory(_chatId);
        history.Add(data);

        FriendInfiniteScrollUtil.Insert(chatScroll, data, history.Count - 1);
        FriendInfiniteScrollUtil.UpdateAll(chatScroll);

        // 3) DB 전송
        var payload = new Dictionary<string, object>
        {
            ["fromUid"] = _myUid,
            ["fromNick"] = myNick,
            ["text"] = text,
            ["ts"] = now
        };

        await push.SetValueAsync(payload);

        await UpdateChatIndexAsync(
            chatId: _chatId,
            myUid: _myUid,
            myNick: myNick,
            friendUid: _friendUid,
            friendNick: _friendNick,
            lastText: text,
            ts: now
                    );
    }

    async Task UpdateChatIndexAsync(string chatId, string myUid, string myNick, string friendUid, string friendNick, string lastText, long ts)
    {
        var root = FirebaseDatabase.DefaultInstance.RootReference;

        // chatIndex/{uid}/{chatId} 형태로 양쪽 다 기록
        var updates = new Dictionary<string, object>();

        // 내 인덱스
        updates[$"chatIndex/{myUid}/{chatId}/withUid"] = friendUid;
        updates[$"chatIndex/{myUid}/{chatId}/withNick"] = friendNick;
        updates[$"chatIndex/{myUid}/{chatId}/lastText"] = lastText;
        updates[$"chatIndex/{myUid}/{chatId}/lastFromUid"] = myUid;
        updates[$"chatIndex/{myUid}/{chatId}/lastTs"] = ts;

        // 상대 인덱스
        updates[$"chatIndex/{friendUid}/{chatId}/withUid"] = myUid;
        updates[$"chatIndex/{friendUid}/{chatId}/withNick"] = myNick;
        updates[$"chatIndex/{friendUid}/{chatId}/lastText"] = lastText;
        updates[$"chatIndex/{friendUid}/{chatId}/lastFromUid"] = myUid;
        updates[$"chatIndex/{friendUid}/{chatId}/lastTs"] = ts;

        await root.UpdateChildrenAsync(updates);
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

    void OnDisable()
    {
        Unsubscribe();
    }
}
