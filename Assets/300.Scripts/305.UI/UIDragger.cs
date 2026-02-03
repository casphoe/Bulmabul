using UnityEngine;
using UnityEngine.EventSystems;

public class UIDragger : MonoBehaviour, IBeginDragHandler, IDragHandler
{
    [Header("Move Target (채팅창 루트 RectTransform)")]
    public RectTransform target;

    [Header("Optional: Clamp inside Canvas")]
    public bool clampToCanvas = true;

    RectTransform _canvasRect;
    Vector2 _startTargetPos;
    Vector2 _startPointerLocalPos;


    void Awake()
    {
        if (target == null) target = transform as RectTransform;

        var canvas = GetComponentInParent<Canvas>();
        if (canvas != null) _canvasRect = canvas.transform as RectTransform;
    }

    public void OnBeginDrag(PointerEventData e)
    {
        if (target == null) return;

        _startTargetPos = target.anchoredPosition;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            target.parent as RectTransform, e.position, e.pressEventCamera, out _startPointerLocalPos);
    }

    public void OnDrag(PointerEventData e)
    {
        if (target == null) return;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            target.parent as RectTransform, e.position, e.pressEventCamera, out var curPointerLocalPos);

        var delta = curPointerLocalPos - _startPointerLocalPos;
        var newPos = _startTargetPos + delta;

        if (clampToCanvas && _canvasRect != null)
            newPos = ClampToCanvas(target, _canvasRect, newPos);

        target.anchoredPosition = newPos;
    }

    static Vector2 ClampToCanvas(RectTransform window, RectTransform canvas, Vector2 pos)
    {
        // 간단 clamp: 창이 캔버스 밖으로 완전히 나가지 않게
        Vector2 half = window.rect.size * 0.5f;
        Vector2 cHalf = canvas.rect.size * 0.5f;

        pos.x = Mathf.Clamp(pos.x, -cHalf.x + half.x, cHalf.x - half.x);
        pos.y = Mathf.Clamp(pos.y, -cHalf.y + half.y, cHalf.y - half.y);

        return pos;
    }
}
