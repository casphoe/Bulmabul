using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

public class DoubleClickTrigger : MonoBehaviour, IPointerClickHandler
{
    [Header("DoubleClick Settings")]
    public float maxInterval = 0.35f;
    public Action onDoubleClick;

    bool _open = false;

    //  1클릭이 들어온 상태인지
    bool _waitingSecond = false;

    Coroutine _timeoutCo;

    /// <summary>
    /// 팝업이 열릴 때 호출: 더블클릭을 새로 받기 시작
    /// </summary>
    public void Open()
    {
        _open = true;
        ResetStateInternal();
    }

    /// <summary>
    /// 팝업이 닫히거나, 더 이상 클릭을 받으면 안 될 때
    /// </summary>
    public void DisOpen()
    {
        _open = false;
        ResetStateInternal();
    }

    /// <summary>
    /// 외부에서 강제 리셋하고 싶을 때(선택)
    /// </summary>
    public void ResetState()
    {
        ResetStateInternal();
    }

    void ResetStateInternal()
    {
        _waitingSecond = false;

        if (_timeoutCo != null)
        {
            StopCoroutine(_timeoutCo);
            _timeoutCo = null;
        }
    }


    public void OnPointerClick(PointerEventData eventData)
    {
        if (!_open) return;

        // 1) 첫 클릭
        if (!_waitingSecond)
        {
            _waitingSecond = true;

            // 제한시간 타이머 시작
            if (_timeoutCo != null) StopCoroutine(_timeoutCo);
            _timeoutCo = StartCoroutine(Timeout());

            return;
        }

        // 2) 두번째 클릭 (시간 내에 들어왔다는 뜻)
        //  성공 처리
        ResetStateInternal();

        // 성공하면 바로 DisOpen: 다음 뽑기는 "뽑기 버튼" 눌러 _open 해야만 가능
        _open = false;

        onDoubleClick?.Invoke();
    }

    IEnumerator Timeout()
    {
        float t = 0f;
        while (t < maxInterval)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        // 시간 초과 = 완전 리셋 (늦게 온 클릭이 새 1클릭이 되지 않음)
        ResetStateInternal();
    }
}
