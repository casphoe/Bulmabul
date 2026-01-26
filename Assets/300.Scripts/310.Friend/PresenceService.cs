using Firebase.Auth;
using Firebase.Database;
using UnityEngine;
using System;
using System.Threading.Tasks;

public class PresenceService : MonoBehaviour
{
    private DatabaseReference _myPresenceRef;
    private DatabaseReference _connectedRef;
    private bool _started;

    // 연결 이벤트 핸들러를 필드로 잡아두면 OnDisable에서 해제 가능
    private EventHandler<ValueChangedEventArgs> _onConnectedHandler;


    /// <summary>
    /// Presence(온라인/오프라인) 시스템 시작
    /// - .info/connected 가 true가 되는 순간:
    ///   1) 연결이 끊길 때(OnDisconnect) online=false, lastSeen 저장 예약
    ///   2) 현재는 online=true, lastSeen 갱신
    ///
    /// 주의:
    /// - OnDisconnect는 "DB 경로"에 걸어야 함.
    ///   presence/{uid} 전체가 아니라 presence/{uid}/online, presence/{uid}/lastSeen 각각에 OnDisconnect를 건다.
    /// - SDK 버전에 따라 SetValueAsync가 없을 수 있음 -> SetValue(...) 사용 (대부분 Task 반환)
    /// </summary>
    public async Task StartPresenceAsync()
    {
        var user = FirebaseAuth.DefaultInstance.CurrentUser;
        if (user == null) return;
        if (_started) return;
        _started = true;

        string uid = user.UserId;

        _myPresenceRef = FirebaseDatabase.DefaultInstance.GetReference($"presence/{uid}");
        _connectedRef = FirebaseDatabase.DefaultInstance.GetReference(".info/connected");

        _onConnectedHandler = async (s, e) =>
        {
            if (e.DatabaseError != null) return;
            if (e.Snapshot == null || !e.Snapshot.Exists) return;

            bool connected = false;
            if (e.Snapshot.Value is bool b) connected = b;
            else bool.TryParse(e.Snapshot.Value?.ToString(), out connected);

            if (!connected) return;

            await SetConnectedPresenceAsync();
        };

        _connectedRef.ValueChanged += _onConnectedHandler;

        try
        {
            var snap = await _connectedRef.GetValueAsync();
            bool connectedNow = false;
            if (snap != null && snap.Exists)
            {
                if (snap.Value is bool b) connectedNow = b;
                else bool.TryParse(snap.Value?.ToString(), out connectedNow);
            }

            if (connectedNow)
                await SetConnectedPresenceAsync();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Presence] initial connected check failed: {ex.Message}");
        }
    }

    private async Task SetConnectedPresenceAsync()
    {
        if (_myPresenceRef == null) return;

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        try
        {
            var onlineRef = _myPresenceRef.Child("online");
            var lastSeenRef = _myPresenceRef.Child("lastSeen");

            await onlineRef.OnDisconnect().SetValue(false);
            await lastSeenRef.OnDisconnect().SetValue(now);

            await onlineRef.SetValueAsync(true);
            await lastSeenRef.SetValueAsync(now);

            Debug.Log($"[Presence] set ONLINE uid={FirebaseAuth.DefaultInstance.CurrentUser?.UserId} now={now}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Presence] SetConnectedPresenceAsync failed: {ex}");
        }
    }

    private void OnDisable()
    {
        if (_connectedRef != null && _onConnectedHandler != null)
            _connectedRef.ValueChanged -= _onConnectedHandler;
    }

    private async void OnApplicationQuit()
    {
        await SetOnlineAsync(false);
    }

    private async Task SetOnlineAsync(bool online)
    {
        if (_myPresenceRef == null) return;

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        try
        {
            await _myPresenceRef.Child("online").SetValueAsync(online);
            await _myPresenceRef.Child("lastSeen").SetValueAsync(now);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Presence] SetOnlineAsync failed: {ex.Message}");
        }
    }
}
