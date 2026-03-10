using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SliderPanelUI : MonoBehaviour
{
    [Header("Target Panel")]
    [SerializeField] private RectTransform panel; // 움직일 패널

    [Header("Sub Panel")]
    [SerializeField] private RectTransform subPanel;

    [Header("Buttons")]
    [SerializeField] private Button btnOpen;   // +
    [SerializeField] private Button btnClose;  // -


    [Header("Positions (Anchored)")]
    [Tooltip("패널이 완전히 닫힌 위치(화면 밖 왼쪽)")]
    [SerializeField] private Vector2 hiddenPos = new Vector2(-1470f, 247.5f);

    [Tooltip("패널이 열렸을 때 위치(화면 안)")]
    [SerializeField] private Vector2 shownPos = new Vector2(-152f, 247.5f);

    [Tooltip("열기 시작할 때 잠깐 잡는 위치(예:-450). 첫 오픈 연출용")]
    [SerializeField] private Vector2 openStartPos = new Vector2(-1170f, 247.5f);

    [Header("Sub Panel Positions")]
    [Tooltip("패널이 완전히 닫힌 위치(화면 밖 왼쪽)")]
    [SerializeField] private Vector2 subHiddenPos = new Vector2(-2129f, 247.5f);

    [Tooltip("패널이 열렸을 때 위치(화면 안)")]
    [SerializeField] private Vector2 subShowPos = new Vector2(-810.5f, 247.5f);

    [Tooltip("열기 시작할 때 잠깐 잡는 위치(예:-450). 첫 오픈 연출용")]
    [SerializeField] private Vector2 subStartPos = new Vector2(-1829, 247.5f);

    [Header("Animation")]
    [Tooltip("초당 이동 속도(px/s). 클수록 빨라짐")]
    [SerializeField] private float moveSpeed = 1800f;

    [Tooltip("열리는 동안 입력 잠금")]
    [SerializeField] private bool lockButtonsWhileMoving = true;

    [Header("켜주는 동안 꺼져야할 오브젝트")]
    [SerializeField] private GameObject memberPanel;

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
        if (panel != null) panel.anchoredPosition = hiddenPos;
        if (subPanel != null) subPanel.anchoredPosition = subHiddenPos;

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

        ShowPanel(true);

        panel.anchoredPosition = openStartPos;
        subPanel.anchoredPosition = subStartPos;

        StartMove(shownPos, subShowPos, openStateAfterMove: true);
    }

    public void ClosePanel()
    {
        if (panel == null) return;
        if (_isMoving) return;
        if (!_isOpen) return;

        StartMove(hiddenPos, subHiddenPos, openStateAfterMove: false);
    }

    public void TogglePanel()
    {
        if (_isOpen) ClosePanel();
        else OpenPanel();
    }

    private void StartMove(Vector2 panelTarget, Vector2 subTarget, bool openStateAfterMove)
    {
        if (_moveCo != null)
            StopCoroutine(_moveCo);

        _moveCo = StartCoroutine(CoMovePanels(panelTarget, subTarget, openStateAfterMove));
    }

    // 둘 다 같은 속도(moveSpeed)로 MoveTowards
    private IEnumerator CoMovePanels(Vector2 panelTarget, Vector2 subTarget, bool openStateAfterMove)
    {
        _isMoving = true;
        RefreshButtonState();

        // 둘 중 하나라도 충분히 멀면 계속 이동
        while (true)
        {
            bool panelDone = (panel == null) || Vector2.Distance(panel.anchoredPosition, panelTarget) <= 0.5f;
            bool subDone = (subPanel == null) || Vector2.Distance(subPanel.anchoredPosition, subTarget) <= 0.5f;

            if (panelDone && subDone)
                break;

            float step = moveSpeed * Time.unscaledDeltaTime;

            if (panel != null && !panelDone)
                panel.anchoredPosition = Vector2.MoveTowards(panel.anchoredPosition, panelTarget, step);

            if (subPanel != null && !subDone)
                subPanel.anchoredPosition = Vector2.MoveTowards(subPanel.anchoredPosition, subTarget, step);

            yield return null;
        }

        if (panel != null) panel.anchoredPosition = panelTarget;
        if (subPanel != null) subPanel.anchoredPosition = subTarget;

        _isOpen = openStateAfterMove;
        _isMoving = false;

        if (!_isOpen) ShowPanel(false);

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

        if (subPanel != null)
            subPanel.anchoredPosition = open ? subShowPos : subHiddenPos; 

        RefreshButtonState();
    }

    void ShowPanel(bool isOpen)
    {
        panel.transform.parent.gameObject.SetActive(isOpen);
        memberPanel.SetActive(!isOpen);
    }
}
