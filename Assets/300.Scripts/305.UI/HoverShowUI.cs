using System.Collections;
using UnityEngine;

public class HoverShowUI : MonoBehaviour
{
    [Header("마우스를 올릴 기준 UI(RectTransform)")]
    [SerializeField] private RectTransform hoverArea; // 예: BtnChatting(버튼 영역)

    [Header("켜줄 UI(패널/툴팁)")]
    [SerializeField] private GameObject targetUI;     // 예: Panel

    [Header("마우스가 targetUI 위에 있어도 유지")]
    [SerializeField] private bool keepOpenWhileOverTarget = true;

    [Header("나가면 끌지 여부")]
    [SerializeField] private bool hideOnExit = true;

    [Header("깜빡임 방지 딜레이(초)")]
    [SerializeField] private float hideDelay = 0.05f;

    private RoomChatUIManager chatUiManager;
    private Canvas _canvas;
    private Camera _uiCam;
    private Coroutine _hideCo;
    private bool _shown;

    private void Awake()
    {
        if (hoverArea == null || targetUI == null) return;

        _canvas = hoverArea.GetComponentInParent<Canvas>();
        _uiCam = (_canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            ? _canvas.worldCamera
            : null;

        chatUiManager = GetComponent<RoomChatUIManager>();

        // 시작은 꺼두는 걸 권장 (원하면 지워도 됨)
        targetUI.SetActive(false);
        _shown = targetUI.activeSelf;
    }

    private void Update()
    {
        if (hoverArea == null || targetUI == null) return;

        Vector2 mouse = Input.mousePosition;

        bool overTrigger = RectTransformUtility.RectangleContainsScreenPoint(hoverArea, mouse, _uiCam);
        bool overTarget = false;

        if (keepOpenWhileOverTarget)
        {
            var tRect = targetUI.transform as RectTransform;
            if (tRect != null)
                overTarget = RectTransformUtility.RectangleContainsScreenPoint(tRect, mouse, _uiCam);
        }

        if (overTrigger || overTarget)
        {
            CancelHide();
            Show();
        }
        else if (hideOnExit)
        {
            ScheduleHide();
        }
    }

    private void Show()
    {
        if (_shown) return;
        targetUI.SetActive(true);
        _shown = true;

        chatUiManager.OnPanelShown();
    }

    private void Hide()
    {
        if (!_shown) return;
        targetUI.SetActive(false);
        _shown = false;

        chatUiManager.OnPanelHidden();
    }

    private void ScheduleHide()
    {
        if (_hideCo != null) return;
        _hideCo = StartCoroutine(CoHide());
    }

    private void CancelHide()
    {
        if (_hideCo == null) return;
        StopCoroutine(_hideCo);
        _hideCo = null;
    }

    private IEnumerator CoHide()
    {
        yield return new WaitForSeconds(hideDelay);
        _hideCo = null;

        Vector2 mouse = Input.mousePosition;

        bool overTrigger = RectTransformUtility.RectangleContainsScreenPoint(hoverArea, mouse, _uiCam);
        bool overTarget = false;

        if (keepOpenWhileOverTarget)
        {
            var tRect = targetUI.transform as RectTransform;
            if (tRect != null)
                overTarget = RectTransformUtility.RectangleContainsScreenPoint(tRect, mouse, _uiCam);
        }

        if (!(overTrigger || overTarget))
            Hide();
    }
}
