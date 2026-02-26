using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SliderPanelUI : MonoBehaviour
{
    [Header("Target Panel")]
    [SerializeField] private RectTransform panel; // 움직일 패널

    [Header("Buttons")]
    [SerializeField] private Button btnOpen;   // +
    [SerializeField] private Button btnClose;  // -


    [Header("Positions (Anchored)")]
    [Tooltip("패널이 완전히 닫힌 위치(화면 밖 왼쪽)")]
    [SerializeField] private Vector2 hiddenPos = new Vector2(-900f, 0f);

    [Tooltip("패널이 열렸을 때 위치(화면 안)")]
    [SerializeField] private Vector2 shownPos = new Vector2(0f, 0f);

    [Header("Animation")]
    [Tooltip("초당 이동 속도(px/s). 클수록 빨라짐")]
    [SerializeField] private float moveSpeed = 1800f;

    [Tooltip("열리는 동안 입력 잠금")]
    [SerializeField] private bool lockButtonsWhileMoving = true;

    private Coroutine _moveCo;
    private bool _isOpen = false;
    private bool _isMoving = false;

    public bool IsOpen => _isOpen;
    public bool IsMoving => _isMoving;

    private void Reset()
    {
        panel = GetComponent<RectTransform>();
    }

    private void Awake()
    {
        if (btnOpen != null) btnOpen.onClick.AddListener(OpenPanel);
        if (btnClose != null) btnClose.onClick.AddListener(ClosePanel);
    }

    private void Start()
    {
        // 시작은 닫힌 상태로 배치
        if (panel != null)
            panel.anchoredPosition = hiddenPos;

        _isOpen = false;
        _isMoving = false;

        ShowPanel(false);

        SetInstant(false);
    }

    public void OpenPanel()
    {
        if (panel == null) return;
        if (_isMoving) return; // 움직이는 중 중복 클릭 방지
        if (_isOpen) return;

        ShowPanel(_isOpen);
        StartMove(shownPos, openStateAfterMove: true);
    }

    public void ClosePanel()
    {
        if (panel == null) return;
        if (_isMoving) return;
        if (!_isOpen) return;

        ShowPanel(_isOpen);
        StartMove(hiddenPos, openStateAfterMove: false);
    }

    public void TogglePanel()
    {
        if (_isOpen) ClosePanel();
        else OpenPanel();
    }

    private void StartMove(Vector2 target, bool openStateAfterMove)
    {
        if (_moveCo != null)
            StopCoroutine(_moveCo);

        _moveCo = StartCoroutine(CoMovePanel(target, openStateAfterMove));
    }

    private IEnumerator CoMovePanel(Vector2 target, bool openStateAfterMove)
    {
        _isMoving = true;
        RefreshButtonState();

        while (Vector2.Distance(panel.anchoredPosition, target) > 0.5f)
        {
            panel.anchoredPosition = Vector2.MoveTowards(
                panel.anchoredPosition,
                target,
                moveSpeed * Time.unscaledDeltaTime // UI는 unscaledDeltaTime 권장(일시정지 영향 적음)
            );

            yield return null;
        }

        panel.anchoredPosition = target;

        _isOpen = openStateAfterMove;
        _isMoving = false;

        RefreshButtonState();
        _moveCo = null;
    }

    private void RefreshButtonState()
    {
        if (!lockButtonsWhileMoving) return;

        if (btnOpen != null)
            btnOpen.interactable = !_isMoving && !_isOpen;

        if (btnClose != null)
            btnClose.interactable = !_isMoving && _isOpen;
    }

    // 외부에서 강제로 상태 맞출 때 사용 (씬 시작 시/로그인 직후)
    public void SetInstant(bool open)
    {
        if (_moveCo != null)
        {
            StopCoroutine(_moveCo);
            _moveCo = null;
        }

        _isMoving = false;
        _isOpen = open;

        if (panel != null)
            panel.anchoredPosition = open ? shownPos : hiddenPos;

        RefreshButtonState();
    }

    void ShowPanel(bool isOpen)
    {
        panel.transform.parent.gameObject.SetActive(isOpen);
    }
}
