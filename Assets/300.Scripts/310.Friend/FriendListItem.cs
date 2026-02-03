using Gpm.Ui;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;


[Serializable]
public class FriendListItemData : InfiniteScrollData
{
    public enum RowMode { InviteCandidate, Friend }
    public enum InviteState { None, Outgoing, Incoming, AlreadyFriend }

    public RowMode mode;

    public string uid;      // 상대 uid
    public string nick;     // 표시 닉네임(또는 nickKey)
    public string photoUrl; // URL

    // 온라인 상태(친구일 때만 의미)
    public bool isOnline;

    public InviteState inviteState;
}

public static class FriendProfileImageCache
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
                Debug.LogWarning($"[FriendProfileImageCache] download fail: {req.error} / url={url}");
                return null;
            }

            var src = DownloadHandlerTexture.GetContent(req);
            if (src == null)
            {
                Debug.LogWarning($"[FriendProfileImageCache] GetContent null / url={url}");
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
                Debug.LogWarning($"[FriendProfileImageCache] clone fail: {e.Message} / url={url}");
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

public class FriendListItem : InfiniteScrollItem
{
    [Header("Common UI")]
    public Text txtNick;

    [Header("RawImage")]
    public RawImage imgPhoto;

    [Tooltip("인스펙터에 기본 프로필 텍스처 넣기(없을 때 표시)")]
    [SerializeField] private Texture _defaultSeed;

    [Header("Invite UI (InviteCandidate)")]
    public GameObject inviteRoot;
    public Button btnInvite;

    [Header("Delete UI (Friend)")]
    public GameObject deleteRoot;
    public Button btnDelete;

    [Header("Presence UI (Friend only)")]
    public Text txtStatus;       // "온라인" / "오프라인"

    [Header("Invite Freind Status")]
    public Text txtInviteState;

    [Header("Friend Actions (Dropdown)")]
    public TMP_Dropdown actionDropdown;          //  row 오른쪽 Dropdown 연결

    private string _boundUrl = "";
    private Texture _fallback; // 성공 다운로드 캐시를 셀 단위 fallback으로도 사용

    private enum FriendAction
    {
        None = 0,
        Chat = 1,
        Invite = 2,
        Profile = 3
    }


    public override void UpdateData(InfiniteScrollData scrollData)
    {
        base.UpdateData(scrollData);

        var d = (FriendListItemData)scrollData;

        if (txtNick) txtNick.text = d.nick ?? "";

        bool isInvite = d.mode == FriendListItemData.RowMode.InviteCandidate;
        bool isFriend = d.mode == FriendListItemData.RowMode.Friend;

        if (inviteRoot) inviteRoot.SetActive(isInvite);
        if (deleteRoot) deleteRoot.SetActive(isFriend);

        var lang = (LaguageManager.Instance != null)
           ? LaguageManager.Instance.currentLang
           : Lauaguage.Kor;

        if (isFriend)
        {
            ApplyLanaugeeOnLineOffLine(d, lang);
        }
        else
        {
            ApplyInviteStateUI(d, lang);
        }

        ApplyProfileImage(d.photoUrl, d.nick);

        if (btnInvite)
        {
            btnInvite.onClick.RemoveAllListeners();
            btnInvite.onClick.AddListener(() => FrinedUiManager.instance?.OnClickInviteButton(d));
            btnInvite.interactable = (d.inviteState == FriendListItemData.InviteState.None);
        }

        if (btnDelete)
        {
            btnDelete.onClick.RemoveAllListeners();
            btnDelete.onClick.AddListener(() => FrinedUiManager.instance?.OnClickDeleteButton(d));
        }

        ApplyActionDropdown(d, lang, isFriend);
    }


    private void ApplyActionDropdown(FriendListItemData d, Lauaguage lang, bool isFriendRow)
    {
        if (actionDropdown == null) return;

        // 초대 후보(InviteCandidate)에서는 액션 드롭다운 숨기고 싶으면:
        actionDropdown.gameObject.SetActive(isFriendRow);
        if (!isFriendRow) return;

        // 1) 옵션 텍스트(언어) 세팅
        BuildDropdownOptions(lang);

        // 2) 오프라인 정책
        // - 기본: 오프라인이면 드롭다운 클릭 자체 막기
        // - allowProfileWhenOffline=true면: 프로필만 허용 (선택 후 검사로 막음)
        actionDropdown.interactable = d.isOnline;

        // 3) 이벤트 바인딩(셀 재사용 때문에 항상 리셋)
        actionDropdown.onValueChanged.RemoveAllListeners();

        // 드롭다운은 “이전 선택값”이 남아있기 쉬워서 항상 0으로 초기화
        actionDropdown.SetValueWithoutNotify(0);

        actionDropdown.onValueChanged.AddListener((idx) =>
        {
            // idx: 0=선택, 1=채팅, 2=초대, 3=프로필
            if (idx == 0) return;

            var action = (FriendAction)idx;

            // 오프라인이면 채팅/초대는 금지 (프로필만 allowProfileWhenOffline이면 허용)
            if (!d.isOnline)
            {
                if (action == FriendAction.Chat || action == FriendAction.Invite)
                {
                    // 오프라인에서 눌렀으면 무시 + 토스트
                    ToastMessageManager.instance?.ShowToast("오프라인 상태입니다.", "User is offline.");
                    actionDropdown.SetValueWithoutNotify(0);
                    return;
                }
            }

            // 매니저에게 액션 넘기기
            FrinedUiManager.instance?.OnFriendActionSelected(d, (int)action);

            // 선택 후 다시 기본값으로
            actionDropdown.SetValueWithoutNotify(0);
        });
    }

    private void BuildDropdownOptions(Lauaguage lang)
    {
        if (actionDropdown == null) return;

        // 매 프레임/매 UpdateData마다 중복으로 AddOption하면 옵션이 계속 늘어나니까
        // 항상 ClearOptions 후 AddOptions
        actionDropdown.ClearOptions();

        var opts = new List<string>();
        if (lang == Lauaguage.Kor)
        {
            opts.Add("선택");
            opts.Add("채팅하기");
            opts.Add("친구초대");
            opts.Add("프로필 보기");
        }
        else
        {
            opts.Add("Select");
            opts.Add("Chat");
            opts.Add("Invite");
            opts.Add("View Profile");
        }

        actionDropdown.AddOptions(opts);
    }

    void ApplyLanaugeeOnLineOffLine(FriendListItemData d, Lauaguage lang)
    {
        switch (lang)
        {
            case Lauaguage.Kor:
                if (d.isOnline)
                {
                    txtStatus.text = "온라인";
                    //btnChatting.gameObject.SetActive(true);
                }
                else
                {
                    txtStatus.text = "오프라인";
                    //btnChatting.gameObject.SetActive(false);
                }
                break;
            case Lauaguage.Eng:
                if (d.isOnline)
                {
                    txtStatus.text = "OnLine";
                    //btnChatting.gameObject.SetActive(true);
                }
                else
                {
                    txtStatus.text = "OffLine";
                    //btnChatting.gameObject.SetActive(false);
                }
                break;
        }
    }

    private void ApplyProfileImage(string url, string nick)
    {
        if (imgPhoto == null) return;

        url = (url ?? "").Trim();


        Debug.Log($"[FriendListItem] nick={nick} url='{url}' seedNull={_defaultSeed == null}");

        // 셀 재사용 시, 다른 url로 바뀌면 기본값으로 리셋
        if (_boundUrl != url)
        {
            _boundUrl = url;
            _fallback = _defaultSeed;
            imgPhoto.texture = _fallback; // 먼저 기본값 깔기
        }

        // URL이 비어도 “무조건 보여야” 하니까 기본이미지 유지
        if (string.IsNullOrWhiteSpace(url))
        {
            imgPhoto.texture = _fallback;
            return;
        }

        // 이미 캐시에 있으면 즉시 적용
        if (FriendProfileImageCache.TryGetCached(url, out var cached) && cached != null)
        {
            _fallback = cached;
            imgPhoto.texture = cached;
            return;
        }

        // 없으면 비동기 다운로드
        _ = CoApplyProfileAsync(url);
    }

    private async Task CoApplyProfileAsync(string url)
    {
        var tex = await FriendProfileImageCache.GetAsync(url);

        if (this == null) return;
        if (!gameObject.activeInHierarchy) return;
        if (_boundUrl != url) return; // 셀 재사용 보호

        if (tex == null)
        {
            // 실패하면 fallback 유지
            imgPhoto.texture = _fallback;
            return;
        }

        _fallback = tex;
        imgPhoto.texture = tex;
    }

    private void ApplyInviteStateUI(FriendListItemData d, Lauaguage lang)
    {
        if (txtInviteState == null) return;

        switch (d.inviteState)
        {
            case FriendListItemData.InviteState.Outgoing:
                txtInviteState.text = (lang == Lauaguage.Kor) ? "초대중" : "Inviting";
                break;
            case FriendListItemData.InviteState.Incoming:
                txtInviteState.text = (lang == Lauaguage.Kor) ? "요청 옴" : "Request received";
                break;
            default:
                txtInviteState.text = (lang == Lauaguage.Kor) ? "초대" : "Invite";
                break;
        }
    }
}
