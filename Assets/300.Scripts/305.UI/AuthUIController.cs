using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Text.RegularExpressions;
using System;

using System.IO;
using SFB;


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

    [Header("Profile Photo")]
    [SerializeField] Button btnPickPhoto;
    [SerializeField] RawImage previewRawImage; //  미리보기
    [SerializeField] string defaultOpenFolder = ""; // 비우면 "내 문서" 등 기본 위치
    [SerializeField] int maxUploadMB = 5;

    // 네 Storage 버킷 주소로 바꿔줘 (Firebase 콘솔 Storage에서 확인 가능)
    // 예: "gs://burumabul.appspot.com"
    [SerializeField] string storageBucketUrl = "gs://YOUR_BUCKET.appspot.com";

    Texture2D _previewTex;

    byte[] _pickedImageBytes = null;
    string _pickedContentType = null;

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
        SignUpSetDeafualt();
        ClearMessage();
    }
    public void ShowForgot()
    {
        SetAll(false);
        panelForgot.SetActive(true);
        ForgotInputClear();
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

    void SignUpSetDeafualt()
    {
        upName.text = "";
        upEmail.text = "";
        upPassword.text = "";
        upPasswordConfirm.text = "";
        upShowPw.isOn = false;
        upNickname.text = "";
        TogglePassword_SignUp();
        _pickedImageBytes = null;
        _pickedContentType = null;
        if (previewRawImage) previewRawImage.texture = null;
    }

    void ForgotInputClear()
    {
        fgEmail.text = "";
    }

    public void ClearMessage() => ToastMessageManager.instance.toasstMessage.text = "";
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

    #region  언어 

    private string T(string kor, string eng)
    {
        if (LaguageManager.Instance == null) return kor;
        return (LaguageManager.Instance.currentLang == Lauaguage.Eng) ? eng : kor;
    }

    #endregion


    #region 버튼
    // 1) 로그인 버튼
    public async void OnClickLogin()
    {
        try
        {
            string email = inEmail.text.Trim();
            string pw = inPassword.text;

            if (!IsValidEmail(email))
                throw new Exception(T("이메일 형식이 올바르지 않습니다.", "Invalid email format."));
            if (string.IsNullOrWhiteSpace(pw))
                throw new Exception(T("비밀번호를 입력하세요.", "Please enter your password."));

            await FireBaseAuthManager.Instance.LoginAsync(email, pw);

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
            ToastMessageManager.instance.ShowToast(e.Message, e.Message);
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

            if (string.IsNullOrWhiteSpace(name)) throw new Exception(T("이름은 필수입니다.", "Name is required."));
            if (!IsValidEmail(email)) throw new Exception(T("이메일 형식이 올바르지 않습니다.", "Invalid email format."));
            if (!IsStrongPassword(pw)) throw new Exception(T("비밀번호는 6자 이상이어야 합니다.", "Password must be at least 6 characters."));
            if (pw != pw2) throw new Exception(T("비밀번호 확인이 일치하지 않습니다.", "Password confirmation does not match."));
            if (string.IsNullOrWhiteSpace(nick)) throw new Exception(T("닉네임은 필수입니다.", "Nickname is required."));

            await FireBaseAuthManager.Instance.RegisterAsync(name, email, pw, nick, _pickedImageBytes, _pickedContentType, storageBucketUrl);

            inEmail.text = email;
        }
        catch (Exception e)
        {
            ToastMessageManager.instance.ShowToast(e.Message, e.Message);
        }
    }

    // 3) 비밀번호 찾기(재설정 메일 발송) 버튼
    public async void OnClickSendResetPasswordEmail()
    {
        try
        {
            string email = fgEmail.text.Trim();
            if (!IsValidEmail(email)) throw new Exception(T("이메일 형식이 올바르지 않습니다.", "Invalid email format."));

            await FireBaseAuthManager.Instance.SendPasswordResetEmailAsync(email);
        }
        catch (Exception e)
        {
            ToastMessageManager.instance.ShowToast(e.Message, e.Message);
        }
    }
    #endregion

    #region 회원 가입 프로필 이미지

    public void OnClickPickSignUpPhoto()
    {
        try
        {
            var exts = new[]
            {
            new ExtensionFilter("Image Files", "png", "jpg", "jpeg"),
        };

            string startDir = string.IsNullOrWhiteSpace(defaultOpenFolder)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
                : defaultOpenFolder;

            string[] paths = StandaloneFileBrowser.OpenFilePanel("프로필 이미지 선택", startDir, exts, false);
            if (paths == null || paths.Length == 0) return;

            string path = paths[0];
            if (!File.Exists(path))
                throw new Exception(T("파일이 존재하지 않습니다.", "File does not exist."));

            long bytesLen = new FileInfo(path).Length;
            long maxBytes = (long)maxUploadMB * 1024L * 1024L;
            if (bytesLen > maxBytes)
                throw new Exception(T($"이미지 용량이 너무 큽니다. ({maxUploadMB}MB 이하)",
                                      $"Image file is too large. (Max {maxUploadMB}MB)"));

            _pickedImageBytes = File.ReadAllBytes(path);

            string ext = Path.GetExtension(path).ToLowerInvariant();
            _pickedContentType = (ext == ".png") ? "image/png" : "image/jpeg";

            ApplyPreview(_pickedImageBytes); // 미리보기
        }
        catch (Exception e)
        {
            ToastMessageManager.instance.ShowToast(e.Message, e.Message);
        }
    }

    void ApplyPreview(byte[] imageBytes)
    {
        if (_previewTex == null)
            _previewTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);

        if (!_previewTex.LoadImage(imageBytes))
            throw new Exception(T("이미지 로드 실패(지원되지 않는 파일일 수 있음).",
                            "Failed to load image (unsupported file maybe)."));

        if (previewRawImage != null)
            previewRawImage.texture = _previewTex;
    }

    #endregion
}
