using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LocalizedText : MonoBehaviour
{
    public string key;

    [Header("Optional (auto-detect if empty)")]
    public TMP_Text tmp;
    public Text ugui;

    private void Awake()
    {
        if (tmp == null) tmp = GetComponent<TMP_Text>();
        if (ugui == null) ugui = GetComponent<Text>();
    }

    private void OnEnable()
    {
        LaguageManager.Instance?.Register(this);
        Refresh();
    }

    private void OnDisable()
    {
        LaguageManager.Instance?.Unregister(this);
    }

    public void Refresh()
    {
        if (string.IsNullOrEmpty(key)) return;

        string value = LaguageManager.Instance != null
            ? LaguageManager.Instance.Get(key)
            : key;

        if (tmp != null) tmp.text = value;
        if (ugui != null) ugui.text = value;
    }
}
