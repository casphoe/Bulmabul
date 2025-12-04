using System;
using System.Threading.Tasks;
using Firebase;
using Firebase.Auth;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using Firebase.Database;
using System.Collections.Generic;

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
    public async Task RegisterAsync(string name,string email, string password, string nickName)
    {
        EnsureReady();

        // 0) 필수 처리(빈 값 방지)
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowToast("이름은 필수입니다.");
            throw new Exception("이름은 필수입니다.");
        }

        if (string.IsNullOrWhiteSpace(nickName))
        {
            ShowToast("닉네임은 필수입니다.");
            throw new Exception("닉네임은 필수입니다.");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            ShowToast("이메일은 필수입니다.");
            throw new Exception("이메일은 필수입니다.");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            ShowToast("비밀번호는 필수입니다.");
            throw new Exception("비밀번호는 필수입니다.");
        }


        FirebaseUser createdUser = null;

        try
        {
            // 1) Auth 회원가입
            AuthResult res = await Auth.CreateUserWithEmailAndPasswordAsync(email, password);
            createdUser = res.User;

            // 2) 닉네임 선점
            await NicknameService.ClaimAsync(createdUser.UserId, nickName);

            // 3) Account 저장
            var acc = CreateDefaultAccount(createdUser, name, nickName);
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
            ShowToast($"회원가입 실패: {ExtractFriendlyError(e)}");
            //Debug.LogError($"Register Fail: {e}");
            // (중요) Auth는 만들어졌는데 닉네임 선점/DB 저장에서 실패하면
            // 계정이 “반쪽”으로 남을 수 있으니 롤백(삭제) 시도
            try
            {
                if (createdUser != null && Auth.CurrentUser != null && Auth.CurrentUser.UserId == createdUser.UserId)
                    await Auth.CurrentUser.DeleteAsync();
            }
            catch (Exception rollbackErr)
            {
                Debug.LogWarning($"Register rollback(DeleteAsync) failed: {rollbackErr}");
            }
            throw;
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
    private Account CreateDefaultAccount(FirebaseUser user, string name, string nickName)
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

        // DB 삭제(닉네임 같이 쓰면 같이 제거)
        var root = FirebaseDatabase.DefaultInstance.RootReference;

        //  한 번에 원자적으로 지우기 (users + nicknames)
        var updates = new Dictionary<string, object>
        {
            [$"users/{uid}"] = null
        };

        if (CurrentAccount != null && !string.IsNullOrWhiteSpace(CurrentAccount.NickName))
        {
            string nickKey = CurrentAccount.NickName.Trim().ToLowerInvariant();
            updates[$"nicknames/{nickKey}"] = null;
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
