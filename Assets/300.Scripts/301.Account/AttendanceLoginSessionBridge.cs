using System.Threading.Tasks;
using UnityEngine;

public static class AttendanceLoginSessionBridge
{
    private static bool _handledThisLoginSession = false;
    private static bool _pendingOpenUi = false;
    private static AttendanceService.AttendanceClaimResult _cachedResult = null;

    public static async Task HandleAfterLoginAsync(Account acc)
    {
        Debug.Log("[AttendanceBridge] HandleAfterLoginAsync called");

        if (acc == null)
        {
            Debug.LogWarning("[AttendanceBridge] acc is null");
            return;
        }

        if (_handledThisLoginSession)
        {
            Debug.LogWarning("[AttendanceBridge] already handled this session");
            return;
        }

        _handledThisLoginSession = true;

        var status = AttendanceService.GetStatus(acc);
        if (status == null)
        {
            Debug.LogWarning("[AttendanceBridge] status is null");
            return;
        }

        Debug.Log($"[AttendanceBridge] status.IsClaimedToday = {status.IsClaimedToday}");
        Debug.Log($"[AttendanceBridge] status.TodayString = {status.TodayString}");
        Debug.Log($"[AttendanceBridge] status.AttendanceCountThisMonth = {status.AttendanceCountThisMonth}");

        if (status.IsClaimedToday)
        {
            Debug.LogWarning("[AttendanceBridge] already claimed today -> no pending UI");
            _pendingOpenUi = false;
            _cachedResult = null;
            return;
        }

        _cachedResult = await AttendanceService.TryClaimTodayAsync(acc, saveImmediately: true);

        Debug.Log($"[AttendanceBridge] claim result null? {_cachedResult == null}");
        Debug.Log($"[AttendanceBridge] claim success? {_cachedResult != null && _cachedResult.Claimed}");

        if (_cachedResult != null && _cachedResult.Claimed)
        {
            _pendingOpenUi = true;
            Debug.Log("[AttendanceBridge] pending UI set TRUE");
        }
        else
        {
            _pendingOpenUi = false;
            Debug.LogWarning("[AttendanceBridge] claim failed, pending UI FALSE");
        }
    }

    public static bool TryConsumePendingResult(out AttendanceService.AttendanceClaimResult result)
    {
        Debug.Log($"[AttendanceBridge] TryConsumePendingResult before => pending:{_pendingOpenUi}, cachedNull:{_cachedResult == null}");

        result = null;

        if (!_pendingOpenUi || _cachedResult == null)
            return false;

        result = _cachedResult;
        _pendingOpenUi = false;
        _cachedResult = null;

        Debug.Log("[AttendanceBridge] TryConsumePendingResult success");
        return true;
    }

    public static void ResetSession()
    {
        Debug.Log("[AttendanceBridge] ResetSession called");
        _handledThisLoginSession = false;
        _pendingOpenUi = false;
        _cachedResult = null;
    }
}