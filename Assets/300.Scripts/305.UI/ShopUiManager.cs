using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public enum ShopUi
{
    Dice,Coin
}

public class ShopUiManager : MonoBehaviour
{
    [Header("주사위 뽑기, 재화 구매 버튼")]
    [SerializeField] Button[] btnShops;

    [Header("주사위 뽑기, 재화 구매 오브젝트 들")]
    [SerializeField] GameObject[] shopObjects;

    [SerializeField] ShopUi shop;

    private void Awake()
    {
        shop = ShopUi.Dice;
        for (int i = 0; i < btnShops.Length; i++)
        {
            int idx = i; // 클로저 방지
            btnShops[i].onClick.AddListener(() => ShopUiClick(idx));
        }

        ApplyShopUI(shop);
    }

    void ShopUiClick(int num)
    {
        shop = (ShopUi)num;
        ApplyShopUI(shop);
    }

    void ApplyShopUI(ShopUi target)
    {
        for (int i = 0; i < shopObjects.Length; i++)
            shopObjects[i].SetActive(i == (int)target);
    }

}
