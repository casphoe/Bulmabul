using System.Collections;
using System.Collections.Generic;
using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Threading.Tasks;
using Firebase.Auth;
using Firebase.Storage;
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

    Texture2D _previewTex;

    DatabaseReference Root => FirebaseDatabase.DefaultInstance.RootReference;

    // ===== 저장 버튼 활성/비활성용 =====
    string _lastCannotReason = null;
    bool _lastCanSave = false;


    // 사진 변경 “대기” 데이터(저장 눌렀을 때 업로드)
    byte[] pendingPhotoBytes;
    string pendingPhotoContentType; // image/png or image/jpeg
    string pendingPhotoFileName;    // profile.png or profile.jpg
    bool hasPendingPhoto => pendingPhotoBytes != null && pendingPhotoBytes.Length > 0;

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

    private void OnEnable()
    {
        // 패널 열릴 때마다 항상 최신 계정값/입력칸 초기화
        LoadFromCurrentAccountToUI();

        // 저장 대기중 사진도 초기화(원하면 유지해도 됨)
        pendingPhotoBytes = null;
        pendingPhotoContentType = null;
        pendingPhotoFileName = null;

        // 저장 버튼 상태 갱신
        RefreshSaveButtonState();
    }

    private void Start()
    {      
        // btnProfile[0] = 수정/저장
        if (btnProfile != null && btnProfile.Length > 0 && btnProfile[0] != null)
            btnProfile[0].onClick.AddListener(OnClickEditOrSave);

        // btnProfile[1] = 취소(원복 + 뒤로가기)
        if (btnProfile != null && btnProfile.Length > 1 && btnProfile[1] != null)
            btnProfile[1].onClick.AddListener(OnClickCancel);

        if (nickName) nickName.onValueChanged.AddListener(_ => RefreshSaveButtonState());
        if (currentPassword) currentPassword.onValueChanged.AddListener(_ => RefreshSaveButtonState());
        if (changePassword) changePassword.onValueChanged.AddListener(_ => RefreshSaveButtonState());

    }

    #endregion

    #region 프로필 사진 선택
    // =========================
    // 버튼: 사진 선택
    // =========================
    public async void OnClickPickProfilePhoto()
    {     
        await SafeRun(async () =>
        {
            // 1) 파일 선택창
            var exts = new[]
            {
            new ExtensionFilter("Image Files", "png", "jpg", "jpeg"),
            new ExtensionFilter("PNG", "png"),
            new ExtensionFilter("JPG", "jpg", "jpeg"),
        };

            string startDir = string.IsNullOrWhiteSpace(defaultOpenFolder)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
                : defaultOpenFolder;

            string[] paths = StandaloneFileBrowser.OpenFilePanel("프로필 이미지 선택", startDir, exts, false);
            if (paths == null || paths.Length == 0) return;

            string path = paths[0];
            if (!File.Exists(path)) throw new Exception("파일이 존재하지 않습니다.");

            // 2) 용량 제한
            long bytesLen = new FileInfo(path).Length;
            long maxBytes = (long)maxUploadMB * 1024L * 1024L;
            if (bytesLen > maxBytes) throw new Exception($"이미지 용량이 너무 큽니다. ({maxUploadMB}MB 이하)");

            // 3) 로드 + 미리보기
            byte[] bytes = File.ReadAllBytes(path);
            ApplyPreview(bytes);

            // 4) 업로드는 “저장” 버튼에서 하도록 대기 저장만 해둠
            string ext = Path.GetExtension(path).ToLowerInvariant();
            pendingPhotoContentType = (ext == ".png") ? "image/png" : "image/jpeg";
            pendingPhotoFileName = (pendingPhotoContentType == "image/png") ? "profile.png" : "profile.jpg";
            pendingPhotoBytes = bytes;

            ShowToastByLang(
                "사진이 선택되었습니다. 저장을 누르면 반영됩니다.",
                "Photo selected. Press Save to apply."
            );
        });
    }

    // =========================
    // 미리보기 적용
    // =========================
    void ApplyPreview(byte[] imageBytes)
    {
        if (_previewTex == null)
            _previewTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);

        if (!_previewTex.LoadImage(imageBytes))
            throw new Exception("이미지 로드 실패(지원되지 않는 파일일 수 있음).");

        if (previewRawImage != null)
            previewRawImage.texture = _previewTex;
    }

    // =========================
    // 공통 래퍼
    // =========================
    async Task SafeRun(Func<Task> action)
    {
        try
        {
            SetAllButtons(false);
            await action();
        }
        catch (Exception e)
        {
            ToastMessageManager.instance.ShowToast(e.Message);
            Debug.LogWarning(e);
        }
        finally
        {
            SetAllButtons(true);
        }
    }

    async Task<string> UploadProfilePhotoAsync(string uid, byte[] bytes, string contentType, string fileName)
    {
        var storage = FirebaseStorage.DefaultInstance;

        //  버킷 문자열 헷갈리면 이게 가장 안전함
        var rootRef = storage.RootReference;

        var fileRef = rootRef.Child("users").Child(uid).Child(fileName);

        var metadata = new MetadataChange { ContentType = contentType };
        await fileRef.PutBytesAsync(bytes, metadata);

        var url = await fileRef.GetDownloadUrlAsync();
        return url.ToString();
    }
    #endregion

    #region 닉네임 변경 & 비밀번호 수정

    async Task ChangeNicknameAsync(string uid, string newNickRaw)
    {
        var fb = FireBaseAuthManager.Instance;

        string oldNickKey = (fb.CurrentAccount.NickName ?? "").Trim();
        string newNickKey = NicknameService.ToNickKey(newNickRaw);

        // 기존과 같으면 무시
        if (newNickKey == oldNickKey)
            return;

        // 1) 선점(중복 체크) - 중복이면 여기서 예외
        await NicknameService.ClaimAsync(uid, newNickRaw);

        try
        {
            // 2) users/{uid}/nick 저장은 key로
            await Root.Child("users").Child(uid).Child("nick").SetValueAsync(newNickKey);

            //  3) 로컬도 key로 저장
            fb.CurrentAccount.NickName = newNickKey;

            //  4) 이전 닉 해제(이전 값이 있을 때만)
            if (!string.IsNullOrWhiteSpace(oldNickKey))
                await NicknameService.ReleaseAsync(uid, oldNickKey);
        }
        catch
        {
            // 실패 시 방금 선점한 닉 해제
            try { await NicknameService.ReleaseAsync(uid, newNickRaw); } catch { }
            throw;
        }
    }

    async Task ChangePasswordAsync(string curPw, string newPw)
    {
        var fb = FireBaseAuthManager.Instance;
        var user = fb.CurrentUser;

        if (newPw.Length < 6) throw new Exception("새 비밀번호는 6자 이상이어야 합니다.");
        if (newPw == curPw) throw new Exception("새 비밀번호가 현재 비밀번호와 같습니다.");

        var cred = EmailAuthProvider.GetCredential(user.Email, curPw);
        await user.ReauthenticateAsync(cred);
        await user.UpdatePasswordAsync(newPw);
    }

    #endregion

    public void OnClickEditOrSave()
    {
        if (!CanSaveNow(out string reason))
        {
            ToastMessageManager.instance.ShowToast(reason);
            return;
        }
        SaveAllChanges();
    }

    async void SaveAllChanges()
    {
        await SafeRun(async () =>
        {
            var fb = FireBaseAuthManager.Instance;
            fb.EnsureReady();

            var user = fb.CurrentUser;
            if (user == null) throw new Exception("로그인 상태가 아닙니다.");
            if (fb.CurrentAccount == null) throw new Exception("CurrentAccount가 없습니다.");

            // 1) 닉네임 변경(입력되어 있고, 기존과 다를 때만) - key 기준으로 비교
            string newNickRaw = nickName.text?.Trim() ?? "";
            string oldNickKey = (fb.CurrentAccount.NickName ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(newNickRaw))
            {
                string newNickKey = NicknameService.ToNickKey(newNickRaw);

                // 기존 key와 같으면 "무시"
                if (newNickKey != oldNickKey)
                    await ChangeNicknameAsync(user.UserId, newNickRaw);
            }

            // 2) 비밀번호 변경(둘 다 입력되었을 때만)
            string curPw = currentPassword.text?.Trim();
            string newPw = changePassword.text?.Trim();
            if (!string.IsNullOrWhiteSpace(curPw) || !string.IsNullOrWhiteSpace(newPw))
            {
                // 둘 중 하나만 입력된 경우 방지
                if (string.IsNullOrWhiteSpace(curPw) || string.IsNullOrWhiteSpace(newPw))
                    throw new Exception("비밀번호 변경은 현재/새 비밀번호를 모두 입력해야 합니다.");

                await ChangePasswordAsync(curPw, newPw);

                // 성공했으면 입력칸 비우기
                currentPassword.text = "";
                changePassword.text = "";
            }

            // 3) 프로필 사진 업로드(선택된 경우에만)
            if (hasPendingPhoto)
            {
                string photoUrl = await UploadProfilePhotoAsync(user.UserId, pendingPhotoBytes, pendingPhotoContentType, pendingPhotoFileName);

                // RTDB 저장
                await Root.Child("users").Child(user.UserId).Child("photoUrl").SetValueAsync(photoUrl);

                // 로컬 Account 반영
                fb.CurrentAccount.PhotoUrl = photoUrl;

                // 대기 데이터 비우기
                pendingPhotoBytes = null;
                pendingPhotoContentType = null;
                pendingPhotoFileName = null;
            }

            // 4) 최종 Account 저장(네 방식)
            await AccountCloudStore.SaveFullAsync(fb.CurrentAccount);

            ShowToastByLang("프로필 저장 완료!", "Profile saved!");
            LoadFromCurrentAccountToUI();
            LobbyUIManager.instance.RefreshPlayerUI();
            LobbyUIManager.instance.profilePanel.gameObject.SetActive(false);
        });
    }

    void LoadFromCurrentAccountToUI()
    {
        var fb = FireBaseAuthManager.Instance;
        if (fb == null || fb.CurrentAccount == null) return;

        nickName.text = fb.CurrentAccount.NickName ?? "";

        currentPassword.text = "";
        changePassword.text = "";
        if (showPassword) showPassword.isOn = false;
        ToggleProfilePasswordOnOff();

        // photoUrl 미리보기까지 로드하려면(옵션) URL에서 이미지 다운로드해서 previewRawImage에 세팅 필요
        // (원하면 그 코드도 바로 만들어줄게)
    }


    public void OnClickCancel()
    {
        // 1) 입력값 비우기
        if (nickName) nickName.text = "";
        if (currentPassword) currentPassword.text = "";
        if (changePassword) changePassword.text = "";

        // 2) 비밀번호 표시 토글 초기화(선택)
        if (showPassword) showPassword.isOn = false;
        ToggleProfilePasswordOnOff();

        // 3) 프로필 이미지 미리보기 제거
        if (previewRawImage) previewRawImage.texture = null;

        // Texture2D도 참조 끊고 싶으면(메모리 정리)
        if (_previewTex != null)
        {
            Destroy(_previewTex);
            _previewTex = null;
        }

        // 4) "저장 대기중인 사진" 데이터 폐기 (너가 pending 방식이면)
        pendingPhotoBytes = null;
        pendingPhotoContentType = null;
        pendingPhotoFileName = null;

        // 5) 패널 닫기(원하는 방식으로)
        LobbyUIManager.instance.profilePanel.gameObject.SetActive(false);
    }

    void SetAllButtons(bool on)
    {
        if (btnPickPhoto) btnPickPhoto.interactable = on;
        if (btnProfile != null)
            for (int i = 0; i < btnProfile.Length; i++)
                if (btnProfile[i]) btnProfile[i].interactable = on;
    }

    #region 프로필 수정 가능한 상태인지 판단

    void RefreshSaveButtonState()
    {
        if (btnProfile == null || btnProfile.Length == 0 || btnProfile[0] == null)
            return;

        bool canSave = CanSaveNow(out string reason);
        btnProfile[0].interactable = canSave;

        _lastCanSave = canSave;
        _lastCannotReason = reason;
    }

    bool CanSaveNow(out string reason)
    {
        reason = "";

        var fb = FireBaseAuthManager.Instance;
        if (fb == null || fb.CurrentAccount == null)
        {
            SetReason(out reason,
             kor: "계정 정보가 없습니다.",
             eng: "Account information is missing.");
            return false;
        }

        // ===== 1) 닉네임 변경 여부 (key 기준) =====
        string newNickRaw = (nickName != null ? nickName.text : "").Trim();
        string oldNickKey = (fb.CurrentAccount.NickName ?? "").Trim(); //key로 저장된다고 가정

        bool nickChanged = false;
        if (!string.IsNullOrWhiteSpace(newNickRaw))
        {
            try
            {
                string newNickKey = NicknameService.ToNickKey(newNickRaw);

                // 기존과 같으면 변경 아님(무시)
                nickChanged = (newNickKey != oldNickKey);
            }
            catch
            {
                SetReason(out reason,
                 kor: "닉네임 형식이 올바르지 않습니다.",
                 eng: "Invalid nickname format.");
                return false;
            }
        }

        // ===== 2) 비밀번호 변경 여부 =====
        string curPw = (currentPassword != null ? currentPassword.text : "").Trim();
        string newPw = (changePassword != null ? changePassword.text : "").Trim();

        bool pwAnyInput = !string.IsNullOrWhiteSpace(curPw) || !string.IsNullOrWhiteSpace(newPw);
        bool pwChanged = false;

        if (pwAnyInput)
        {
            if (string.IsNullOrWhiteSpace(curPw) || string.IsNullOrWhiteSpace(newPw))
            {
                SetReason(out reason,
                kor: "비밀번호 변경은 현재/새 비밀번호를 모두 입력해야 합니다.",
                eng: "To change password, enter both current and new passwords.");
                return false;
            }
            if (newPw.Length < 6)
            {
                SetReason(out reason,
                    kor: "새 비밀번호는 6자 이상이어야 합니다.",
                    eng: "New password must be at least 6 characters.");
                return false;
            }

            if (newPw == curPw)
            {
                SetReason(out reason,
                    kor: "새 비밀번호가 현재 비밀번호와 같습니다.",
                    eng: "New password is the same as the current password.");
                return false;
            }

            pwChanged = true;
        }

        // ===== 3) 사진 변경 여부 =====
        bool photoChanged = hasPendingPhoto;

        // ===== 최종: 변경이 하나라도 있어야 저장 가능 =====
        bool anyChange = nickChanged || pwChanged || photoChanged;
        if (!anyChange)
        {
            SetReason(out reason,
                kor: "변경된 내용이 없습니다.",
                eng: "No changes to save.");
            return false;
        }

        return true;
    }

    #endregion

    public void ProfileSaveOnOff(bool isActive)
    {
        btnProfile[0].interactable = isActive;
    }

    #region Toast Helper (한/영)
    private void ShowToastByLang(string kor, string eng)
    {
        var lang = (LaguageManager.Instance != null)
            ? LaguageManager.Instance.currentLang
            : Lauaguage.Kor;

        string msg = (lang == Lauaguage.Eng) ? eng : kor;

        if (ToastMessageManager.instance != null)
            ToastMessageManager.instance.ShowToast(msg);
        else
            Debug.LogWarning($"[Toast missing] {msg}");
    }

    private static bool IsEnglish()
    {
        return (LaguageManager.Instance != null && LaguageManager.Instance.currentLang == Lauaguage.Eng);
    }

    private static void SetReason(out string reason, string kor, string eng)
    {
        reason = IsEnglish() ? eng : kor;
    }
    #endregion
}
