using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SpriteManager : MonoBehaviour
{
    [SerializeField] Sprite[] mapSprite;

    public static SpriteManager instance;

    private void Awake()
    {
        instance = this;
    }

    public void imageMapSpriteChange(Image img, int num)
    {
        img.sprite = mapSprite[num];
    }
}
