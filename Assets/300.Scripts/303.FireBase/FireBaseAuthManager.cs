using System;
using System.Threading.Tasks;
using Firebase;
using Firebase.Auth;
using UnityEngine;
using TMPro;
using System.Collections;

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
    public Account CurrentAccount { get; private set; }

    // 초기화 완료 여부
    public bool IsReady { get; private set; }
    #region 토스 메시지 설정 변수
    [Header("토스 메시지")]
    [SerializeField] TextMeshProUGUI toasstMessage;

    [Header("토스트 설정")]
    [SerializeField] float toastShowSeconds = 1.2f; // 완전히 보이는 유지 시간
    [SerializeField] float toastFadeSeconds = 0.8f; // 페이드 아웃 시간

    Coroutine toastRoutine;

    #endregion

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

        if (toasstMessage != null)
        {
            var c = toasstMessage.color;
            c.a = 0f;
            toasstMessage.color = c;
            toasstMessage.text = "";
        }

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
    public async Task RegisterAsync(string email, string password, string nickName)
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

            // 2) 닉네임 선점(중복 체크)
            await NicknameService.ClaimAsync(createdUser.UserId, nickName);

            // 3) Account 생성 + DB 저장
            var acc = CreateDefaultAccount(createdUser, nickName);
            await AccountCloudStore.SaveFullAsync(acc);

            CurrentAccount = acc;

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
            await LoadAccountAfterLoginAsync();

            ShowToast("로그인 성공!");
            Debug.Log($"Login OK: {email} / uid={res.User.UserId} / nick={CurrentAccount?.NickName}");
        }
        catch (Exception e)
        {
            ShowToast($"로그인 실패: {ExtractFriendlyError(e)}");
            //Debug.LogError($"Login Fail: {e}");
            throw;
        }
    }

    /// <summary>
    /// 로그아웃
    /// - 원하면 여기서 강제 저장(autosaver 쓴다면 ForceSaveAllAsync)
    /// </summary>
    public async Task LogoutAsync()
    {
        EnsureReady();

        // 로그아웃 직전 저장하고 싶으면
        if (CurrentAccount != null)
        {
            CurrentAccount.LogoutDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            await AccountCloudStore.SaveFullAsync(CurrentAccount);
        }

        Auth.SignOut();
        CurrentAccount = null;

        Debug.Log("Logout OK");
    }

    /// <summary>
    /// 로그인 후 Account 로드/생성
    /// - DB에 계정이 없으면 기본 계정을 만들고 저장
    /// </summary>
    private async Task LoadAccountAfterLoginAsync()
    {
        var user = Auth.CurrentUser;
        if (user == null) throw new Exception("Not logged in.");

        // DB에 accountEnc 없으면 생성하는 로직 포함
        CurrentAccount = await AccountCloudStore.LoadOrCreateAsync(u => CreateDefaultAccount(u, "Player"));

        // 로그인 날짜 갱신(원하면)
        CurrentAccount.LoginDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        await AccountCloudStore.SaveFullAsync(CurrentAccount);
    }


    /// <summary>
    /// 기본 Account 생성 함수
    /// - 여기서 게임 기본값(초기 돈/출석/인벤 등)을 설정
    /// </summary>
    private Account CreateDefaultAccount(FirebaseUser user, string nickName)
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

            Money = 3000f,

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

    private void EnsureReady()
    {
        if (!IsReady || Auth == null)
            throw new Exception("Firebase is not ready yet. (Wait for Start initialization)");
    }

    #region 토스 메시지 표시
    public void ShowToast(string msg)
    {
        if (toasstMessage == null)
        {
            Debug.LogWarning("toasstMessage(TextMeshProUGUI) is not assigned.");
            return;
        }

        if (toastRoutine != null) StopCoroutine(toastRoutine);
        toastRoutine = StartCoroutine(CoToastFadeOut(msg, toastShowSeconds, toastFadeSeconds));
    }

    IEnumerator CoToastFadeOut(string msg, float showSec, float fadeSec)
    {
        // 1) 텍스트 세팅 + 즉시 알파 1
        toasstMessage.text = msg;

        Color c = toasstMessage.color;
        c.a = 1f;
        toasstMessage.color = c;

        // 2) 잠깐 유지
        if (showSec > 0f)
            yield return new WaitForSeconds(showSec);

        // 3) 페이드 아웃 (1 -> 0)
        float t = 0f;
        float dur = Mathf.Max(0.01f, fadeSec);

        while (t < dur)
        {
            t += Time.unscaledDeltaTime; // UI는 timescale=0에서도 보이게 하려면 unscaled 권장
            float a = Mathf.Lerp(1f, 0f, t / dur);
            c.a = a;
            toasstMessage.color = c;
            yield return null;
        }

        // 4) 완전히 숨김
        c.a = 0f;
        toasstMessage.color = c;
        toasstMessage.text = "";
        toastRoutine = null;
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
}
