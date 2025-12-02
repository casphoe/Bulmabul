using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System;
using TMPro;

public class LobbyUIManager : MonoBehaviour
{
    #region 옵션
    [Header("옵션의 따른 버튼 및 오브젝트들")]
    [SerializeField] Button btnOption;

    [Header("Option Panel 오브젝트")]
    [SerializeField] GameObject optionPanel;
    #endregion

    #region 회원 탈퇴
    [Header("회원 탈퇴 Panel 오브젝트")]
    [SerializeField] GameObject memberShipDwraw;

    [Header("회원 탈퇴에 다른 입력 Password")]
    [SerializeField] TMP_InputField password;

    [SerializeField] TMP_InputField confirmPassword;
    #endregion

    #region 방 만들기
    [Header("방 만들기 Panel 오브젝트")]
    [SerializeField] GameObject roomPanel;
    #endregion

    public static LobbyUIManager instance;

    private void Awake()
    {
        instance = this;
    }

    private void Start()
    {
        SetActive(false);
    }

    #region UI 켜주는 기능
    void SetActive(bool isActive)
    {
        optionPanel.SetActive(isActive);
        memberShipDwraw.SetActive(isActive);
        roomPanel.SetActive(isActive);
    }

    public async void BtnClick(int num)
    {
        SetActive(false);
        switch(num)
        {
            case 0:
                optionPanel.SetActive(true);
                break;
            case 1:
                try
                {
                    await FireBaseAuthManager.Instance.LogoutToScene0Async();
                }
                catch (Exception e)
                {
                    Debug.LogError(e);                  
                }
                break;
            case 2:
                memberShipDwraw.SetActive(true);
                break;
            case 3:
                try
                {
                    string pass1 = password.text;
                    string pass2 = confirmPassword.text;
                    if(pass1 == pass2)
                    {
                        await FireBaseAuthManager.Instance.DeleteCurrentAccountAsync(password.text);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }
                break;
            case 4:
                memberShipDwraw.SetActive(false);
                break;
        }
    }
    #endregion

    #region 회원 탈퇴

    #endregion
}
