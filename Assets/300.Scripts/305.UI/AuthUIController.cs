using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class AuthUIController : MonoBehaviour
{
    [Header("Panels")]
    public GameObject panelSignIn, panelSignUp, panelForgot;

    [Header("SignIn UI")]
    public TMP_InputField inEmail;
    public TMP_InputField inPassword;
    public Toggle inShowPw;
    public Toggle inRememberEmail;

    [Header("SignUp UI")]
    public TMP_InputField upEmail;
    public TMP_InputField upPassword;
    public TMP_InputField upPasswordConfirm;
    public TMP_InputField upNickname; 
    public Toggle upShowPw;

    [Header("Forgot UI")]
    public TMP_InputField fgEmail;

    const string LAST_EMAIL_KEY = "last_email";

    private void Start()
    {
        if (PlayerPrefs.HasKey(LAST_EMAIL_KEY))
        {
            inEmail.text = PlayerPrefs.GetString(LAST_EMAIL_KEY);
            inRememberEmail.isOn = true;
        }
    }
}
