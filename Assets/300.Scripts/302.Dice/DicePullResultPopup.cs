using System.Collections.Generic;
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

    [Header("Results")]
    public Transform resultGrid;
    public DiceResultItem itemPrefab;

    void Awake()
    {
        if (root != null) root.SetActive(false);

        if (btnClose != null)
            btnClose.onClick.AddListener(Close);

        if (effectRoot != null)
            effectRoot.SetActive(false);
    }

    public void Open(int pullCount)
    {
        ClearGrid();

        if (root != null) root.SetActive(true);
        if (effectRoot != null) effectRoot.SetActive(false);
    }

    public void Close()
    {
        if (effectRoot != null) effectRoot.SetActive(false);
        if (root != null) root.SetActive(false);
    }

    // 스킵 모드: 연출 없이 결과만
    public void ShowResultsInstant(List<Dice> rolled)
    {
        if (effectRoot != null) effectRoot.SetActive(false);
        ShowResults(rolled);
    }

    // 일반 모드: 이펙트 후 결과
    public async Task PlayEffectThenShowAsync(List<Dice> rolled)
    {
        if (effectRoot != null) effectRoot.SetActive(true);

        await Wait(effectTime);

        if (effectRoot != null) effectRoot.SetActive(false);

        ShowResults(rolled);
    }

    void ShowResults(List<Dice> rolled)
    {
        if (rolled == null || itemPrefab == null || resultGrid == null) return;

        ClearGrid();

        for (int i = 0; i < rolled.Count; i++)
        {
            var it = Instantiate(itemPrefab, resultGrid);
            it.Set(rolled[i]); // 여기서 등급별 색/이미지 처리
        }
    }

    void ClearGrid()
    {
        if (resultGrid == null) return;
        for (int i = resultGrid.childCount - 1; i >= 0; i--)
            Destroy(resultGrid.GetChild(i).gameObject);
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
