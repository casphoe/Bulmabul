using TMPro;
using UnityEngine;

[RequireComponent(typeof(TMP_InputField))]
//첫글자를 무조건 소문자로 인식하게 함 (이름)
public class LowercaseInput : MonoBehaviour
{
    TMP_InputField input;
    bool guarded;

    void Awake()
    {
        input = GetComponent<TMP_InputField>();
        input.onValueChanged.AddListener(OnChanged);
    }

    void OnDestroy()
    {
        if (input != null) input.onValueChanged.RemoveListener(OnChanged);
    }

    void OnChanged(string s)
    {
        if (guarded) return;
        var lower = s.ToLowerInvariant();
        if (lower == s) return;

        guarded = true;
        int caret = input.caretPosition;
        input.SetTextWithoutNotify(lower);
        input.caretPosition = Mathf.Min(caret, lower.Length);
        guarded = false;
    }
}
