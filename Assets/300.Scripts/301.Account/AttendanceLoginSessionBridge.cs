using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 로그인 직후 출석 처리를 1회만 수행하고,
/// 출석 성공 결과를 UI에서 나중에 꺼내 쓸 수 있도록 임시 저장해두는 브리지 클래스
/// </summary>
public static class AttendanceLoginSessionBridge
{
    /// <summary>
    /// 현재 로그인 세션에서 이미 출석 처리를 시도했는지 여부
    /// true이면 같은 로그인 세션에서는 중복 처리하지 않음
    /// </summary>
    private static bool _handledThisLoginSession = false;

    /// <summary>
    /// 출석 성공 후 UI를 열어야 하는지 여부
    /// true이면 UI 쪽에서 결과를 소비할 수 있는 상태
    /// </summary>
    private static bool _pendingOpenUi = false;

    /// <summary>
    /// 출석 처리 결과를 임시로 저장해두는 캐시
    /// UI가 열릴 때 이 값을 꺼내 보상 내용을 표시할 수 있음
    /// </summary>
    private static AttendanceService.AttendanceClaimResult _cachedResult = null;

    /// <summary>
    /// 로그인 직후 호출되어 오늘 출석이 아직 안 된 경우 자동 출석 처리 수행
    /// 한 로그인 세션에서는 1회만 실행되며,
    /// 성공 시 결과를 캐시에 저장하고 UI 오픈 대기 상태로 전환함
    /// </summary>
    /// <param name="acc">현재 로그인한 계정 데이터</param>
    public static async Task HandleAfterLoginAsync(Account acc)
    {
        Debug.Log("[AttendanceBridge] HandleAfterLoginAsync called");

        // 계정 정보가 없으면 출석 처리 불가
        if (acc == null)
        {
            Debug.LogWarning("[AttendanceBridge] acc is null");
            return;
        }

        // 이미 이번 로그인 세션에서 처리했다면 중복 실행 방지
        if (_handledThisLoginSession)
        {
            Debug.LogWarning("[AttendanceBridge] already handled this session");
            return;
        }

        // 이번 세션에서 처리 시작 표시
        _handledThisLoginSession = true;

        // 현재 출석 상태 조회
        var status = AttendanceService.GetStatus(acc);
        if (status == null)
        {
            Debug.LogWarning("[AttendanceBridge] status is null");
            return;
        }

        // 오늘 이미 출석이 끝난 상태면 더 이상 처리하지 않음
        if (status.IsClaimedToday)
        {
            Debug.LogWarning("[AttendanceBridge] already claimed today -> no pending UI");
            _pendingOpenUi = false;
            _cachedResult = null;
            return;
        }

        // 오늘 출석 처리 시도
        // saveImmediately: true 이므로 성공 시 즉시 저장까지 수행
        _cachedResult = await AttendanceService.TryClaimTodayAsync(acc, saveImmediately: true);

        // 출석 성공 시 UI에서 결과를 보여줄 수 있도록 대기 상태 설정
        if (_cachedResult != null && _cachedResult.Claimed)
        {
            _pendingOpenUi = true;
            Debug.Log("[AttendanceBridge] pending UI set TRUE");
        }
        else
        {
            // 실패했으면 UI를 열지 않음
            _pendingOpenUi = false;
            Debug.LogWarning("[AttendanceBridge] claim failed, pending UI FALSE");
        }
    }

    /// <summary>
    /// 대기 중인 출석 결과가 있으면 반환하고,
    /// 반환과 동시에 내부 대기 상태와 캐시를 비움
    /// 즉, 한 번만 소비 가능한 구조
    /// </summary>
    /// <param name="result">UI에 전달할 출석 결과</param>
    /// <returns>소비할 결과가 있으면 true, 없으면 false</returns>
    public static bool TryConsumePendingResult(out AttendanceService.AttendanceClaimResult result)
    {
        Debug.Log($"[AttendanceBridge] TryConsumePendingResult before => pending:{_pendingOpenUi}, cachedNull:{_cachedResult == null}");

        result = null;

        // UI 오픈 대기 상태가 아니거나 캐시된 결과가 없으면 실패
        if (!_pendingOpenUi || _cachedResult == null)
            return false;

        // 결과 전달
        result = _cachedResult;

        // 한 번 전달한 뒤에는 다시 열리지 않도록 상태 초기화
        _pendingOpenUi = false;
        _cachedResult = null;

        Debug.Log("[AttendanceBridge] TryConsumePendingResult success");
        return true;
    }

    /// <summary>
    /// 로그인 세션 관련 출석 처리 상태를 초기화
    /// 로그아웃 후 재로그인하거나 새 세션 시작 시 다시 처리 가능하게 만듦
    /// </summary>
    public static void ResetSession()
    {
        Debug.Log("[AttendanceBridge] ResetSession called");
        _handledThisLoginSession = false;
        _pendingOpenUi = false;
        _cachedResult = null;
    }
}