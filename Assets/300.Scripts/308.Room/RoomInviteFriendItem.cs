using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RoomInviteFriendItem : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private RawImage profileImage;
    [SerializeField] private TMP_Text txtNick;
    [SerializeField] private TMP_Text txtLevel;
    [SerializeField] private TMP_Text txtOnline;
    [SerializeField] private Button btnInvite;

    [Header("Default")]
    [SerializeField] private Texture defaultProfileTexture;

    private FriendRow _row;
    private string _boundUrl = "";

    public void SetData(FriendRow row, Action<FriendRow, RoomInviteFriendItem> onInvite)
    {
        _row = row;

        if (txtNick != null)
            txtNick.text = row.nick;

        if (txtLevel != null)
            txtLevel.text = $"Lv.{row.accountLevel}";

        if (txtOnline != null)
            txtOnline.text = "온라인";

        if (btnInvite != null)
        {
            btnInvite.interactable = true;
            btnInvite.onClick.RemoveAllListeners();
            btnInvite.onClick.AddListener(() =>
            {
                onInvite?.Invoke(_row, this);
            });

            var t = btnInvite.GetComponentInChildren<TMP_Text>();
            if (t != null) t.text = "초대";
        }

        ApplyProfile(row.photoUrl);
    }

    public void SetInvited()
    {
        if (btnInvite == null) return;

        btnInvite.interactable = false;

        var t = btnInvite.GetComponentInChildren<TMP_Text>();
        if (t != null) t.text = "초대 완료";
    }

    private void ApplyProfile(string url)
    {
        if (profileImage == null) return;

        url = (url ?? "").Trim();

        if (_boundUrl != url)
        {
            _boundUrl = url;
            profileImage.texture = defaultProfileTexture;
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            profileImage.texture = defaultProfileTexture;
            return;
        }

        if (FriendProfileImageCache.TryGetCached(url, out var cached) && cached != null)
        {
            profileImage.texture = cached;
            return;
        }

        _ = ApplyProfileAsync(url);
    }

    private async System.Threading.Tasks.Task ApplyProfileAsync(string url)
    {
        var tex = await FriendProfileImageCache.GetAsync(url);

        if (this == null) return;
        if (!gameObject.activeInHierarchy) return;
        if (_boundUrl != url) return;

        profileImage.texture = tex != null ? tex : defaultProfileTexture;
    }
}