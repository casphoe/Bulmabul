using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public class DicePullResultPopup : MonoBehaviour
{
    [Header("Root")]
    public GameObject root;
    public Button btnClose;

    [Header("Effect")]
    public GameObject effectRoot;   // 파티클/애니메이션 오브젝트
    public float effectTime = 1.0f; // 이펙트 재생 시간

    [Header("Results Roots")]
    public Transform singleCenter; // 1회 결과(가운데)
    public Transform tenGrid;      // 10회 결과(GridLayoutGroup)
    public DiceResultItem itemPrefab;

    [Header("Close Settings")]
    public float autoCloseSeconds = 3f; // 결과 표시 후 자동 닫기 시간
    public bool rightClickClose = true; // 우클릭 닫기

    CancellationTokenSource _closeCts;
    bool _isOpen;

    void Awake()
    {
        if (root != null) root.SetActive(false);

        if (btnClose != null)
            btnClose.onClick.AddListener(Close);

        if (effectRoot != null)
            effectRoot.SetActive(false);

        if (singleCenter != null) singleCenter.gameObject.SetActive(false);
        if (tenGrid != null) tenGrid.gameObject.SetActive(false);
    }

    void OnDisable()
    {
        CancelCloseTimer();
        _isOpen = false;
    }

    void Update()
    {
        if (!_isOpen) return;

        if (rightClickClose && Input.GetMouseButtonDown(1)) // 우클릭
        {
            Close();
        }
    }

    public void Open(int pullCount)
    {
        CancelCloseTimer();
        ClearGrid();

        if (root != null) root.SetActive(true);
        if (effectRoot != null) effectRoot.SetActive(false);

        ApplyLayout(pullCount);
        _isOpen = true;
    }

    public void Close()
    {
        CancelCloseTimer();

        if (effectRoot != null) effectRoot.SetActive(false);
        if (root != null) root.SetActive(false);

        _isOpen = false;
    }

    // 스킵 모드: 연출 없이 결과만
    public void ShowResultsInstant(List<Dice> rolled)
    {
        if (effectRoot != null) effectRoot.SetActive(false);
        ShowResults(rolled);

        StartAutoCloseTimer();
    }

    // 일반 모드: 이펙트 후 결과
    public async Task PlayEffectThenShowAsync(List<Dice> rolled)
    {
        if (effectRoot != null) effectRoot.SetActive(true);

        await Wait(effectTime);

        if (effectRoot != null) effectRoot.SetActive(false);

        ShowResults(rolled);

        StartAutoCloseTimer();
    }

    void ApplyLayout(int pullCount)
    {
        bool isTen = (pullCount >= 10);

        if (singleCenter != null) singleCenter.gameObject.SetActive(!isTen);
        if (tenGrid != null) tenGrid.gameObject.SetActive(isTen);
    }

    void ShowResults(List<Dice> rolled)
    {
        if (rolled == null || itemPrefab == null) return;
        if (singleCenter == null || tenGrid == null) return;

        ClearGrid();

        bool isTen = rolled.Count >= 10;
        ApplyLayout(isTen ? 10 : 1);

        Transform parent = isTen ? tenGrid : singleCenter;

        for (int i = 0; i < rolled.Count; i++)
        {
            var it = Instantiate(itemPrefab, parent);
            it.Set(rolled[i]); // 여기서 등급별 색/이미지 처리
        }
    }


    void ClearGrid()
    {
        ClearChildren(singleCenter);
        ClearChildren(tenGrid);
    }

    static void ClearChildren(Transform t)
    {
        if (t == null) return;
        for (int i = t.childCount - 1; i >= 0; i--)
            Object.Destroy(t.GetChild(i).gameObject);
    }

    void StartAutoCloseTimer()
    {
        CancelCloseTimer();
        _closeCts = new CancellationTokenSource();
        _ = AutoCloseAsync(_closeCts.Token);
    }

    void CancelCloseTimer()
    {
        if (_closeCts != null)
        {
            _closeCts.Cancel();
            _closeCts.Dispose();
            _closeCts = null;
        }
    }

    async Task AutoCloseAsync(CancellationToken ct)
    {
        float t = 0f;
        while (t < autoCloseSeconds)
        {
            if (ct.IsCancellationRequested) return;
            t += Time.unscaledDeltaTime;
            await Task.Yield();
        }

        if (!ct.IsCancellationRequested)
            Close();
    }

    static async Task Wait(float sec)
    {
        float t = 0f;
        while (t < sec)
        {
            t += Time.unscaledDeltaTime;
            await Task.Yield();
        }
    }
}
