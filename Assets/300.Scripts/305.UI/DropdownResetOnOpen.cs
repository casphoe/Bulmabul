using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class DropdownResetOnOpen : MonoBehaviour , IPointerDownHandler
{
    public TMP_Dropdown dropdown;

    void Reset()
    {
        if (dropdown == null) dropdown = GetComponent<TMP_Dropdown>();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (dropdown == null) return;
        dropdown.SetValueWithoutNotify(0); // 드롭다운 열기 직전에 0으로 초기화
    }
}
