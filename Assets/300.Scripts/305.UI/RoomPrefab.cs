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

    public Text[] txtData;
    private int _sessionIndex = -1;
    public Sprite[] mapSprites;

    public override void UpdateData(InfiniteScrollData scrollData)
    {
        base.UpdateData(scrollData);

        RoomPrefabData itemData = (RoomPrefabData)scrollData;
        _sessionIndex = itemData.index;

        var launcher = NetWorkLauncher.instance;
        if (launcher == null) return;
        // index로 세션 찾기
        if (_sessionIndex < 0 || _sessionIndex >= launcher.CachedSessions.Count) return;
        var s = launcher.CachedSessions[_sessionIndex];

        // 프로퍼티에서 mode/max 읽기
        MatchMode mode = MatchMode.Solo;
        int max = s.MaxPlayers;
        if (s.Properties != null)
        {
            if (s.Properties.TryGetValue("mode", out var pm)) mode = (MatchMode)(int)pm;
            if (s.Properties.TryGetValue("max", out var px)) max = (int)px;
        }
        //map 읽기 (0=Korea, 1=USA)
        int map = 0;
        if (s.Properties != null && s.Properties.TryGetValue("map", out var pMap))
            map = (int)pMap;

        map = Mathf.Clamp(map, 0, 1);
        
        //이미지 세팅
        if (mapImage != null && mapSprites != null && mapSprites.Length > map)
            mapImage.sprite = mapSprites[map];

        switch (LaguageManager.Instance.currentLang)
        {
            case Lauaguage.Kor:
                txtData[0].text = (map == 0) ? "한국맵" : "미국맵";
                txtData[2].text = mode == MatchMode.Team ? "팀전" : "개인전";
                break;
            case Lauaguage.Eng:
                txtData[0].text = (map == 0) ? "KOREA" : "USA";
                txtData[2].text = mode == MatchMode.Team ? "TEAM" : "SOLO";
                break;
        }

        int cur = s.PlayerCount; // Fusion SessionInfo에 보통 존재
        txtData[1].text = s.Name;
        txtData[3].text = $"{cur}/{max}";
    }

    //참가 버튼
    public void RoomJointBtnClick()
    {
        var launcher = NetWorkLauncher.instance;
        if (launcher == null) return;


        // 혹시 최신 상태에서 꽉 찼는지 마지막 체크(레이스 방지)
        if (_sessionIndex < 0 || _sessionIndex >= launcher.CachedSessions.Count) return;

        var s = launcher.CachedSessions[_sessionIndex];
        int max = s.MaxPlayers;
        if (max <= 0 && s.Properties != null && s.Properties.TryGetValue("max", out var px)) max = (int)px;

        if (max > 0 && s.PlayerCount >= max)
        {
            return;
        }

        //참가
        launcher.JoinRoomByIndex(_sessionIndex);
    }

    public void OnClick()
    {
        OnSelect();
    }
}
