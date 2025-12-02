using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[Serializable]
public class LangItem { public string key; public string text; }

[Serializable]
public class LangTable
{
    public List<LangItem> kor;
    public List<LangItem> eng;
}

public class LaguageManager : MonoBehaviour
{
    const string LANG_KEY = "lang"; // 0=Kor, 1=Eng

    [Header("Language Json (TextAsset)")]
    public TextAsset languageJson;

    [Header("현재 언어")]
    public Lauaguage currentLang = Lauaguage.Kor;

    public static LaguageManager Instance;

    private bool _isChangingLang;   // 토글 루프 방지

    private readonly HashSet<LocalizedText> _targets = new HashSet<LocalizedText>();
    private Dictionary<string, string> _mapKor = new Dictionary<string, string>();
    private Dictionary<string, string> _mapEng = new Dictionary<string, string>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);   // 중복 제거
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        LoadLanguageFromPrefs();
        LoadJsonTables();
        ApplyLanguageToToggles_NoNotify(currentLang);
        ApplyLanguageToUI(currentLang);
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 씬 로드 직후 1프레임 기다렸다가(새 UI 생성/Start 이후) 적용
        StartCoroutine(CoReapplyNextFrame());
    }

    private IEnumerator CoReapplyNextFrame()
    {
        yield return null;
        ReapplyToCurrentSceneUI();
    }

    private void ReapplyToCurrentSceneUI()
    {
        ApplyLanguageToToggles_NoNotify(currentLang);
        ApplyLanguageToUI(currentLang);
    }

    public void Register(LocalizedText t)
    {
        if (t != null) _targets.Add(t);
    }

    public void Unregister(LocalizedText t)
    {
        if (t != null) _targets.Remove(t);
    }

    public string Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";

        var map = (currentLang == Lauaguage.Kor) ? _mapKor : _mapEng;
        if (map != null && map.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v))
            return v;

        // 키가 없으면 디버깅 편하게 key 그대로 출력
        return $"[{key}]";
    }


    private void OnKorToggleChanged(bool isOn)
    {
        if (_isChangingLang) return;
        if (!isOn) return; // 꺼지는 이벤트는 무시
        SetLanguage(Lauaguage.Kor);
    }

    private void OnEngToggleChanged(bool isOn)
    {
        if (_isChangingLang) return;
        if (!isOn) return;
        SetLanguage(Lauaguage.Eng);
    }

    /// <summary>
    /// 언어 변경의 "단일 진입점"
    /// - 토글 배타처리
    /// - PlayerPrefs 저장
    /// - UI 갱신
    /// </summary>
    public void SetLanguage(Lauaguage lang)
    {
        if (currentLang == lang) return;

        currentLang = lang;
        SaveLanguageToPrefs(lang);

        ApplyLanguageToToggles_NoNotify(lang);
        ApplyLanguageToUI(lang);

        Debug.Log($"[LANG] set to {lang}");
    }

    private void ApplyLanguageToToggles_NoNotify(Lauaguage lang)
    {
        _isChangingLang = true;

        // 핵심: static instance가 씬 재진입 때 null일 수 있으니 fallback
        var auth = AuthUIController.instance != null
            ? AuthUIController.instance
            : FindFirstObjectByType<AuthUIController>();

        if (auth != null)
        {
            if (auth.inKor != null) auth.inKor.SetIsOnWithoutNotify(lang == Lauaguage.Kor);
            if (auth.inEng != null) auth.inEng.SetIsOnWithoutNotify(lang == Lauaguage.Eng);
        }

        _isChangingLang = false;
    }

    private void SaveLanguageToPrefs(Lauaguage lang)
    {
        PlayerPrefs.SetInt(LANG_KEY, lang == Lauaguage.Kor ? 0 : 1);
        PlayerPrefs.Save();
    }

    private void LoadLanguageFromPrefs()
    {
        int v = PlayerPrefs.GetInt(LANG_KEY, 0);
        currentLang = (v == 0) ? Lauaguage.Kor : Lauaguage.Eng;
    }

    private void LoadJsonTables()
    {
        if (languageJson == null)
        {
            Debug.LogWarning("[LANG] languageJson(TextAsset)이 비어있음. Inspector에 넣어주세요.");
            _mapKor = new Dictionary<string, string>();
            _mapEng = new Dictionary<string, string>();
            return;
        }

        try
        {
            var table = JsonUtility.FromJson<LangTable>(languageJson.text);

            _mapKor = BuildMap(table?.kor);
            _mapEng = BuildMap(table?.eng);

            Debug.Log($"[LANG] Loaded: kor={_mapKor.Count}, eng={_mapEng.Count}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[LANG] JSON parse failed: {e}");
            _mapKor = new Dictionary<string, string>();
            _mapEng = new Dictionary<string, string>();
        }
    }

    private Dictionary<string, string> BuildMap(List<LangItem> list)
    {
        var map = new Dictionary<string, string>();
        if (list == null) return map;

        foreach (var it in list)
        {
            if (it == null) continue;
            if (string.IsNullOrEmpty(it.key)) continue;
            map[it.key] = it.text ?? "";
        }
        return map;
    }

    private void ApplyLanguageToUI(Lauaguage lang)
    {
        // null 정리(씬 이동으로 파괴된 오브젝트)
        _targets.RemoveWhere(t => t == null);

        // 등록된 모든 LocalizedText 갱신
        foreach (var t in _targets)
            if (t != null) t.Refresh();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
