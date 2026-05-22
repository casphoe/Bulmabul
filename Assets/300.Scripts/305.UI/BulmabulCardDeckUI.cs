using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 카드 더미 UI 표시.
/// 
/// 실제 카드 수(maxCardCount)는 0~100이고,
/// UI에서는 최대 20장의 더미처럼 보이게 만든다.
/// 
/// 규칙:
/// - 실제 카드 15장 = UI 카드 1장
/// - 100장 -> 20장 보임
/// - 95장  -> 19장 보임
/// - 90장  -> 18장 보임
/// </summary>
public class BulmabulCardDeckUI : MonoBehaviour
{
    [Header("루트")]
    [SerializeField] private RectTransform deckStackRoot;

    [Header("카드 프리팹(없으면 imageTemplate 사용)")]
    [SerializeField] private Image cardPrefab;

    [Header("카드 템플릿(씬에 하나 배치해둔 카드 이미지)")]
    [SerializeField] private Image imageTemplate;

    [Header("설정")]
    [SerializeField] private int maxCardCount = 300;
    [SerializeField] private int maxVisibleStackCount = 30;

    [Tooltip("카드가 겹쳐질 때 X 간격")]
    [SerializeField] private float offsetX = 2.5f;

    [Tooltip("카드가 겹쳐질 때 Y 간격")]
    [SerializeField] private float offsetY = 3.0f;

    [Tooltip("뒤 카드가 살짝 작아지게 할지")]
    [SerializeField] private bool useScaleOffset = false;

    [SerializeField] private float scaleStep = 0.01f;

    [Header("선택: 남은 카드 수 텍스트")]
    [SerializeField] private TextMeshProUGUI txtCardCount;

    private readonly List<Image> stackImages = new List<Image>();

    private int currentCardCount = 300;

    private void Awake()
    {
        CreateStackImages();
        RefreshDeckView();
    }

    /// <summary>
    /// 카드 UI 20장을 생성한다.
    /// </summary>
    private void CreateStackImages()
    {
        if (deckStackRoot == null)
        {
            Debug.LogWarning("deckStackRoot가 연결되지 않았습니다.");
            return;
        }

        // 기존 생성물 제거
        for (int i = deckStackRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(deckStackRoot.GetChild(i).gameObject);
        }

        stackImages.Clear();

        Image sourcePrefab = cardPrefab != null ? cardPrefab : imageTemplate;

        if (sourcePrefab == null)
        {
            Debug.LogWarning("cardPrefab 또는 imageTemplate 중 하나는 반드시 필요합니다.");
            return;
        }

        for (int i = 0; i < maxVisibleStackCount; i++)
        {
            Image img = Instantiate(sourcePrefab, deckStackRoot);
            img.name = $"DeckCard_{i + 1}";

            RectTransform rt = img.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            // 뒤에서 앞으로 갈수록 살짝 위/오른쪽으로
            rt.anchoredPosition = new Vector2(i * offsetX, i * offsetY);

            if (useScaleOffset)
            {
                float scale = 1f - ((maxVisibleStackCount - 1 - i) * scaleStep);
                rt.localScale = Vector3.one * scale;
            }
            else
            {
                rt.localScale = Vector3.one;
            }

            img.gameObject.SetActive(false);
            stackImages.Add(img);
        }
    }

    /// <summary>
    /// 현재 카드 수를 직접 설정.
    /// </summary>
    public void SetCardCount(int count)
    {
        currentCardCount = Mathf.Clamp(count, 0, maxCardCount);
        RefreshDeckView();
    }

    /// <summary>
    /// 카드 count만큼 뽑는다.
    /// </summary>
    public void DrawCards(int count)
    {
        if (count <= 0)
            return;

        currentCardCount -= count;
        if (currentCardCount < 0)
            currentCardCount = 0;

        RefreshDeckView();
    }

    /// <summary>
    /// 카드 count만큼 다시 넣는다.
    /// </summary>
    public void AddCards(int count)
    {
        if (count <= 0)
            return;

        currentCardCount += count;
        if (currentCardCount > maxCardCount)
            currentCardCount = maxCardCount;

        RefreshDeckView();
    }

    /// <summary>
    /// 현재 카드 수에 맞춰 보이는 더미 수를 갱신한다.
    /// </summary>
    private void RefreshDeckView()
    {
        int visibleCount = GetVisibleStackCount(currentCardCount);

        for (int i = 0; i < stackImages.Count; i++)
        {
            bool active = i < visibleCount;
            stackImages[i].gameObject.SetActive(active);

            if (active)
            {
                stackImages[i].transform.SetSiblingIndex(i);
            }
        }
        if (txtCardCount != null)
        {
            var lang = (LaguageManager.Instance != null)
                ? LaguageManager.Instance.currentLang
                : Lauaguage.Kor;

            if (lang == Lauaguage.Kor)
                txtCardCount.text = $"남은 카드 {currentCardCount} / {maxCardCount}";
            else
                txtCardCount.text = $"Cards {currentCardCount} / {maxCardCount}";
        }
    }

    /// <summary>
    /// 실제 카드 수 -> UI 더미 수 변환
    /// </summary>
    private int GetVisibleStackCount(int realCardCount)
    {
        if (realCardCount <= 0)
            return 0;

        float cardsPerStack = (float)maxCardCount / maxVisibleStackCount;
        return Mathf.Clamp(Mathf.CeilToInt(realCardCount / cardsPerStack), 0, maxVisibleStackCount);
    }

#if UNITY_EDITOR
    [ContextMenu("테스트 - 5장 뽑기")]
    private void TestDraw5()
    {
        DrawCards(5);
    }

    [ContextMenu("테스트 - 1장 뽑기")]
    private void TestDraw1()
    {
        DrawCards(1);
    }

    [ContextMenu("테스트 - 100장으로 리셋")]
    private void TestReset100()
    {
        SetCardCount(300);
    }
#endif
}