using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Gpm.Ui;

public class RoomList : MonoBehaviour
{
    public InfiniteScroll roomScrollList;
    public GameObject roomListContent;

    private List<RoomPrefabData> dataList = new List<RoomPrefabData>();

    public int selectIndex = 0;
    public int index = 0;

    private void Awake()
    {
        roomScrollList.AddSelectCallback((data) =>
        {
            var d = (RoomPrefabData)data;
            if (NetWorkLauncher.instance != null)
                NetWorkLauncher.instance.selectindex = d.index; // CachedSessions 인덱스
        });
    }

    void RoomListClear()
    {
        dataList.Clear();
        roomScrollList.ClearData();
    }

    public void RoomLoadList()
    {
        var launcher = NetWorkLauncher.instance;
        if (launcher == null) return;

        //  1) 화면만 싹 비우기
        RoomListClear();

        // 2) “검색/정렬 결과” 리스트로만 다시 채우기
        var src = launcher.roomPrefabList;

        for (int i = 0; i < src.Count; i++)
        {
            // src[i].index = CachedSessions 인덱스(진짜 참가 인덱스)
            var d = new RoomPrefabData
            {
                index = src[i].index,
                number = i + 1
            };

            dataList.Add(d);
            roomScrollList.InsertData(d);
        }

        roomScrollList.UpdateAllData();
    }

    private void OnEnable()
    {
        AllUpdate();
    }

    void AllUpdate()
    {
        roomScrollList.UpdateAllData();
    }
}
