using Gpm.Ui;
using System;
using System.Reflection;

public static class GpmInfiniteScrollCompat
{
    public static void ClearData(InfiniteScroll scroll)
    {
        if (scroll == null) return;
        scroll.ClearData();
    }

    public static void InsertData(InfiniteScroll scroll, InfiniteScrollData data, int index)
    {
        if (scroll == null || data == null) return;

        // InsertData(data, index, bool) 우선
        var t = scroll.GetType();
        var m = t.GetMethod("InsertData", new[] { typeof(InfiniteScrollData), typeof(int), typeof(bool) });
        if (m != null)
        {
            m.Invoke(scroll, new object[] { data, index, true });
            return;
        }

        // InsertData(data, index)
        m = t.GetMethod("InsertData", new[] { typeof(InfiniteScrollData), typeof(int) });
        if (m != null)
        {
            m.Invoke(scroll, new object[] { data, index });
            return;
        }

        throw new MissingMethodException("InfiniteScroll.InsertData signature not found.");
    }

    public static void UpdateAllData(InfiniteScroll scroll)
    {
        if (scroll == null) return;

        var t = scroll.GetType();

        MethodInfo m =
            t.GetMethod("UpdateAllData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
            t.GetMethod("UpdateAllItems", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
            t.GetMethod("UpdateAll", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (m == null) return;

        var ps = m.GetParameters();

        // 0개면 그냥 호출
        if (ps.Length == 0)
        {
            m.Invoke(scroll, null);
            return;
        }

        // 1개 bool이면 true 넣어서 호출(대부분 강제 리프레시 의미)
        if (ps.Length == 1 && ps[0].ParameterType == typeof(bool))
        {
            m.Invoke(scroll, new object[] { true });
            return;
        }

        // 그 외는 호출하지 않음(버전/시그니처 예외 방어)
       
    }
}