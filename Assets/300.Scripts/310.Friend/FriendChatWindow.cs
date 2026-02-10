using Firebase.Database;
using Gpm.Ui;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#region Data Model

/// <summary>
/// 채팅 1건(메시지 1개)을 InfiniteScroll에 넣기 위한 데이터 모델
/// - InfiniteScrollData를 상속하면 GPM InfiniteScroll이 이 타입을 데이터로 취급한다.
/// 
/// 필드 설명:
/// - msgId     : Firebase Push()로 생성된 메시지 고유 키(중복 방지 / 정렬 / 캐시 키)
/// - fromUid   : 보낸 사람 uid (내 uid면 내 메시지)
/// - text      : 메시지 본문
/// - fromNick  : 보낸 사람 닉네임(저장되어 있으면 표시, 없으면 fallback)
/// - ts        : Unix seconds (UTC 기준 권장) - 화면에 시간 표시용
/// - isMine    : 내 메시지인지(좌/우 정렬, 색상 등 UI 분기용)
/// - useTyping : 타이핑 애니메이션 사용 여부(셀에서 typing효과를 줄지 말지)
/// </summary>
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

#endregion

/// <summary>
/// 친구 1:1 채팅창 UI 컨트롤러
/// 
/// 역할(Responsibility)
/// 1) 채팅창 열기/닫기(Open/Close)
/// 2) 로컬(메모리) 히스토리를 Scroll UI에 뿌리기
/// 3) Firebase chats/{a}/{b}/messages 구독(ChildAdded) → 새 메시지 수신 시 UI 추가
/// 4) 메시지 보내기(Push + SetValueAsync)
/// 5) 메시지 수신/발송 후 스크롤을 맨 아래로 내리기
/// 6) 오프라인(친구 로그아웃/프레즌스 false) 상태면 전송 막고 토스트 표시
/// 
/// 주의:
/// - Firebase 이벤트 콜백은 메인 스레드가 아닐 수 있다.
///   하지만 Unity SDK에서 ChildAdded 콜백은 보통 메인에서 들어오는 경우가 많지만,
///   안전하게 하려면 매니저의 _mainThreadQueue를 타도록 설계하는 게 가장 확실하다.
///   (여기 코드는 단순화를 위해 바로 UI를 갱신하고 있음)
/// </summary>
public class FriendChatWindow : MonoBehaviour
{
    #region Inspector (UI References)

    [Header("Root")]
    /// <summary>
    /// 채팅창 전체를 켜고 끄는 Root 오브젝트
    /// - Open()에서 SetActive(true)
    /// - Close()에서 SetActive(false)
    /// </summary>
    public GameObject root;

    /// <summary>
    /// 닫기 버튼
    /// </summary>
    public Button btnClose;

    [Header("Header")]

    /// <summary>
    /// 헤더 텍스트(상대 닉네임 + 상태 문구)
    /// </summary>
    public Text txtHeader;

    /// <summary>
    /// 창을 드래그해서 움직이게 하는 핸들 이미지(UIDragger 부착)
    /// </summary>
    public Image dragHandleImage;

    [Header("Scroll")]

    /// <summary>
    /// 채팅 메시지 목록을 보여주는 InfiniteScroll
    /// </summary>
    public InfiniteScroll chatScroll;

    /// <summary>
    /// ScrollRect(맨 아래로 내리기 위해 필요)
    /// - 인스펙터에 연결 안 했으면 Setup에서 자동 탐색
    /// </summary>
    public ScrollRect chatScrollRect;

    [Header("Input")]

    /// <summary>
    /// 메시지 입력창(TMP)
    /// </summary>
    public TMP_InputField inputMsg;

    /// <summary>
    /// 전송 버튼
    /// </summary>
    public Button btnSend;
    #endregion

    #region Session / State

    // 세션 정보

    /// <summary>내 uid</summary>
    string _myUid;

    /// <summary>상대 uid</summary>
    string _friendUid;

    /// <summary>상대 닉네임(헤더 표시, fallback 용)</summary>
    string _friendNick;

    /// <summary>
    /// chats/{a}/{b} 형태의 루트 경로(정렬된 uid 기준)
    /// 예) chats/uid1/uid9
    /// </summary>
    string _chatRootPath;

    /// <summary>
    /// 로컬에서 이 채팅방을 식별하는 키(딕셔너리 key로 사용)
    /// 예) uid1_uid9
    /// </summary>
    string _chatKey;

    /// <summary>
    /// 상위 매니저 참조
    /// - 로컬 히스토리 저장/조회
    /// - 내 닉네임 얻기
    /// - chatIndex 갱신
    /// - 열림/닫힘 카운팅
    /// </summary>
    FrinedUiManager _mgr;

    /// <summary>messages 노드 레퍼런스</summary>
    DatabaseReference _msgRef;

    /// <summary>ChildAdded 이벤트 핸들러(구독 해제에 필요)</summary>
    EventHandler<ChildChangedEventArgs> _onChildAdded;

    /// <summary>
    /// 창이 현재 열린 상태인지
    /// - Close()를 누르면 false
    /// - 열린 상태에서만 메시지 수신 시 UI 반영
    /// </summary>
    bool _opened;

    #endregion

    #region Setup / Open / Close

    /// <summary>
    /// 채팅창 초기 설정
    /// - 필수 세션 정보(myUid, friendUid, friendNick) 저장
    /// - uid 정렬 규칙으로 chatKey / chatRootPath 생성
    /// - 버튼 이벤트(닫기/전송) 등록
    /// - 드래그 핸들(UIDragger) 부착
    /// - ScrollRect 자동 탐색(인스펙터 미연결 대비)
    /// </summary>
    public void Setup(FrinedUiManager mgr, string myUid, string friendUid, string friendNick)
    {
        _mgr = mgr;
        _myUid = myUid;
        _friendUid = friendUid;
        _friendNick = friendNick;

        // (A) 정렬된 uid 기준으로 채팅 경로/키 생성
        // - 같은 두 uid면 항상 같은 결과가 나와야 한다.
        BuildChatPath(_myUid, _friendUid, out _chatKey, out _chatRootPath);

        // (B) 닫기 버튼 리스너 갱신
        // - 프리팹 재사용/재바인딩 시 중복 호출 방지 위해 RemoveAllListeners
        if (btnClose) btnClose.onClick.RemoveAllListeners();
        if (btnClose) btnClose.onClick.AddListener(Close);

        // (C) 전송 버튼 리스너 갱신
        if (btnSend) btnSend.onClick.RemoveAllListeners();
        if (btnSend) btnSend.onClick.AddListener(() => _ = SendAsync());

        // (D) 드래그 핸들에 UIDragger 부착
        // - dragHandleImage를 잡고 창 전체(RectTransform)를 이동시키는 방식
        if (dragHandleImage != null)
        {
            var dragger = dragHandleImage.GetComponent<UIDragger>();
            if (dragger == null) dragger = dragHandleImage.gameObject.AddComponent<UIDragger>();
            dragger.target = transform as RectTransform;
        }

        // (E) ScrollRect 자동 탐색
        // - 채팅창 구조 변경/인스펙터 미연결에 대비한 안전장치
        if (chatScrollRect == null)
            chatScrollRect = GetComponentInChildren<ScrollRect>(true);
    }

    /// <summary>
    /// 채팅창 열기
    /// 동작:
    /// 1) Root 켜기 + UI 최상단으로 올리기(겹칠 때 위로)
    /// 2) 열린 상태 플래그 true
    /// 3) 매니저에 "열렸다" 알림(카운팅)
    /// 4) 헤더 텍스트(언어) 적용
    /// 5) 로컬 히스토리를 스크롤에 뿌림
    /// 6) 스크롤 맨 아래로 내림
    /// 7) Firebase messages 구독 시작
    /// </summary>
    public void Open()
    {
        if (root) root.SetActive(true);

        // UI 레이어 최상위로(다른 창보다 위)
        transform.SetAsLastSibling();
        _opened = true;

        // 매니저 카운팅(패널 토글 UX 등에 사용)
        _mgr?.NotifyChatWindowOpened();

        // 헤더 언어 적용
        ApplyHeaderLanguage();

        // (1) 로컬(메모리) 히스토리 뿌리기
        FriendInfiniteScrollUtil.ClearAll(chatScroll);
        var history = _mgr.GetOrCreateChatHistory(_chatKey);
        for (int i = 0; i < history.Count; i++)
        {
            var h = history[i];

            // - Open 시 기존 히스토리를 다시 그릴 때는 타이핑 애니메이션을 끄는 게 보통 자연스러움
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

        // (2) 맨 아래로 내림(한 프레임 뒤)
        StartCoroutine(CoScrollToBottom());

        // (3) 새 메시지 수신 구독
        SubscribeIfNeeded();
    }

    /// <summary>
    /// 채팅창 닫기
    /// 동작:
    /// 1) 열린 상태 플래그 false
    /// 2) Firebase 구독 해제(중복 이벤트/메모리 누수 방지)
    /// 3) Root 끄기
    /// 4) 매니저에 "닫혔다" 알림(카운팅)
    /// </summary>
    public void Close()
    {
        _opened = false;

        // 메시지 구독 해제(꼭 필요)
        Unsubscribe();

        // UI 끄기
        if (root) root.SetActive(false);

        // 매니저 카운팅
        _mgr?.NotifyChatWindowClosed();
    }

    #endregion

    #region Header / Language

    /// <summary>
    /// 헤더(상단 텍스트) 언어 적용
    /// - 한국어: "OOO 채팅중..."
    /// - 영어  : "Chatting with OOO..."
    /// </summary>
    public void ApplyHeaderLanguage()
    {
        if (txtHeader == null) return;

        var lang = (LaguageManager.Instance != null)
            ? LaguageManager.Instance.currentLang
            : Lauaguage.Kor;

        string nick = string.IsNullOrWhiteSpace(_friendNick) ? "알 수 없음" : _friendNick;
        txtHeader.text = (lang == Lauaguage.Kor) ? $"{nick} 채팅중..." : $"Chatting with {nick}...";
    }

    #endregion

    #region Firebase Subscribe (Incoming Messages)

    /// <summary>
    ///Firebase messages 구독 시작
    /// 
    /// 흐름:
    /// 1) 기존 구독이 있으면 먼저 해제(Unsubscribe)
    /// 2) chats/{a}/{b}/messages 레퍼런스 잡기
    /// 3) ChildAdded 이벤트 등록
    /// 4) 새 메시지 수신 시:
    ///    - 스냅샷에서 msgId/fromUid/text/fromNick/ts 파싱
    ///    - 내 메시지인지 판단(isMine)
    ///    - 닉네임 fallback 처리
    ///    - 로컬 히스토리에 중복 msgId면 무시
    ///    - 히스토리에 추가 후 스크롤 UI에 Insert
    ///    - 맨 아래로 내림
    /// 
    /// </summary>
    void SubscribeIfNeeded()
    {
        // 중복 구독 방지
        Unsubscribe();

        // messages 레퍼런스
        _msgRef = FirebaseDatabase.DefaultInstance.GetReference($"{_chatRootPath}/messages");

        // ChildAdded 핸들러 저장(나중에 해제하려고 필드에 보관)
        _onChildAdded = (s, e) =>
        {
            // (A) 창이 닫혀있으면 UI 반영하지 않음
            if (!_opened) return;

            // (B) Firebase 에러/스냅샷 유효성 검사
            if (e.DatabaseError != null) return;
            if (e.Snapshot == null || !e.Snapshot.Exists) return;

            var snap = e.Snapshot;
            // msgId = Push 키 (중복방지/캐시 키)
            string msgId = snap.Key;

            // 메시지 필드들 파싱
            string fromUid = snap.Child("fromUid").Value?.ToString() ?? "";
            string text = snap.Child("text").Value?.ToString() ?? "";
            string fromNick = snap.Child("fromNick").Value?.ToString() ?? "";
            long ts = TryLong(snap.Child("ts").Value);

            // 내 메시지인지 판단
            bool isMine = (fromUid == _myUid);

            // 닉네임이 비어있으면 fallback
            // - 내 메시지: 내 닉(_mgr.GetMyNick())
            // - 상대 메시지: friendNick 또는 uid 일부
            if (string.IsNullOrWhiteSpace(fromNick))
            {
                if (isMine) fromNick = SafeNick(_mgr?.GetMyNick(), "나");
                else fromNick = SafeNick(_friendNick, ShortUid(fromUid));
            }

            // InfiniteScroll에 넣을 데이터 구성
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

            // 로컬 히스토리 가져오기(없으면 생성)
            var history = _mgr.GetOrCreateChatHistory(_chatKey);

            // msgId 중복이면 무시(재접속/리스너 중복/이벤트 중복 대비)
            if (history.Exists(x => x.msgId == msgId)) return;

            // 히스토리에 추가
            history.Add(data);

            // UI에 추가 (항상 마지막 인덱스로 Insert)
            int idx = history.Count - 1;
            FriendInfiniteScrollUtil.Insert(chatScroll, data, idx);
            FriendInfiniteScrollUtil.UpdateAll(chatScroll);

            // 새 메시지 오면 자동 아래로
            StartCoroutine(CoScrollToBottom());
        };

        // 구독 등록
        _msgRef.ChildAdded += _onChildAdded;
    }

    /// <summary>
    /// Firebase 메시지 구독 해제
    /// - 닫기/비활성화/파괴 시 반드시 호출해야 함
    /// - 안 하면:
    ///   1) 메시지가 올 때마다 중복으로 이벤트가 호출되고
    ///   2) 파괴된 오브젝트를 참조하려다 예외가 날 수 있으며
    ///   3) 메모리 누수/성능 문제가 발생한다.
    /// </summary>
    void Unsubscribe()
    {
        if (_msgRef != null && _onChildAdded != null)
            _msgRef.ChildAdded -= _onChildAdded;

        _msgRef = null;
        _onChildAdded = null;
    }

    #endregion

    #region Send Message

    /// <summary>
    /// 메시지 전송 처리
    /// 
    /// 처리 순서:
    /// 1) 입력 텍스트 가져오기(Trim) + 빈 값이면 리턴
    /// 2) 상대 온라인 여부 확인(presence/{friendUid}/online)
    ///    - offline이면 전송 막고 토스트 표시
    /// 3) 입력창 비우기(UX)
    /// 4) Push()로 msgId 생성
    /// 5) 로컬에 즉시 반영(UI가 "바로" 뜨게)
    /// 6) 서버로 SetValueAsync 전송
    /// 7) 성공하면 내 chatIndex 갱신(최근 메시지 표시용)
    /// 8) 실패하면 오프라인 토스트(상대가 나가면서 messages가 삭제되는 등)
    /// </summary>
    async Task SendAsync()
    {
        if (inputMsg == null) return;

        // (1) 입력 텍스트 확보
        string text = (inputMsg.text ?? "").Trim();
        if (string.IsNullOrEmpty(text)) return;

        // (2) 상대 온라인 확인
        bool friendOnline = await IsFriendOnlineAsync(_friendUid);

        if (!friendOnline)
        {
            // offline이면 전송 막고 토스트
            ShowFriendOfflineToast(_friendNick);

            // 입력 유지/삭제는 취향인데, 보통은 유지가 좋아서 유지 추천
            // inputMsg.text = text; // 유지하고 싶으면 이렇게 (이미 text에 담아둠)
            return;
        }

        // (3) 입력창 초기화(보내기 성공/실패와 별개로 UX상 먼저 비움)
        inputMsg.text = "";

        // (4) 메시지 메타
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string myNick = SafeNick(_mgr?.GetMyNick(), "나");

        // (5) msgId 생성(Push)
        var push = FirebaseDatabase.DefaultInstance
            .GetReference($"{_chatRootPath}/messages")
            .Push();

        string msgId = push.Key;

        // (6) 로컬 즉시 반영(서버 응답 기다리지 않고 UI에 먼저 보이게)
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

        // (7) 서버로 전송할 payload 구성
        var payload = new Dictionary<string, object>
        {
            ["fromUid"] = _myUid,
            ["fromNick"] = myNick,
            ["text"] = text,
            ["ts"] = now
        };

        try
        {
            // (8) 서버 전송
            await push.SetValueAsync(payload);

            // (9) 내 chatIndex 갱신(최근 메시지/시간 표시용)
            // - 상대 chatIndex는 rules상 상대만 쓰게 하는 게 안전함
            await _mgr.UpdateMyChatIndexAsync(_friendUid, _friendNick, text, _myUid, now);
        }
        catch (Exception)
        {
            // 전송 실패 시(권한/네트워크/상대가 나가며 messages 삭제 등)
            // - 로컬에 이미 찍힌 메시지를 "회색 처리" 같은 걸 하고 싶다면
            //   FriendChatMessageData에 sendState를 추가하는 방식 추천
            ShowFriendOfflineToast(_friendNick);
        }
    }

    #endregion

    #region UI Helpers

    /// <summary>
    /// 스크롤을 맨 아래로 내리는 코루틴
    /// - InfiniteScroll/레이아웃 업데이트가 "한 프레임 뒤"에 반영되는 경우가 많아서
    ///   yield return null 후에 verticalNormalizedPosition을 조정한다.
    /// </summary>
    IEnumerator CoScrollToBottom()
    {
        // UI 업데이트 1프레임 기다렸다가 내림
        yield return null;
        if (chatScrollRect != null)
            chatScrollRect.verticalNormalizedPosition = 0f;
    }

    #endregion

    #region Chat Path / Parsing Utils

    /// <summary>
    /// 두 uid(a,b)를 항상 동일한 순서로 정렬해
    /// chats/{sortedA}/{sortedB} 경로와
    /// 로컬 식별키 chatKey(sortedA_sortedB)를 생성한다.
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

    /// <summary>
    /// Firebase 값(object)을 long으로 안전 변환
    /// </summary>
    static long TryLong(object v)
    {
        if (v == null) return 0;
        if (long.TryParse(v.ToString(), out var n)) return n;
        return 0;
    }

    /// <summary>
    /// 닉네임이 비어있으면 fallback으로 대체
    /// </summary>
    static string SafeNick(string nick, string fallback)
        => string.IsNullOrWhiteSpace(nick) ? fallback : nick;

    /// <summary>
    /// uid가 너무 길면 앞부분만 잘라 UI 표시용 문자열 생성
    /// </summary>
    static string ShortUid(string uid)
    {
        if (string.IsNullOrWhiteSpace(uid)) return "unknown";
        return uid.Length >= 6 ? uid.Substring(0, 6) : uid;
    }

    #endregion

    #region Lifecycle Safety

    /// <summary>
    /// 오브젝트가 비활성화될 때도 구독을 끊어준다.
    /// - 씬 전환/패널 닫힘/Destroy 등 다양한 경우에 대비
    /// </summary>
    void OnDisable() => Unsubscribe();

    #endregion

    #region Presence / Offline Toast

    /// <summary>
    /// 상대 온라인 여부 확인
    /// - presence/{friendUid}/online 값을 1회 읽어 true/false 반환
    /// - rules에 의해 auth != null이면 read 가능(presence는 read: auth != null)
    /// 
    /// 실패 시(네트워크/권한)에는 안전하게 offline(false) 처리한다.
    /// </summary>
    private async Task<bool> IsFriendOnlineAsync(string friendUid)
    {
        if (string.IsNullOrEmpty(friendUid)) return false;

        try
        {
            var snap = await FirebaseDatabase.DefaultInstance
                .GetReference($"presence/{friendUid}/online")
                .GetValueAsync();

            if (snap == null || !snap.Exists) return false;

            // bool 타입이면 바로 반환
            if (snap.Value is bool b) return b;

            // string "true"/"false" 등으로 올 수도 있으니 파싱
            bool parsed = false;
            bool.TryParse(snap.Value.ToString(), out parsed);
            return parsed;
        }
        catch
        {
            // 네트워크 문제/권한 문제면 안전하게 offline 취급
            return false;
        }
    }

    /// <summary>
    /// 상대가 오프라인일 때 토스트 표시
    /// - 언어(한/영)에 맞춰 메시지 분기
    /// </summary>
    private void ShowFriendOfflineToast(string friendNick)
    {
        string nick = string.IsNullOrWhiteSpace(friendNick) ? "알 수 없음" : friendNick;

        var lang = (LaguageManager.Instance != null)
            ? LaguageManager.Instance.currentLang
            : Lauaguage.Kor;

        if (lang == Lauaguage.Kor)
            ToastMessageManager.instance?.ShowToast($"{nick} 친구가 로그아웃 상태입니다.", $"{nick} is logged out.");
        else
            ToastMessageManager.instance?.ShowToast($"{nick} is logged out.", $"{nick} is logged out.");
    }

    #endregion
}
