using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// 화면 중앙/상단 등에 짧은 안내 메시지를 띄우는 토스트 메시지 매니저.
/// 
/// 특징:
/// - Time.timeScale = 0인 일시정지 상태에서도 정상 작동
/// - 메시지 표시 후 자동 페이드 아웃
/// - 한국어/영어 언어 처리 지원
/// - CanvasGroup을 사용해 메시지 전체 알파 제어
/// </summary>
public class ToastMessageManager : MonoBehaviour
{
    [Header("토스트 메시지")]
    public TextMeshProUGUI toasstMessage;

    [Header("토스트 설정")]
    [Tooltip("메시지가 완전히 보이는 시간. 일시정지 중에도 흐름")]
    public float toastShowSeconds = 1.2f;

    [Tooltip("메시지가 서서히 사라지는 시간. 일시정지 중에도 흐름")]
    public float toastFadeSeconds = 0.8f;

    [Header("Canvas Group")]
    [Tooltip("토스트 전체 알파 제어용 CanvasGroup. 비워두면 자동으로 찾거나 생성")]
    [SerializeField] private CanvasGroup toastCanvasGroup;

    public Coroutine toastRoutine;

    public static ToastMessageManager instance;

    private void Awake()
    {
        instance = this;

        EnsureCanvasGroup();

        if (toasstMessage != null)
        {
            toasstMessage.text = "";

            Color c = toasstMessage.color;
            c.a = 1f;
            toasstMessage.color = c;
        }

        if (toastCanvasGroup != null)
        {
            toastCanvasGroup.alpha = 0f;
            toastCanvasGroup.interactable = false;
            toastCanvasGroup.blocksRaycasts = false;
        }

        // PauseBlockPanel보다 위에 보이도록 Canvas에서 가장 마지막으로 올림
        transform.SetAsLastSibling();
    }

    /// <summary>
    /// CanvasGroup 자동 연결.
    /// ToastMessage 오브젝트 또는 부모 오브젝트에서 찾고,
    /// 없으면 ToastMessage 오브젝트에 추가한다.
    /// </summary>
    private void EnsureCanvasGroup()
    {
        if (toastCanvasGroup != null)
            return;

        toastCanvasGroup = GetComponent<CanvasGroup>();

        if (toastCanvasGroup == null && transform.parent != null)
            toastCanvasGroup = transform.parent.GetComponent<CanvasGroup>();

        if (toastCanvasGroup == null)
            toastCanvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    /// <summary>
    /// 한국어 메시지만 출력.
    /// </summary>
    public void ShowToast(string msg)
    {
        if (toasstMessage == null)
            return;

        EnsureCanvasGroup();

        // 다른 UI 패널보다 위에 보이도록 매번 맨 앞으로 올림
        transform.SetAsLastSibling();

        if (toastRoutine != null)
            StopCoroutine(toastRoutine);

        toastRoutine = StartCoroutine(CoToastFadeOut(msg, toastShowSeconds, toastFadeSeconds));
    }

    /// <summary>
    /// 현재 언어에 맞는 메시지 출력.
    /// </summary>
    public void ShowToast(string kor, string eng)
    {
        ShowToast(GetByLanguage(kor, eng));
    }

    private string GetByLanguage(string kor, string eng)
    {
        if (LaguageManager.Instance == null)
            return kor;

        return LaguageManager.Instance.currentLang == Lauaguage.Eng ? eng : kor;
    }

    /// <summary>
    /// 일시정지 상태에서도 동작하는 토스트 코루틴.
    /// WaitForSecondsRealtime / Time.unscaledDeltaTime을 사용한다.
    /// </summary>
    private IEnumerator CoToastFadeOut(string msg, float showSec, float fadeSec)
    {
        toasstMessage.gameObject.SetActive(true);
        toasstMessage.text = msg;

        if (toastCanvasGroup != null)
        {
            toastCanvasGroup.alpha = 1f;
            toastCanvasGroup.interactable = false;
            toastCanvasGroup.blocksRaycasts = false;
        }

        // Text 자체 알파는 1로 유지하고,
        // 실제 사라짐은 CanvasGroup alpha로 처리한다.
        Color textColor = toasstMessage.color;
        textColor.a = 1f;
        toasstMessage.color = textColor;

        if (showSec > 0f)
            yield return new WaitForSecondsRealtime(showSec);

        float t = 0f;
        float dur = Mathf.Max(0.01f, fadeSec);

        while (t < dur)
        {
            t += Time.unscaledDeltaTime;

            float alpha = Mathf.Lerp(1f, 0f, t / dur);

            if (toastCanvasGroup != null)
                toastCanvasGroup.alpha = alpha;

            yield return null;
        }

        if (toastCanvasGroup != null)
            toastCanvasGroup.alpha = 0f;

        toasstMessage.text = "";
        toasstMessage.gameObject.SetActive(false);

        toastRoutine = null;
    }
}