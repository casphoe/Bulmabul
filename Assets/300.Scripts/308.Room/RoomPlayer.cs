using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Gpm.Ui;
using UnityEngine.UI;
using Fusion;

[Serializable]
public class RoomPlayerData : InfiniteScrollData
{
    public int slotIndex;
    public bool isEmpty;

    public PlayerRef player;

    public string nickname;
    public int level;

    public bool isLeader;
    public bool isReady;

    public bool isMe;
}

//방 플레이어 정보
// 리더인지 알 수 있어야하고, 닉네임 정보, 레벨
public class RoomPlayer : InfiniteScrollItem
{
    [Header("UI")]
    public Text txtSlot;
    public Text txtNick;
    public Text txtLevel;
    public Text txtReady;
    public Text txtMe;

    [Header("UI Icons (or GameObjects)")]
    public GameObject leaderIcon;   // 리더 이미지 오브젝트

    [Header("버튼")]
    public Button btnMakeLeader;

    public Button btnKick;

    private RoomPlayerData d;

    public override void UpdateData(InfiniteScrollData scrollData)
    {
        base.UpdateData(scrollData);
        d = (RoomPlayerData)scrollData;

        // 언어
        var lang = (LaguageManager.Instance != null)
            ? LaguageManager.Instance.currentLang
            : Lauaguage.Kor;


        // 슬롯 텍스트
        if (txtSlot != null)
        {
            if (lang == Lauaguage.Kor) txtSlot.text = $"플레이어 {d.slotIndex + 1}";
            else txtSlot.text = $"Player {d.slotIndex + 1}";
        }

        // 빈 슬롯 처리
        if (d.isEmpty)
        {
            if (txtNick != null) txtNick.text = "-";
            if (txtLevel != null) txtLevel.text = "";
            if (txtReady != null) txtReady.text = "";
            if (txtMe != null) txtMe.text = "";
            SetActiveSafe(leaderIcon, false);
            return;
        }


        if (txtNick != null) txtNick.text = d.nickname;

        // 레벨 텍스트
        if (txtLevel != null)
        {
            if (lang == Lauaguage.Kor) txtLevel.text = $"레벨 {d.level}";
            else txtLevel.text = $"Lv.{d.level}";
        }

        SetActiveSafe(leaderIcon, d.isLeader);

        if (txtReady != null)
        {
            if (lang == Lauaguage.Kor) txtReady.text = "준비";
            else txtReady.text = "READY";
        }

        if (txtMe != null) txtMe.text = d.isMe ? "ME" : "";

        bool amLeader = (RoomState.instance != null &&
                     RoomMembersState.Instance != null &&
                     RoomState.instance.Leader != PlayerRef.None &&
                     RoomMembersState.Instance.Runner.LocalPlayer == RoomState.instance.Leader);

        bool canGive = amLeader && !d.isEmpty && !d.isMe && d.player != PlayerRef.None;

        bool canKick = amLeader && !d.isEmpty && !d.isMe && d.player != PlayerRef.None;

        if (btnMakeLeader != null)
        {
            btnMakeLeader.gameObject.SetActive(canGive);
            btnMakeLeader.onClick.RemoveAllListeners();

            if (canGive)
                btnMakeLeader.onClick.AddListener(OnClickMakeLeader);
        }

        if (btnKick != null)
        {
            btnKick.gameObject.SetActive(canKick);
            btnKick.onClick.RemoveAllListeners();
            if (canKick)
                btnKick.onClick.AddListener(OnClickKick);
        }
    }

    private void SetActiveSafe(GameObject go, bool on)
    {
        if (go != null && go.activeSelf != on)
            go.SetActive(on);
    }

    private void OnClickMakeLeader()
    {
        var roomState = RoomState.instance;
        var members = RoomMembersState.Instance;
        if (roomState == null || members == null) return;

        var me = members.Runner.LocalPlayer;
        roomState.RPC_RequestTransferLeader(me, d.player);
    }

    private void OnClickKick()
    {
        var roomState = RoomState.instance;
        var members = RoomMembersState.Instance;
        if (roomState == null || members == null) return;

        var me = members.Runner.LocalPlayer;
        roomState.RPC_RequestKick(me, d.player);
    }
}
