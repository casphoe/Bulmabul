using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Gpm.Ui;
using System;

public class RoomPrefabData : InfiniteScrollData
{
    public int index = 0;
    public int number = 0;
}
[Serializable]
public class RoomPrefab : InfiniteScrollItem
{
    public Image mapImage;

    public Sprite[] mapSprite;

    public Text[] txtData;
    public int listIndex = 0;

    public override void UpdateData(InfiniteScrollData scrollData)
    {
        base.UpdateData(scrollData);

        RoomPrefabData itemData = (RoomPrefabData)scrollData;

        var launcher = NetWorkLauncher.instance;
        if (launcher == null) return;
        // index로 세션 찾기
        if (itemData.index < 0 || itemData.index >= launcher.CachedSessions.Count) return;
        var s = launcher.CachedSessions[itemData.index];

        // 프로퍼티에서 mode/max 읽기
        MatchMode mode = MatchMode.Solo;
        int max = 0;
        if (s.Properties != null)
        {
            if (s.Properties.TryGetValue("mode", out var pm)) mode = (MatchMode)(int)pm;
            if (s.Properties.TryGetValue("max", out var px)) max = (int)px;
        }

        int cur = s.PlayerCount; // Fusion SessionInfo에 보통 존재
        txtData[1].text = s.Name;
        switch(LaguageManager.Instance.currentLang)
        {
            case Lauaguage.Kor:
                txtData[2].text = mode == MatchMode.Team ? "팀전" : "개인전";
                break;
            case Lauaguage.Eng:
                txtData[2].text = mode == MatchMode.Team ? "TEAM" : "SOLO";
                break;
        }
        txtData[3].text = $"{cur}/{max}";
    }

    public void RoomJointBtnClick()
    {

    }

    public void OnClick()
    {
        OnSelect();
    }
}
