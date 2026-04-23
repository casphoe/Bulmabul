using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class FriendProfilePopup : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject root;
    [SerializeField] private Button btnClose;

    [Header("Texts")]
    [SerializeField] private TMP_Text txtNick;
    [SerializeField] private TMP_Text txtLevel;
    [SerializeField] private TMP_Text txtLastSeen;
    [SerializeField] private TMP_Text txtEquippedDice;

    private void Awake()
    {
        if (root != null) root.SetActive(false);
        if (btnClose != null) btnClose.onClick.AddListener(Close);
    }

    public async void Open(string friendUid)
    {
        if (string.IsNullOrWhiteSpace(friendUid)) return;

        if (root != null) root.SetActive(true);

        if (txtNick != null) txtNick.text = "불러오는 중...";
        if (txtLevel != null) txtLevel.text = "-";
        if (txtLastSeen != null) txtLastSeen.text = "-";
        if (txtEquippedDice != null) txtEquippedDice.text = "-";

        try
        {
            var data = await FriendService.GetFriendProfileAsync(friendUid);
            Apply(data);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[FriendProfilePopup] {e}");
            ToastMessageManager.instance?.ShowToast("친구 프로필을 불러오지 못했습니다.", "Failed to load friend profile.");
            Close();
        }
    }

    public void Close()
    {
        if (root != null) root.SetActive(false);
    }

    void Apply(FriendProfileData data)
    {
        if (data == null)
        {
            if (txtNick != null) txtNick.text = "-";
            if (txtLevel != null) txtLevel.text = "-";
            if (txtLastSeen != null) txtLastSeen.text = "-";
            if (txtEquippedDice != null) txtEquippedDice.text = "-";
            return;
        }

        if (txtNick != null)
            txtNick.text = string.IsNullOrWhiteSpace(data.nick) ? "-" : data.nick;

        if (txtLevel != null)
            txtLevel.text = $"Lv. {Mathf.Max(1, data.accountLevel)}";

        if (txtLastSeen != null)
            txtLastSeen.text = FormatLastSeen(data);

        if (txtEquippedDice != null)
            txtEquippedDice.text = FormatEquippedDice(data.equippedDiceKey);
    }

    string FormatLastSeen(FriendProfileData data)
    {
        if (data.isOnline) return "온라인";
        if (data.lastSeenUnix <= 0) return "기록 없음";

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(data.lastSeenUnix)
                .ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm");
        }
        catch
        {
            return "기록 없음";
        }
    }

    string FormatEquippedDice(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return "장착 안 함";

        string[] split = key.Split('|');
        if (split.Length >= 2)
            return $"{split[0]} / {split[1]}성";

        return key;
    }
}
