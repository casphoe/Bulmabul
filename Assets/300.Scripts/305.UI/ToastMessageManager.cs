using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class ToastMessageManager : MonoBehaviour
{

    [Header("토스 메시지")]
    public TextMeshProUGUI toasstMessage;

    [Header("토스트 설정")]
    public float toastShowSeconds = 1.2f; // 완전히 보이는 유지 시간
    public float toastFadeSeconds = 0.8f; // 페이드 아웃 시간

    public Coroutine toastRoutine;

    public static ToastMessageManager instance;

    private void Awake()
    {
        instance = this;
    }

    private void Start()
    {
        if (toasstMessage != null)
        {
            var c = toasstMessage.color;
            c.a = 0f;
            toasstMessage.color = c;
            toasstMessage.text = "";
        }
        toasstMessage.transform.parent.GetComponent<CanvasGroup>().blocksRaycasts = false;
    }

    public void ShowToast(string msg)
    {
        if (toasstMessage == null) return;

        if (toastRoutine != null) StopCoroutine(toastRoutine);
        toastRoutine = StartCoroutine(CoToastFadeOut(msg, toastShowSeconds, toastFadeSeconds));
    }

    public void ShowToast(string kor, string eng)
    {
        ShowToast(GetByLanguage(kor, eng));
    }

    private string GetByLanguage(string kor, string eng)
    {
        // LaguageManager가 없으면 한국어 기본
        if (LaguageManager.Instance == null) return kor;

        return (LaguageManager.Instance.currentLang == Lauaguage.Eng) ? eng : kor;
    }

    IEnumerator CoToastFadeOut(string msg, float showSec, float fadeSec)
    {
        toasstMessage.text = msg;

        Color c = toasstMessage.color;
        c.a = 1f;
        toasstMessage.color = c;

        if (showSec > 0f)
            yield return new WaitForSeconds(showSec);

        float t = 0f;
        float dur = Mathf.Max(0.01f, fadeSec);

        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            c.a = Mathf.Lerp(1f, 0f, t / dur);
            toasstMessage.color = c;
            yield return null;
        }

        c.a = 0f;
        toasstMessage.color = c;
        toasstMessage.text = "";
        toastRoutine = null;
    }
}
