using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Firebase.Auth;
using Firebase.Database;

using SFB;  // StandaloneFileBrowser 네임스페이스

public class ProflieManger : MonoBehaviour
{
    #region UI 변수
    [Header("Inputs")]
    [SerializeField] TMP_InputField nickName;

    [SerializeField] TMP_InputField currentPassword;

    [SerializeField] TMP_InputField changePassword;

    [Header("Toggle")]
    [SerializeField] Toggle showPassword;

    [Header("Button")]
    [SerializeField] Button[] btnProfile;

    [Header("Profile Photo")]
    [SerializeField] Button btnPickPhoto;
    [SerializeField] RawImage previewRawImage; //  미리보기
    [SerializeField] string defaultOpenFolder = ""; // 비우면 "내 문서" 등 기본 위치
    [SerializeField] int maxUploadMB = 5;

    // 네 Storage 버킷 주소로 바꿔줘 (Firebase 콘솔 Storage에서 확인 가능)
    // 예: "gs://burumabul.appspot.com"
    [SerializeField] string storageBucketUrl = "gs://YOUR_BUCKET.appspot.com";

    Texture2D _previewTex;

    DatabaseReference Root => FirebaseDatabase.DefaultInstance.RootReference;
    #endregion

    #region Toggle
    public void ToggleProfilePasswordOnOff()
    {
        TogglePassword(currentPassword, showPassword.isOn);
        TogglePassword(changePassword, showPassword.isOn);
    }

    void TogglePassword(TMP_InputField field, bool show)
    {
        field.contentType = show ? TMP_InputField.ContentType.Standard : TMP_InputField.ContentType.Password;
        field.ForceLabelUpdate();
    }
    #endregion

}
