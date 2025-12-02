using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Text.RegularExpressions;
using System;

[Serializable] class FindEmailReq { public string nameKey; public string nick; }
[Serializable] class FindEmailRes { public bool ok; public string maskedEmail; }

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
    public TMP_InputField upName;
    public TMP_InputField upEmail;
    public TMP_InputField upPassword;
    public TMP_InputField upPasswordConfirm;
    public TMP_InputField upNickname; 
    public Toggle upShowPw;

    [Header("Forgot UI")]
    public TMP_InputField fgEmail;

    [Header("Language Toggles")]
    public Toggle inKor, inEng;

    const string LAST_EMAIL_KEY = "last_email";

    #region 토스 메시지 설정 변수
    [Header("토스 메시지")]
    public TextMeshProUGUI toasstMessage;

    [Header("토스트 설정")]
    public float toastShowSeconds = 1.2f; // 완전히 보이는 유지 시간
    public float toastFadeSeconds = 0.8f; // 페이드 아웃 시간

    public Coroutine toastRoutine;
    #endregion

    public static AuthUIController instance;

    private const string FUNCTIONS_REGION = "us-central1";

    private void Awake()
    {
        instance = this;
    }


    private void Start()
    {
        if (PlayerPrefs.HasKey(LAST_EMAIL_KEY))
        {
            inEmail.text = PlayerPrefs.GetString(LAST_EMAIL_KEY);
            inRememberEmail.isOn = true;
        }


        if (toasstMessage != null)
        {
            var c = toasstMessage.color;
            c.a = 0f;
            toasstMessage.color = c;
            toasstMessage.text = "";
        }
        // 초기 패널
        ShowSignIn();
    }

    #region Panel Switch

    public void ShowSignIn()
    {
        SetAll(false);
        panelSignIn.SetActive(true);
        ClearMessage();
    }

    public void ShowSignUp()
    {
        SetAll(false);
        panelSignUp.SetActive(true);
        ClearMessage();
    }
    public void ShowForgot()
    {
        SetAll(false);
        panelForgot.SetActive(true);
        ClearMessage();
    }

    public void GameExit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit(); // 어플리케이션 종료
#endif
    }

    void SetAll(bool v)
    {
        panelSignIn.SetActive(v);
        panelSignUp.SetActive(v);
        panelForgot.SetActive(v);

    }

    public void ClearMessage() => toasstMessage.text = "";
    #endregion

    #region Toast

    public void ShowToast(string msg)
    {
        if (toasstMessage == null) return;

        if (toastRoutine != null) StopCoroutine(toastRoutine);
        toastRoutine = StartCoroutine(CoToastFadeOut(msg, toastShowSeconds, toastFadeSeconds));
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

    #endregion

    #region Toggle & UI Helpers
    public void TogglePassword_SignIn()
       => TogglePassword(inPassword, inShowPw.isOn);

    public void TogglePassword_SignUp()
    {
        TogglePassword(upPassword, upShowPw.isOn);
        TogglePassword(upPasswordConfirm, upShowPw.isOn);
    }

    void TogglePassword(TMP_InputField field, bool show)
    {
        field.contentType = show ? TMP_InputField.ContentType.Standard : TMP_InputField.ContentType.Password;
        field.ForceLabelUpdate();
    }

    public void ToggleLangauge(int langauageNum)
    {
        // 0=Kor, 1=Eng
        LaguageManager.Instance.SetLanguage(langauageNum == 0 ? Lauaguage.Kor : Lauaguage.Eng);
    }

    #region 이메일인지 아닌지 여부와 패스워드 강도 알아보게 하는 기능
    bool IsValidEmail(string email)
       => !string.IsNullOrWhiteSpace(email) &&
          Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");

    bool IsStrongPassword(string pw)
        => !string.IsNullOrWhiteSpace(pw) && pw.Length >= 6; // Firebase 최소 6자\
    #endregion

    #endregion


    #region 버튼
    // 1) 로그인 버튼
    public async void OnClickLogin()
    {
        try
        {
            string email = inEmail.text.Trim();
            string pw = inPassword.text;

            if (!IsValidEmail(email)) throw new Exception("이메일 형식이 올바르지 않습니다.");
            if (string.IsNullOrWhiteSpace(pw)) throw new Exception("비밀번호를 입력하세요.");

            await FireBaseAuthManager.Instance.LoginAsync(email, pw);

            // 이메일 기억
            if (inRememberEmail != null && inRememberEmail.isOn)
            {
                PlayerPrefs.SetString(LAST_EMAIL_KEY, email);
                PlayerPrefs.Save();
            }
            else
            {
                PlayerPrefs.DeleteKey(LAST_EMAIL_KEY);
                PlayerPrefs.Save();
            }
        }
        catch (Exception e)
        {
            ShowToast(e.Message);
        }
    }

    // 2) 회원가입 버튼(이름 + 닉 필수)
    public async void OnClickRegister()
    {
        try
        {
            string name = upName != null ? upName.text.Trim() : "";
            string email = upEmail.text.Trim();
            string pw = upPassword.text;
            string pw2 = upPasswordConfirm.text;
            string nick = upNickname.text.Trim();

            if (string.IsNullOrWhiteSpace(name)) throw new Exception("이름은 필수입니다.");
            if (!IsValidEmail(email)) throw new Exception("이메일 형식이 올바르지 않습니다.");
            if (!IsStrongPassword(pw)) throw new Exception("비밀번호는 6자 이상이어야 합니다.");
            if (pw != pw2) throw new Exception("비밀번호 확인이 일치하지 않습니다.");
            if (string.IsNullOrWhiteSpace(nick)) throw new Exception("닉네임은 필수입니다.");

            await FireBaseAuthManager.Instance.RegisterAsync(name, email, pw, nick);

            inEmail.text = email;
        }
        catch (Exception e)
        {
            ShowToast(e.Message);
        }
    }

    // 3) 비밀번호 찾기(재설정 메일 발송) 버튼
    public async void OnClickSendResetPasswordEmail()
    {
        try
        {
            string email = fgEmail.text.Trim();
            if (!IsValidEmail(email)) throw new Exception("이메일 형식이 올바르지 않습니다.");

            await FireBaseAuthManager.Instance.SendPasswordResetEmailAsync(email);
        }
        catch (Exception e)
        {
            ShowToast(e.Message);
        }
    }
    #endregion
}
