using Firebase.Database;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 친구들의 presence/{uid}를 실시간 구독해서 online/lastSeen 변화를 콜백으로 전달.
/// - StartWatching(uids, onChanged) 호출하면 필요한 uid만 구독
/// - StopAll()/OnDisable에서 해제
/// </summary>
public class FriendPresenceWatcher : MonoBehaviour
{
    public static FriendPresenceWatcher Instance { get; private set; }


    // uid -> (ref, handler)
    private readonly Dictionary<string, (DatabaseReference r, EventHandler<ValueChangedEventArgs> h)> _subs
    = new();


    // Unity 메인스레드에서 UI 안전하게 갱신하기 위해 큐 사용
    private readonly Queue<Action> _mainThreadQueue = new();
    private readonly object _queueLock = new();

    // 현재 콜백 (StartWatching을 다시 부르면 최신 콜백으로 갈아끼움)
    private Action<string, bool, long> _onChanged;


    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        // 메인스레드에서 콜백 실행
        while (true)
        {
            Action a = null;
            lock (_queueLock)
            {
                if (_mainThreadQueue.Count == 0) break;
                a = _mainThreadQueue.Dequeue();
            }


            try { a?.Invoke(); }
            catch (Exception e) { Debug.LogWarning(e); }
        }
    }

    public void StartWatching(IEnumerable<string> uids, Action<string, bool, long> onChanged)
    {
        if (uids == null) return;

        //최신 콜백으로 교체
        _onChanged = onChanged;

        var set = new HashSet<string>(uids);


        // 1) 필요 없는 구독 제거
        var toRemove = new List<string>();
        foreach (var kv in _subs)
            if (!set.Contains(kv.Key))
                toRemove.Add(kv.Key);


        foreach (var uid in toRemove)
            Unwatch(uid);


        // 2) 신규 구독 추가
        foreach (var uid in set)
        {
            if (string.IsNullOrWhiteSpace(uid)) continue;
            if (_subs.ContainsKey(uid)) continue;
            Watch(uid);
        }
    }


    private void Watch(string uid)
    {
        var r = FirebaseDatabase.DefaultInstance.GetReference($"presence/{uid}");


        EventHandler<ValueChangedEventArgs> h = (s, e) =>
        {
            if (e.DatabaseError != null) return;
            var snap = e.Snapshot;

            bool online = false;
            long lastSeen = 0;

            if (snap != null && snap.Exists)
            {
                online = TryBool(snap.Child("online").Value);
                lastSeen = TryLong(snap.Child("lastSeen").Value);
            }

            EnqueueMain(() => _onChanged?.Invoke(uid, online, lastSeen));
        };


        r.ValueChanged += h;
        _subs[uid] = (r, h);
    }


    private void Unwatch(string uid)
    {
        if (!_subs.TryGetValue(uid, out var sub)) return;
        try { sub.r.ValueChanged -= sub.h; } catch { }
        _subs.Remove(uid);
    }


    public void StopAll()
    {
        foreach (var kv in _subs)
        {
            try { kv.Value.r.ValueChanged -= kv.Value.h; } catch { }
        }
        _subs.Clear();

        lock (_queueLock) _mainThreadQueue.Clear();
        _onChanged = null;
    }

    private void OnDestroy() => StopAll();


    private void EnqueueMain(Action a)
    {
        if (a == null) return;
        lock (_queueLock)
        {
            _mainThreadQueue.Enqueue(a);
        }
    }


    private static bool TryBool(object v)
    {
        if (v == null) return false;
        if (v is bool b) return b;
        if (bool.TryParse(v.ToString(), out var bb)) return bb;
        if (int.TryParse(v.ToString(), out var n)) return n != 0;
        return false;
    }


    private static long TryLong(object v)
    {
        if (v == null) return 0;
        if (long.TryParse(v.ToString(), out var n)) return n;
        return 0;
    }
}
