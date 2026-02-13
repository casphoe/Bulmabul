using System;
using UnityEngine;
using UnityEngine.EventSystems;


public class DoubleClickTrigger : MonoBehaviour, IPointerClickHandler
{
    public float maxInterval = 0.35f;
    public Action onDoubleClick;

    float _lastClickTime = -999f;

    public void OnPointerClick(PointerEventData eventData)
    {
        // eventData.clickCount 쓰면 플랫폼/설정에 따라 불안정할 때가 있어
        // 시간 기반 더블클릭이 실전에서 안전함.
        float now = Time.unscaledTime;
        if (now - _lastClickTime <= maxInterval)
        {
            _lastClickTime = -999f;
            onDoubleClick?.Invoke();
        }
        else
        {
            _lastClickTime = now;
        }
    }
}
