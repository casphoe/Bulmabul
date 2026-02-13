using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class RainbowImageEffect : MonoBehaviour
{
    public float speed = 1.2f;
    Image _img;

    void Awake() => _img = GetComponent<Image>();

    void Update()
    {
        if (_img == null) return;
        float h = Mathf.Repeat(Time.unscaledTime * speed, 1f);
        _img.color = Color.HSVToRGB(h, 1f, 1f);
    }
}
