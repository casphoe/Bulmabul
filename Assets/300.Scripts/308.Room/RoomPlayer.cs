using Fusion;
using Gpm.Ui;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

[Serializable]
public class RoomPlayerData : InfiniteScrollData
{
    public int slotIndex;
    public bool isEmpty;

    public PlayerRef player;

    public string nickname;
    public string name;
    public int level;

    public bool isLeader;
    public bool isReady;

    public bool isMe;

    //프로필 URL
    public string photoUrl;
}

// ===== URL -> Texture 캐시(스크롤/리프레시로 인한 중복다운로드 방지) =====
public static class RoomProfileImageCache
{
    private static readonly Dictionary<string, Texture2D> _cache = new();
    private static readonly Dictionary<string, Task<Texture2D>> _inflight = new();

    public static bool TryGetCached(string url, out Texture2D tex)
    {
        url = (url ?? "").Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            tex = null;
            return false;
        }

        if (_cache.TryGetValue(url, out tex) && tex != null)
            return true;

        tex = null;
        return false;
    }

    public static async Task<Texture2D> GetAsync(string url)
    {
        url = (url ?? "").Trim();
        if (string.IsNullOrWhiteSpace(url)) return null;

        if (_cache.TryGetValue(url, out var tex) && tex != null)
            return tex;

        if (_inflight.TryGetValue(url, out var task) && task != null)
            return await task;

        var newTask = DownloadAndCloneAsync(url);
        _inflight[url] = newTask;

        try
        {
            tex = await newTask;
            if (tex != null) _cache[url] = tex;
            return tex;
        }
        finally
        {
            _inflight.Remove(url);
        }
    }

    private static async Task<Texture2D> DownloadAndCloneAsync(string url)
    {
        using (var req = UnityWebRequestTexture.GetTexture(url, false))
        {
            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[RoomProfileImageCache] download fail: {req.error} / url={url}");
                return null;
            }

            var src = DownloadHandlerTexture.GetContent(req);
            if (src == null)
            {
                Debug.LogWarning($"[RoomProfileImageCache] GetContent null / url={url}");
                return null;
            }

            var dst = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            try
            {
                dst.SetPixels32(src.GetPixels32());
                dst.Apply();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RoomProfileImageCache] clone fail: {e.Message} / url={url}");
                UnityEngine.Object.Destroy(dst);
                return null;
            }

            return dst;
        }
    }

    public static void ClearAll()
    {
        foreach (var kv in _cache)
        {
            if (kv.Value != null)
                UnityEngine.Object.Destroy(kv.Value);
        }
        _cache.Clear();
        _inflight.Clear();
    }
}

//방 플레이어 정보
// 리더인지 알 수 있어야하고, 닉네임 정보, 레벨
public class RoomPlayer : InfiniteScrollItem
{
    [Header("UI")]
    public Text txtSlot;
    public Text txtNick;
    public Text txtLevel;
    public Text txtName;
    public Text txtMe;

    [Header("기본 프로필(인스펙터에 넣는 기본값)")]
    [SerializeField] private Texture _defaultSeed;   //  인스펙터용 기본 이미지

    [Header("런타임 fallback (성공한 다운로드를 여기에 저장)")]
    public Texture defaultProfileTexture;

    public RawImage profileImage;

    [Header("UI Icons (or GameObjects)")]
    public GameObject leaderIcon;   // 리더 이미지 오브젝트

    [Header("버튼")]
    public Button btnMakeLeader;

    public Button btnKick;

    private RoomPlayerData d;
    private string _boundUrl = ""; //아이템 재활용 대비

    public override void UpdateData(InfiniteScrollData scrollData)
    {
        base.UpdateData(scrollData);
        d = (RoomPlayerData)scrollData;

        // 언어
        var lang = (LaguageManager.Instance != null)
            ? LaguageManager.Instance.currentLang
            : Lauaguage.Kor;

        // 빈 슬롯 처리
        if (d.isEmpty)
        {
            if (txtNick != null) txtNick.text = "-";
            if (txtLevel != null) txtLevel.text = "";
            if (txtName != null) txtName.text = "";
            if (txtMe != null) txtMe.text = "";
            SetActiveSafe(leaderIcon, false);
            defaultProfileTexture = _defaultSeed;          //  빈 슬롯은 기본값으로
            if (profileImage != null) profileImage.texture = _defaultSeed; // or null
            return;
        }


        if (txtNick != null)
        {
            if (lang == Lauaguage.Kor) txtNick.text = $"닉네임 : {d.nickname}";
            else txtNick.text = $"NickName : {d.nickname}";
        }

        // 레벨 텍스트
        if (txtLevel != null)
        {
            if (lang == Lauaguage.Kor) txtLevel.text = $"레벨 {d.level}";
            else txtLevel.text = $"Lv.{d.level}";
        }

        SetActiveSafe(leaderIcon, d.isLeader);

        if (txtName != null)
        {
            if (lang == Lauaguage.Kor) txtName.text = $"이름 : {d.name}";
            else txtName.text = $"Name : {d.name}";
        }

        if (txtMe != null) txtMe.text = d.isMe ? "ME" : "";

        ApplyProfileImage(d.photoUrl);


        bool amLeader = false;
        var members = RoomMembersState.Instance;
        if (members != null && members.Runner != null && members.Leader != PlayerRef.None)
        {
            amLeader = (members.Runner.LocalPlayer == members.Leader);
        }

        bool canGive = amLeader && !d.isEmpty && !d.isMe && d.player != PlayerRef.None;

        bool canKick = amLeader && !d.isEmpty && !d.isMe && d.player != PlayerRef.None;

        ApplyButtons(canGive, canKick);
    }

    private void ApplyProfileImage(string url)
    {
        if (profileImage == null) return;

        url = (url ?? "").Trim();

        // 다른 사람(다른 url)로 셀이 재사용되면 fallback을 기본값으로 리셋
        if (_boundUrl != url)
        {
            _boundUrl = url;
            defaultProfileTexture = _defaultSeed;
            profileImage.texture = defaultProfileTexture; // 일단 기본값 깔고 시작
        }

        // url 없으면 기본값 유지
        if (string.IsNullOrWhiteSpace(url))
        {
            profileImage.texture = defaultProfileTexture;
            return;
        }

        //  캐시에 이미 있으면 즉시 적용 + defaultProfileTexture에도 저장
        if (RoomProfileImageCache.TryGetCached(url, out var cached) && cached != null)
        {
            defaultProfileTexture = cached;
            profileImage.texture = cached;
            return;
        }

        //  없으면 비동기 다운로드
        _ = CoApplyProfileAsync(url);
    }

    private async Task CoApplyProfileAsync(string url)
    {
        var tex = await RoomProfileImageCache.GetAsync(url);

        if (this == null) return;
        if (!gameObject.activeInHierarchy) return;
        if (_boundUrl != url) return; //  재사용된 셀 보호

        if (tex == null)
        {
            Debug.LogWarning($"[RoomPlayer] profile download returned null. keep defaultProfileTexture. url={url}");
            profileImage.texture = defaultProfileTexture; // null이면 fallback 유지
            return;
        }

        // 성공하면 “defaultProfileTexture에 저장” + 적용
        defaultProfileTexture = tex;
        profileImage.texture = tex;
    }


    private void ApplyButtons(bool canGive, bool canKick)
    {
        if (btnMakeLeader != null)
        {
            btnMakeLeader.gameObject.SetActive(canGive);
            btnMakeLeader.onClick.RemoveAllListeners();
            if (canGive) btnMakeLeader.onClick.AddListener(OnClickMakeLeader);
        }

        if (btnKick != null)
        {
            btnKick.gameObject.SetActive(canKick);
            btnKick.onClick.RemoveAllListeners();
            if (canKick) btnKick.onClick.AddListener(OnClickKick);
        }
    }

    private void SetActiveSafe(GameObject go, bool on)
    {
        if (go != null && go.activeSelf != on)
            go.SetActive(on);
    }

    private void OnClickMakeLeader()
    {
        var members = RoomMembersState.Instance;
        if (members == null) return;

        members.RPC_RequestTransferLeader(d.player);
    }

    private void OnClickKick()
    {
        var members = RoomMembersState.Instance;
        if (members == null) return;

        members.RPC_RequestKick(d.player);
    }
}
