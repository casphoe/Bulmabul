using System;
using System.Linq;
using System.Reflection;
using Gpm.Ui;

public static class RoomInfiniteScrollUtil 
{
    public static void ClearAll(InfiniteScroll scroll)
    {
        if (scroll == null) return;
        InvokeNoArg(scroll, "ClearData", "RemoveAllData", "Clear", "ResetData", "ClearAllData");
    }

    public static void UpdateAll(InfiniteScroll scroll)
    {
        if (scroll == null) return;
        InvokeNoArg(scroll, "UpdateAllData", "Refresh", "Rebuild");
    }

    public static void Insert(InfiniteScroll scroll, InfiniteScrollData data, int insertIndex = -1)
    {
        if (scroll == null || data == null) return;

        var t = scroll.GetType();
        var methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => m.Name == "InsertData").ToArray();

        // 1) InsertData(data) 가 있으면 보통 "append"이므로 그걸 우선 사용
        foreach (var m in methods)
        {
            var p = m.GetParameters();
            if (p.Length == 1 && typeof(InfiniteScrollData).IsAssignableFrom(p[0].ParameterType))
            {
                m.Invoke(scroll, new object[] { data });
                return;
            }
        }

        // 2) InsertData(data, index, bool)만 있는 버전이면 index를 0이 아니라 "맨 뒤"로
        foreach (var m in methods)
        {
            var p = m.GetParameters();
            if (p.Length == 3 &&
                typeof(InfiniteScrollData).IsAssignableFrom(p[0].ParameterType) &&
                p[1].ParameterType == typeof(int) &&
                p[2].ParameterType == typeof(bool))
            {
                int idx = insertIndex;
                if (idx < 0) idx = GetDataCount(scroll); // ✅ 현재 개수 = 맨 뒤

                m.Invoke(scroll, new object[] { data, idx, true });
                return;
            }
        }

        throw new MissingMethodException("InfiniteScroll.InsertData 시그니처를 찾지 못했습니다. GPM InfiniteScroll 버전을 확인하세요.");
    }

    // 현재 데이터 개수 얻기 
    private static int GetDataCount(InfiniteScroll scroll)
    {
        var t = scroll.GetType();

        // 프로퍼티 시도
        var prop = t.GetProperty("DataCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
               ?? t.GetProperty("ItemCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (prop != null && prop.PropertyType == typeof(int))
            return (int)prop.GetValue(scroll);

        // 메서드 시도
        var mi = t.GetMethod("GetDataCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null)
              ?? t.GetMethod("GetItemCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);

        if (mi != null && mi.ReturnType == typeof(int))
            return (int)mi.Invoke(scroll, null);

        // 최후: 내부 리스트/배열 찾아 Count
        var fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (var f in fields)
        {
            var val = f.GetValue(scroll);
            if (val is System.Collections.ICollection col)
                return col.Count;
        }

        return 0;
    }

    private static void InvokeNoArg(object target, params string[] names)
    {
        var t = target.GetType();
        foreach (var n in names)
        {
            var mi = t.GetMethod(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mi == null) continue;
            if (mi.GetParameters().Length != 0) continue;
            mi.Invoke(target, null);
            return;
        }
    }
}
