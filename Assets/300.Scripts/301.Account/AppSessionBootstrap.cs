using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 앱 전체 실행 동안 유지되는 세션 부트스트랩
/// - 앱 시작 시 세션성 static 상태 초기화
/// - 씬 이동 후에도 유지
/// - 강제 종료는 OnApplicationQuit가 보장되지 않으므로,
///   다음 앱 시작 시 Awake에서 다시 초기화하는 구조로 안전성 확보
/// </summary>
public class AppSessionBootstrap : MonoBehaviour
{
    private static AppSessionBootstrap _instance;
    private static bool _bootstrapped;

    private void Awake()
    {
        // 중복 생성 방지
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        // 앱 실행 후 첫 1회만 초기화
        if (_bootstrapped) return;
        _bootstrapped = true;

        AttendanceLoginSessionBridge.ResetSession();
    }

    private void OnApplicationQuit()
    {
        // 정상 종료 시 정리
        AttendanceLoginSessionBridge.ResetSession();
        _bootstrapped = false;
        _instance = null;
    }
}
