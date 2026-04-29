using System;
using Firebase.Auth;
using Firebase.Database;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 초대받은 유저 쪽에서 방 초대를 실시간으로 감지하고
/// 수락/거절 UI 패널을 띄우는 클래스.
/// 
/// 사용 위치:
/// - 보통 LobbyScene의 Canvas 아래에 배치한다.
/// 
/// 역할:
/// 1. Firebase roomInvites/{내 UID} 경로 감시
/// 2. pending 상태의 초대가 있으면 UI 패널 표시
/// 3. 수락 시 accepted로 상태 변경 후 해당 방 입장
/// 4. 거절 시 declined로 상태 변경
/// </summary>
public class RoomInviteReceiver : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject root; // 초대 수락/거절 UI 패널 전체 Root

    [Header("Text")]
    [SerializeField] private TMP_Text txtMessage; // 초대 메시지 표시 텍스트

    [Header("Buttons")]
    [SerializeField] private Button btnAccept;  // 수락 버튼
    [SerializeField] private Button btnDecline; // 거절 버튼

    // Firebase에서 내 초대 목록을 감시할 참조
    private DatabaseReference _inviteRef;

    // 현재 화면에 표시 중인 초대 데이터
    private RoomInviteData _currentInvite;

    // Firebase 리스너가 중복 등록되는 것을 방지하기 위한 플래그
    private bool _isListening;

    private void Awake()
    {
        // 시작 시 초대 패널은 숨김
        if (root != null)
            root.SetActive(false);

        // 수락 버튼 이벤트 연결
        if (btnAccept != null)
        {
            btnAccept.onClick.RemoveAllListeners();
            btnAccept.onClick.AddListener(OnClickAccept);
        }

        // 거절 버튼 이벤트 연결
        if (btnDecline != null)
        {
            btnDecline.onClick.RemoveAllListeners();
            btnDecline.onClick.AddListener(OnClickDecline);
        }

        // 버튼 텍스트를 현재 언어에 맞게 설정
        RefreshButtonTexts();
    }

    private void OnEnable()
    {
        // 오브젝트가 켜지면 내 초대 목록 감시 시작
        StartListen();
    }

    private void OnDisable()
    {
        // 오브젝트가 꺼지면 Firebase 리스너 제거
        StopListen();
    }

    /// <summary>
    /// Firebase에서 roomInvites/{내 UID} 경로를 실시간 감시한다.
    /// 
    /// 이 경로에는 다른 유저가 나에게 보낸 방 초대 정보가 저장된다.
    /// </summary>
    private void StartListen()
    {
        // 이미 리스닝 중이면 중복 등록하지 않음
        if (_isListening) return;

        var user = FirebaseAuth.DefaultInstance.CurrentUser;
        if (user == null)
        {
            Debug.LogWarning("[RoomInviteReceiver] 로그인 유저가 없습니다.");
            return;
        }

        string myUid = user.UserId;

        // 내 UID 기준으로 받은 초대 목록 경로 지정
        _inviteRef = FirebaseDatabase.DefaultInstance
            .RootReference
            .Child("roomInvites")
            .Child(myUid);

        // 해당 경로 전체 값이 바뀔 때마다 호출
        _inviteRef.ValueChanged += OnInviteValueChanged;
        _isListening = true;

        Debug.Log($"[RoomInviteReceiver] Listen start. uid={myUid}");
    }

    /// <summary>
    /// Firebase 리스너 제거.
    /// 씬 이동, 오브젝트 비활성화 시 중복 이벤트 발생을 막기 위해 반드시 제거해야 한다.
    /// </summary>
    private void StopListen()
    {
        if (!_isListening) return;

        if (_inviteRef != null)
            _inviteRef.ValueChanged -= OnInviteValueChanged;

        _inviteRef = null;
        _isListening = false;
    }

    /// <summary>
    /// roomInvites/{내 UID} 값이 변경되었을 때 호출된다.
    /// 
    /// 여기서 pending 상태의 초대만 검사하고,
    /// 여러 초대가 있을 경우 createdAt이 가장 최신인 초대 하나만 표시한다.
    /// </summary>
    private void OnInviteValueChanged(object sender, ValueChangedEventArgs e)
    {
        // Firebase 권한 오류 또는 읽기 오류 처리
        if (e.DatabaseError != null)
        {
            Debug.LogWarning($"[RoomInviteReceiver] DatabaseError: {e.DatabaseError.Message}");
            return;
        }

        // 초대 데이터가 없으면 종료
        if (e.Snapshot == null || !e.Snapshot.Exists)
            return;

        var user = FirebaseAuth.DefaultInstance.CurrentUser;
        string myUid = user != null ? user.UserId : "";

        RoomInviteData latestInvite = null;

        // 현재 시간. 초대 만료 여부 확인에 사용
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 내가 받은 모든 초대 순회
        foreach (var child in e.Snapshot.Children)
        {
            // pending 상태의 초대만 UI에 표시
            string status = child.Child("status").Value?.ToString() ?? "";
            if (status != "pending")
                continue;

            // 초대 만료 시간
            long expireAt = TryLong(child.Child("expireAt").Value);

            // 방 이름이 없으면 유효하지 않은 초대이므로 무시
            string roomName = child.Child("roomName").Value?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(roomName))
                continue;

            // Firebase Snapshot을 RoomInviteData로 변환
            var invite = new RoomInviteData
            {
                inviteId = child.Child("inviteId").Value?.ToString() ?? child.Key,

                fromUid = child.Child("fromUid").Value?.ToString() ?? "",
                fromNick = child.Child("fromNick").Value?.ToString() ?? "",

                toUid = child.Child("toUid").Value?.ToString() ?? myUid,
                toNick = child.Child("toNick").Value?.ToString() ?? "",

                roomName = roomName,
                roomMode = TryInt(child.Child("roomMode").Value, 0),
                map = TryInt(child.Child("map").Value, 0),
                maxPlayers = TryInt(child.Child("maxPlayers").Value, 4),

                createdAt = TryLong(child.Child("createdAt").Value),
                expireAt = expireAt,

                status = status
            };

            // 만료된 초대라면 expired 상태로 변경하고 UI에는 표시하지 않음
            if (expireAt > 0 && expireAt < now)
            {
                _ = RoomInviteService.SetInviteStatusAsync(invite, "expired");
                continue;
            }

            // 가장 최근 초대 하나만 선택
            if (latestInvite == null || invite.createdAt > latestInvite.createdAt)
                latestInvite = invite;
        }

        // 표시할 pending 초대가 있으면 수락/거절 패널 표시
        if (latestInvite != null)
            ShowInvite(latestInvite);
    }

    /// <summary>
    /// 초대 수락/거절 패널을 화면에 표시한다.
    /// </summary>
    private void ShowInvite(RoomInviteData invite)
    {
        // 이미 같은 초대가 표시 중이면 중복 표시하지 않음
        if (_currentInvite != null &&
            _currentInvite.inviteId == invite.inviteId &&
            root != null &&
            root.activeSelf)
        {
            return;
        }

        _currentInvite = invite;

        var lang = LaguageManager.Instance != null
            ? LaguageManager.Instance.currentLang
            : Lauaguage.Kor;

        // 초대 메시지 표시
        if (txtMessage != null)
        {
            if (lang == Lauaguage.Kor)
            {
                txtMessage.text =
                    $"{invite.fromNick}님이 방에 초대했습니다.\n" +
                    $"방 이름 : {invite.roomName}\n" +
                    "수락하시겠습니까?";
            }
            else
            {
                txtMessage.text =
                    $"{invite.fromNick} invited you to a room.\n" +
                    $"Room : {invite.roomName}\n" +
                    "Do you want to join?";
            }
        }

        // 언어가 변경되었을 수도 있으므로 버튼 텍스트도 갱신
        RefreshButtonTexts();

        // 패널 표시
        if (root != null)
            root.SetActive(true);
    }

    /// <summary>
    /// 수락 버튼 클릭.
    /// 
    /// 처리 순서:
    /// 1. 초대받은 방이 아직 입장 가능한지 확인
    /// 2. 방이 가득 찼으면 초대받은 사람에게 토스트 표시
    /// 3. 방이 가득 찬 경우 accepted 처리하지 않음
    /// 4. 입장 가능할 때만 accepted 처리 후 방 입장
    /// </summary>
    private async void OnClickAccept()
    {
        if (_currentInvite == null) return;

        try
        {
            RoomInviteData inviteToJoin = _currentInvite;

            string roomName = inviteToJoin.roomName;
            MatchMode mode = (MatchMode)inviteToJoin.roomMode;

            // 네트워크 런처가 없으면 입장 불가
            if (NetWorkLauncher.instance == null)
            {
                ToastMessageManager.instance?.ShowToast(
                    "네트워크 매니저를 찾을 수 없습니다.",
                    "Network manager not found."
                );
                return;
            }

            // =========================
            // 방 입장 가능 여부 먼저 확인
            // =========================
            // 여기서 방이 가득 찼는지 확인한다.
            // 가득 찬 경우 accepted로 바꾸지 않고,
            // 초대받은 사람에게 "방이 가득 찼습니다." 토스트만 띄운다.
            if (!NetWorkLauncher.instance.CanJoinRoomFromInvite(
                    roomName,
                    out string failKor,
                    out string failEng))
            {
                ToastMessageManager.instance?.ShowToast(
                    failKor,
                    failEng
                );

                // 이 초대는 더 이상 입장할 수 없는 초대이므로 expired 처리
                // 그래야 같은 초대 패널이 계속 다시 뜨지 않음
                await RoomInviteService.SetInviteStatusAsync(
                    inviteToJoin,
                    "expired"
                );

                _currentInvite = null;

                if (root != null)
                    root.SetActive(false);

                return;
            }

            // =========================
            // 입장 가능할 때만 accepted 처리
            // =========================
            // roomInvites와 roomInviteOutbox 양쪽 모두 accepted로 갱신
            await RoomInviteService.SetInviteStatusAsync(
                inviteToJoin,
                "accepted"
            );

            _currentInvite = null;

            if (root != null)
                root.SetActive(false);

            // 초대받은 방으로 입장
            NetWorkLauncher.instance.JoinRoomFromInvite(roomName, mode);
        }
        catch (Exception e)
        {
            Debug.LogError($"[RoomInviteReceiver] Accept failed: {e}");

            ToastMessageManager.instance?.ShowToast(
                "방 초대 수락에 실패했습니다.",
                "Failed to accept room invitation."
            );
        }
    }

    /// <summary>
    /// 거절 버튼 클릭.
    /// 
    /// 처리 순서:
    /// 1. 초대 상태를 declined로 변경
    /// 2. 방장 쪽 Outbox도 declined로 같이 변경됨
    /// 3. 초대 패널 닫기
    /// </summary>
    private async void OnClickDecline()
    {
        if (_currentInvite == null) return;

        try
        {
            // roomInvites와 roomInviteOutbox 양쪽 모두 declined로 갱신
            await RoomInviteService.SetInviteStatusAsync(
                _currentInvite,
                "declined"
            );
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[RoomInviteReceiver] Decline failed: {e.Message}");
        }

        _currentInvite = null;

        if (root != null)
            root.SetActive(false);
    }

    /// <summary>
    /// 수락/거절 버튼 텍스트를 현재 언어에 맞게 갱신한다.
    /// </summary>
    private void RefreshButtonTexts()
    {
        var lang = LaguageManager.Instance != null
            ? LaguageManager.Instance.currentLang
            : Lauaguage.Kor;

        if (btnAccept != null)
        {
            var t = btnAccept.GetComponentInChildren<TMP_Text>();
            if (t != null)
                t.text = lang == Lauaguage.Kor ? "수락" : "Accept";
        }

        if (btnDecline != null)
        {
            var t = btnDecline.GetComponentInChildren<TMP_Text>();
            if (t != null)
                t.text = lang == Lauaguage.Kor ? "거절" : "Decline";
        }
    }

    /// <summary>
    /// object 값을 int로 안전하게 변환한다.
    /// Firebase 값이 null이거나 숫자 변환에 실패하면 기본값을 반환한다.
    /// </summary>
    private int TryInt(object value, int def = 0)
    {
        if (value == null) return def;
        if (int.TryParse(value.ToString(), out int result)) return result;
        return def;
    }

    /// <summary>
    /// object 값을 long으로 안전하게 변환한다.
    /// Firebase Timestamp나 expireAt 값을 읽을 때 사용한다.
    /// </summary>
    private long TryLong(object value)
    {
        if (value == null) return 0;
        if (long.TryParse(value.ToString(), out long result)) return result;
        return 0;
    }
}