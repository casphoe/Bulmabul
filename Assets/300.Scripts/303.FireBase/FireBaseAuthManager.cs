using System;
using System.Threading.Tasks;
using Firebase;
using Firebase.Auth;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using Firebase.Database;
using System.Collections.Generic;
using Firebase.Storage;

/// <summary>
/// Firebase 이메일/비밀번호 회원가입 + 로그인 매니저
/// - "닉네임"은 선택사항이 아니라 필수값으로 처리
/// - 회원가입 성공 시: Auth 생성 → 닉네임 중복 선점(NicknameService.ClaimAsync) → 계정(Account) 생성 → DB 저장
/// - 로그인 성공 시: Auth 로그인 → DB에서 Account 로드(없으면 생성)
/// 
/// 전제:
/// 1) FirebaseAuth, FirebaseDatabase 패키지 임포트 되어 있어야 함
/// 2) Realtime Database 규칙은 /users/{uid} 본인만 접근, /nicknames는 규칙대로 설정
/// 3) NicknameService, AccountCloudStore, AccountAutoSaver(선택) 클래스가 프로젝트에 존재해야 함
/// </summary>
public class FireBaseAuthManager : MonoBehaviour
{
    public static FireBaseAuthManager Instance { get; private set; }

    public FirebaseAuth Auth { get; private set; }
    public FirebaseUser CurrentUser => Auth?.CurrentUser;

    // 로그인 후 메모리에 들고 있을 현재 계정 데이터
    public Account CurrentAccount;

    // 초기화 완료 여부
    public bool IsReady { get; set; }

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private async void Start()
    {
        // Firebase 의존성 체크/자동수정
        var dep = await FirebaseApp.CheckAndFixDependenciesAsync();
        if (dep != DependencyStatus.Available)
        {
            Debug.LogError($"Firebase Dependency Error: {dep}");
            IsReady = false;
            return;
        }

        Auth = FirebaseAuth.DefaultInstance;
        IsReady = true;

        Debug.Log("Firebase Ready.");

        // 앱 재실행 시 이미 로그인 상태일 수 있음 (원하면 자동 로드)
        // if (Auth.CurrentUser != null) await LoadAccountAfterLoginAsync();
    }

    /// <summary>
    /// 회원가입(닉네임 필수)
    /// 흐름:
    /// 1) Auth 계정 생성
    /// 2) 닉네임 선점(중복 체크)
    /// 3) Account 생성 + DB 저장
    /// </summary>
    public async Task RegisterAsync(string name,string email, string password, string nickName, byte[] profileImageBytes, 
    string profileContentType, string storageBucketUrl)
    {
        EnsureReady();

        // 0) 필수 처리(빈 값 방지)
        if (string.IsNullOrWhiteSpace(name))
            Fail("이름은 필수입니다.");

        if (string.IsNullOrWhiteSpace(nickName))
            Fail("닉네임은 필수입니다.");

        if (string.IsNullOrWhiteSpace(email))
            Fail("이메일은 필수입니다.");

        if (string.IsNullOrWhiteSpace(password))
            Fail("비밀번호는 필수입니다.");

        //  프로필 이미지 필수
        if (profileImageBytes == null || profileImageBytes.Length == 0)
            Fail("프로필 이미지는 필수입니다.");

        if (string.IsNullOrWhiteSpace(profileContentType))
            Fail("프로필 이미지 타입이 없습니다.");

        if (profileContentType != "image/png" && profileContentType != "image/jpeg")
            Fail("프로필 이미지는 PNG 또는 JPG만 가능합니다.");


        FirebaseUser createdUser = null;
        bool nickClaimed = false;
        bool nameClaimed = false;

        string stage = "시작";
        string uploadedObjectPath = null; // users/{uid}/profile.png

        try
        {
            // 1) Auth 회원가입
            stage = "Auth 계정 생성";
            AuthResult res = await Auth.CreateUserWithEmailAndPasswordAsync(email, password);
            createdUser = res.User;

            // 2) 닉네임 선점
            stage = "닉네임 중복 선점";
            await NicknameService.ClaimAsync(createdUser.UserId, nickName);
            nickClaimed = true;

            // 3) 이름 선점
            stage = "이름 중복 선점";
            await NameService.ClaimAsync(createdUser.UserId, name);
            nameClaimed = true;

            // 4) 프로필 이미지 업로드
            stage = "프로필 이미지 업로드";

            var storage = FirebaseStorage.DefaultInstance;

            // 디버그로 현재 프로젝트 기본 버킷 확인
            Debug.Log($"[Storage] Default bucket = {storage.App.Options.StorageBucket}");

            
            var rootRef = storage.RootReference;

            string fileName = (profileContentType == "image/png") ? "profile.png" : "profile.jpg";
            var fileRef = rootRef.Child("users").Child(createdUser.UserId).Child(fileName);

            var metadata = new MetadataChange { ContentType = profileContentType };
            await fileRef.PutBytesAsync(profileImageBytes, metadata);

            var url = await fileRef.GetDownloadUrlAsync();
            string photoUrl = url.ToString();

            // 5) Account 저장
            stage = "계정 데이터 저장";
            var acc = CreateDefaultAccount(createdUser, name, nickName, photoUrl);
            await AccountCloudStore.SaveFullAsync(acc);

            var verify = await AccountCloudStore.LoadOrThrowAsync();
            Debug.Log($"[VERIFY] DiceCount={verify.DiceInventory?.Count}, AttendMonth={verify.AttendanceMonthKey}, Login={verify.LoginDate}");

            CurrentAccount = acc;
            AuthUIController.instance.ShowSignIn();
            ShowToast("회원가입 성공!");
            Debug.Log($"Register OK: {email} / uid={createdUser.UserId} / nick={nickName}");
        }
        catch (Exception e)
        {
            string reason = Friendly(e);
            string userMsg = $"회원가입 실패({stage}): {reason}";

            Debug.LogWarning($"[RegisterFail] stage={stage}\n{e}");
            ShowToast(userMsg);

            //  선점한 것들 반환
            try { if (nameClaimed && createdUser != null) await NameService.ReleaseAsync(createdUser.UserId, name); } catch { }
            try { if (nickClaimed && createdUser != null) await NicknameService.ReleaseAsync(createdUser.UserId, nickName); } catch { }

            // Auth 생성만 되고 중간에서 실패하면 계정이 반쪽이므로 삭제 롤백
            try
            {
                if (createdUser != null && Auth.CurrentUser != null && Auth.CurrentUser.UserId == createdUser.UserId)
                    await Auth.CurrentUser.DeleteAsync();
            }
            catch (Exception rollbackErr)
            {
                Debug.LogWarning($"Register rollback(DeleteAsync) failed: {rollbackErr}");
            }

            //  UI(OnClickRegister)에서 e.Message로도 보이게
            throw new Exception(userMsg, e);
        }
    }

    /// <summary>
    /// 로그인
    /// 흐름:
    /// 1) Auth 로그인
    /// 2) DB에서 Account 로드(없으면 생성)
    /// </summary>
    public async Task LoginAsync(string email, string password)
    {
        EnsureReady();

        try
        {
            AuthResult res = await Auth.SignInWithEmailAndPasswordAsync(email, password);
            // 여기서 계정 없으면 예외 발생
            await LoadAccountAfterLoginAsync();

            ShowToast("로그인 성공!");
            Debug.Log($"Login OK: {email} / uid={res.User.UserId} / nick={CurrentAccount?.NickName}");
            StartCoroutine(SceneMove(1,1));
        }
        catch (Exception e)
        {
            // Auth는 성공했는데 Account가 없는 경우도 있으니 안전하게 signout
            try { Auth?.SignOut(); } catch { }

            CurrentAccount = null;

            ShowToast($"로그인 실패: {ExtractFriendlyError(e)}");
            //Debug.LogError($"Login Fail: {e}");
            throw;
        }
    }

    IEnumerator SceneMove(float timer, int num)
    {
        yield return new WaitForSeconds(timer);
        SceneManager.LoadScene(num);
    }

    /// <summary>
    /// 로그인 후 Account 로드/생성
    /// - DB에 계정이 없으면 기본 계정을 만들고 저장
    /// </summary>
    private async Task LoadAccountAfterLoginAsync()
    {
        var user = Auth.CurrentUser;
        if (user == null) throw new Exception("Not logged in.");

        CurrentAccount = await AccountCloudStore.LoadOrThrowAsync();

        // 로그인 날짜 갱신(원하면)
        CurrentAccount.LoginDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        await AccountCloudStore.SaveFullAsync(CurrentAccount);
    }


    /// <summary>
    /// 기본 Account 생성 함수
    /// - 여기서 게임 기본값(초기 돈/출석/인벤 등)을 설정
    /// </summary>
    private Account CreateDefaultAccount(FirebaseUser user, string name, string nickName, string photoUrl)
    {
        // 시작 주사위 1개 지급
        var starterDice = new OwnedDice
        {
            Grade = DiceGrade.Common,
            Star = 1,
            Level = 1,
            Count = 1,
            Exp = 0
        };

        var acc = new Account
        {
            Name = name.Trim(),
            NickName = nickName.Trim(),
            Email = user.Email,
            PhotoUrl = photoUrl,

            LoginDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            LogoutDate = "",

            Cash = 3000f,      

            AccountLevel = 1,
            AccountExp = 0,

            LastAttendanceDate = "",
            AttendanceMonthKey = DateTime.Now.ToString("yyyy-MM"),
            AttendanceCountThisMonth = 0,

            ClaimedAttendanceDays = new System.Collections.Generic.List<int>(),
            DiceInventory = new System.Collections.Generic.List<OwnedDice>(),
            EquippedDice = null
        };

        acc.DiceInventory.Add(starterDice);
        acc.EquippedDice = starterDice;

        return acc;
    }
    //비밀번호 찾기(이메일로 재설정 메일 발송)
    public async Task SendPasswordResetEmailAsync(string email)
    {
        EnsureReady();

        if (string.IsNullOrWhiteSpace(email))
            throw new Exception("이메일을 입력하세요.");

        await Auth.SendPasswordResetEmailAsync(email.Trim());
        ShowToast("비밀번호 재설정 메일을 전송했습니다.");
    }

    public void EnsureReady()
    {
        if (!IsReady || Auth == null)
            throw new Exception("Firebase is not ready yet. (Wait for Start initialization)");
    }

    #region 토스 메시지 표시
    public void ShowToast(string msg)
    {
        if (AuthUIController.instance == null || AuthUIController.instance.toasstMessage == null)
        {
            Debug.LogWarning($"Toast(UI 없음): {msg}");
            return;
        }

        AuthUIController.instance.ShowToast(msg);
    }

    void Fail(string msg)
    {
        ShowToast(msg);               //  여기서 토스트
        throw new Exception(msg);     // 흐름 중단
    }


    private string ExtractFriendlyError(Exception e)
    {
        // 닉네임 관련은 그대로 보여주기
        if (e.Message.Contains("닉네임")) return e.Message;

        // Firebase 공통 예외
        if (e is FirebaseException fe)
        {
            // Auth쪽 에러코드로 해석(가능한 경우)
            // ErrorCode는 int로 들어옴
            try
            {
                var authErr = (AuthError)fe.ErrorCode;
                return $"{AuthErrorToKorean(authErr)} ({authErr})";
            }
            catch
            {
                return $"Firebase 오류 코드: {fe.ErrorCode} / {fe.Message}";
            }
        }

        return e.Message;
    }

    private string AuthErrorToKorean(AuthError err)
    {
        switch (err)
        {
            case AuthError.InvalidEmail: return "이메일 형식이 올바르지 않습니다.";
            case AuthError.WrongPassword: return "비밀번호가 틀렸습니다.";
            case AuthError.UserNotFound: return "존재하지 않는 계정입니다.";
            case AuthError.EmailAlreadyInUse: return "이미 사용 중인 이메일입니다.";
            case AuthError.WeakPassword: return "비밀번호가 너무 약합니다.";
            case AuthError.MissingEmail: return "이메일을 입력하세요.";
            case AuthError.MissingPassword: return "비밀번호를 입력하세요.";
            case AuthError.NetworkRequestFailed: return "네트워크 연결을 확인하세요.";
            default: return "인증 처리 중 오류가 발생했습니다.";
        }
    }


    private Exception Unwrap(Exception e)
    {
        // Firebase는 AggregateException으로 감싸져 오는 경우가 많음
        if (e is AggregateException ae && ae.InnerExceptions != null && ae.InnerExceptions.Count > 0)
            return Unwrap(ae.InnerExceptions[0]);
        if (e.InnerException != null) return Unwrap(e.InnerException);
        return e;
    }

    private string Friendly(Exception e)
    {
        e = Unwrap(e);

        // 닉네임/이름 중복은 우리가 던진 메시지 그대로 쓰는 게 제일 명확
        if (e.Message.Contains("닉네임")) return e.Message;
        if (e.Message.Contains("이름")) return e.Message;

        // RTDB Rules 권한 문제 흔함
        string msg = e.Message ?? "";
        if (msg.Contains("Permission denied") || msg.Contains("permission_denied"))
            return "DB 권한 거부입니다. Realtime Database Rules에 /names, /nicknames 쓰기 허용이 있는지 확인하세요.";

        if (e is FirebaseException fe)
        {
            // Auth 에러면 AuthError로 해석 가능
            try
            {
                var authErr = (AuthError)fe.ErrorCode;
                return AuthErrorToKorean(authErr);
            }
            catch
            {
                return $"Firebase 오류: {fe.Message}";
            }
        }

        return string.IsNullOrWhiteSpace(msg) ? e.GetType().Name : msg;
    }
    #endregion

    #region 회원 탈퇴
    public async Task DeleteCurrentAccountAsync(string password)
    {
        EnsureReady();

        var user = Auth.CurrentUser;
        if (user == null)
            throw new Exception("로그인 상태가 아닙니다.");

        try
        {
            // 1) 먼저 입력 없이 바로 시도 (방금 로그인했으면 통과하는 경우 많음)
            await DeleteAllUserDataAndAuthAsync(user);
        }
        catch (FirebaseException fe)
        {
            // RequiresRecentLogin이면 그때만 비번 요구
            if ((AuthError)fe.ErrorCode != AuthError.RequiresRecentLogin)
                throw;

            if (string.IsNullOrWhiteSpace(password))
                throw new Exception("보안을 위해 비밀번호를 한 번 더 입력해 주세요.");

            // 2) 재인증(이메일은 CurrentUser에서 가져오면 됨)
            var cred = EmailAuthProvider.GetCredential(user.Email, password);
            await user.ReauthenticateAsync(cred);

            // 3) 재시도
            await DeleteAllUserDataAndAuthAsync(user);
        }
    }

    private async Task DeleteAllUserDataAndAuthAsync(FirebaseUser user)
    {
        string uid = user.UserId;
        // Storage 프로필 이미지 삭제(있으면)
        try
        {
            var storage = FirebaseStorage.DefaultInstance;
            var rootRef = storage.RootReference;

            // 둘 다 시도 (없으면 예외 -> 무시)
            try { await rootRef.Child("users").Child(uid).Child("profile.png").DeleteAsync(); } catch { }
            try { await rootRef.Child("users").Child(uid).Child("profile.jpg").DeleteAsync(); } catch { }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DeleteAccount] Storage delete failed: {e.Message}");
            // 스토리지 삭제 실패해도 탈퇴 자체는 계속 진행할지 정책 선택
        }

        // DB 삭제(닉네임 같이 쓰면 같이 제거)
        var root = FirebaseDatabase.DefaultInstance.RootReference;

        // 한 번에 원자적으로 지우기 (users + nicknames + names)
        var updates = new Dictionary<string, object>
        {
            [$"users/{uid}"] = null
        };


        // 닉네임 인덱스 제거
        if (CurrentAccount != null && !string.IsNullOrWhiteSpace(CurrentAccount.NickName))
        {
            string nickKey = NicknameService.ToNickKey(CurrentAccount.NickName);
            updates[$"nicknames/{nickKey}"] = null;
        }

        // 이름 인덱스 제거
        if (CurrentAccount != null && !string.IsNullOrWhiteSpace(CurrentAccount.Name))
        {
            string nameKey = NameService.ToNameKey(CurrentAccount.Name);
            updates[$"names/{nameKey}"] = null;
        }

        await root.UpdateChildrenAsync(updates);

        // Auth 삭제
        await user.DeleteAsync();

        // 로컬 정리
        CurrentAccount = null;
        try { Auth.SignOut(); } catch { }

        ShowToast("회원 탈퇴가 완료되었습니다.");
        SceneManager.LoadScene(0);
    }
    #endregion

    #region 로그아웃
    /// <summary>
    /// 로그아웃:
    /// 1) 로그아웃 시간 기록 + 클라우드 저장
    /// 2) FirebaseAuth SignOut
    /// 3) 메모리(CurrentAccount) 정리
    /// 4) UI/씬 복귀
    /// </summary>
    public async Task LogoutToScene0Async()
    {
        EnsureReady();

        var user = Auth.CurrentUser;
        if (user == null)
            throw new Exception("로그인 상태가 아닙니다. (로그아웃 불가)");

        if (CurrentAccount == null)
            throw new Exception("CurrentAccount가 없습니다. 저장 후 로그아웃할 수 없습니다.");

        // 1) 로그아웃 날짜 갱신
        CurrentAccount.LogoutDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        // 2) 저장이 반드시 성공해야 함 (실패하면 여기서 예외 -> 아래 진행 안 됨)
        await AccountCloudStore.SaveFullAsync(CurrentAccount);

        // 3) 저장 성공 후에만 SignOut + 씬 이동
        Auth.SignOut();
        CurrentAccount = null;

        SceneManager.LoadScene(0);

    }
    #endregion
}
