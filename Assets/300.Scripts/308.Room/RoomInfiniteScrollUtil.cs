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

    public static void Insert(InfiniteScroll scroll, InfiniteScrollData data)
    {
        if (scroll == null || data == null) return;

        var t = scroll.GetType();
        var methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => m.Name == "InsertData").ToArray();

        foreach (var m in methods)
        {
            var p = m.GetParameters();
            if (p.Length == 1 && typeof(InfiniteScrollData).IsAssignableFrom(p[0].ParameterType))
            {
                m.Invoke(scroll, new object[] { data });
                return;
            }
        }

        foreach (var m in methods)
        {
            var p = m.GetParameters();
            if (p.Length == 3 &&
                typeof(InfiniteScrollData).IsAssignableFrom(p[0].ParameterType) &&
                p[1].ParameterType == typeof(int) &&
                p[2].ParameterType == typeof(bool))
            {
                m.Invoke(scroll, new object[] { data, 0, true });
                return;
            }
        }

        throw new MissingMethodException("InfiniteScroll.InsertData 시그니처를 찾지 못했습니다. GPM InfiniteScroll 버전을 확인하세요.");
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
