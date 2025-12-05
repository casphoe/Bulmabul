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
            NetWorkLauncher.instance.selectindex = ((RoomPrefabData)data).index;
            selectIndex = NetWorkLauncher.instance.selectindex;
        });
    }

    void RoomListClear()
    {
        dataList.Clear();
        roomScrollList.ClearData();

        NetWorkLauncher.instance.roomPrefabList.Clear();
        index = 0;
    }

    void InfinteScrollReboot()
    {
        int count = dataList.Count;
        for(int i = 0; i < count; i++)
        {
            RoomPrefabData data = dataList[i];
            data.index = i;
            data.number = i + 1;
        }
    }

    public void RoomLoadList()
    {
        RoomListClear();
        int count = NetWorkLauncher.instance.CachedSessions.Count;
        for (int i = 0; i < count; i++)
            RoomInsertData();
        AllUpdate();
    }

    void RoomInsertData()
    {
        RoomPrefabData data = new RoomPrefabData();
        data.index = index++;
        data.number = roomScrollList.GetItemCount() + 1;
        dataList.Add(data);
        roomScrollList.InsertData(data);

        NetWorkLauncher.instance.roomPrefabList.Add(data);
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
