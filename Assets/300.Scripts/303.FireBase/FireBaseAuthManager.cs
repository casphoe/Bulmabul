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

    [SerializeField] private PresenceService presence;

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
    /// 3) 이름 선정 (중복 체크)
    /// 4) 프로필 이미지 업로드
    /// 5) Account 생성 + DB 저장
    /// </summary>
    public async Task RegisterAsync(string name, string email, string password, string nickName, byte[] profileImageBytes,
    string profileContentType, string storageBucketUrl)
    {
        EnsureReady();

        // 0) 필수 처리(빈 값 방지)
        if (string.IsNullOrWhiteSpace(name))
            Fail("이름은 필수입니다.", "Name is required.");

        if (string.IsNullOrWhiteSpace(nickName))
            Fail("닉네임은 필수입니다.", "Nickname is required.");

        if (string.IsNullOrWhiteSpace(email))
            Fail("이메일은 필수입니다.", "Email is required.");

        if (string.IsNullOrWhiteSpace(password))
            Fail("비밀번호는 필수입니다.", "Password is required.");

        if (profileImageBytes == null || profileImageBytes.Length == 0)
            Fail("프로필 이미지는 필수입니다.", "Profile image is required.");

        if (string.IsNullOrWhiteSpace(profileContentType))
            Fail("프로필 이미지 타입이 없습니다.", "Profile image content type is missing.");

        if (profileContentType != "image/png" && profileContentType != "image/jpeg")
            Fail("프로필 이미지는 PNG 또는 JPG만 가능합니다.", "Profile image must be PNG or JPG.");


        FirebaseUser createdUser = null;
        bool nickClaimed = false;
        bool nameClaimed = false;

        string stageKor = "시작";
        string stageEng = "Start";

        try
        {
            // 1) Auth 회원가입
            stageKor = "Auth 계정 생성";
            stageEng = "Creating Auth account";
            AuthResult res = await Auth.CreateUserWithEmailAndPasswordAsync(email, password);
            createdUser = res.User;

            // 2) 닉네임 선점
            stageKor = "닉네임 중복 선점";
            stageEng = "Claiming nickname";
            await NicknameService.ClaimAsync(createdUser.UserId, nickName);
            nickClaimed = true;

            // 3) 이름 선점
            stageKor = "이름 중복 선점";
            stageEng = "Claiming name";
            await NameService.ClaimAsync(createdUser.UserId, name);
            nameClaimed = true;

            // 4) 프로필 이미지 업로드
            stageKor = "프로필 이미지 업로드";
            stageEng = "Uploading profile image";

            var storage = FirebaseStorage.DefaultInstance;

            var rootRef = storage.RootReference;

            string fileName = (profileContentType == "image/png") ? "profile.png" : "profile.jpg";
            var fileRef = rootRef.Child("users").Child(createdUser.UserId).Child(fileName);

            var metadata = new MetadataChange { ContentType = profileContentType };
            await fileRef.PutBytesAsync(profileImageBytes, metadata);

            var url = await fileRef.GetDownloadUrlAsync();
            string photoUrl = url.ToString();

            stageKor = "DB 저장";
            stageEng = "Saving DB data";

            var dbRoot = FirebaseDatabase.DefaultInstance.RootReference;

            // nick은 네가 users에는 "원문"으로 저장하는지 "key"로 저장하는지 정책이 중요함.
            // 지금 Register에서는 CreateDefaultAccount에 nickName(원문)을 넣고 있으니 그대로 저장하는 버전으로 작성.
            var updates = new Dictionary<string, object>
            {
                [$"users/{createdUser.UserId}/photoUrl"] = photoUrl,
                [$"users/{createdUser.UserId}/nick"] = nickName.Trim(),

                // 공개 프로필(친구/초대에서 읽을 데이터)
                [$"userPublic/{createdUser.UserId}/photoUrl"] = photoUrl,
                [$"userPublic/{createdUser.UserId}/nick"] = nickName.Trim(),
            };

            await dbRoot.UpdateChildrenAsync(updates);

            // 5) Account 저장
            stageKor = "계정 데이터 저장";
            stageEng = "Saving account data";

            var acc = CreateDefaultAccount(createdUser, name, nickName, photoUrl);
            await AccountCloudStore.SaveFullAsync(acc);

            var verify = await AccountCloudStore.LoadOrThrowAsync();
            Debug.Log($"[VERIFY] DiceCount={verify.DiceInventory?.Count}, AttendMonth={verify.AttendanceMonthKey}, Login={verify.LoginDate}");

            CurrentAccount = acc;
            AuthUIController.instance.ShowSignIn();
            ToastMessageManager.instance.ShowToast("회원가입 성공!", "Sign up successful!");
            Debug.Log($"Register OK: {email} / uid={createdUser.UserId} / nick={nickName}");
        }
        catch (Exception e)
        {
            string reasonKor, reasonEng;
            Friendly(e, out reasonKor, out reasonEng);

            string userMsgKor = $"회원가입 실패({stageKor}): {reasonKor}";
            string userMsgEng = $"Sign up failed ({stageEng}): {reasonEng}";

            Debug.LogWarning($"[RegisterFail] stage={stageKor}/{stageEng}\n{e}");
            ToastMessageManager.instance.ShowToast(userMsgKor, userMsgEng);

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
            throw new Exception(T(userMsgKor, userMsgEng), e);
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

            ToastMessageManager.instance.ShowToast("로그인 성공!", "Login successful!");
            Debug.Log($"Login OK: {email} / uid={res.User.UserId} / nick={CurrentAccount?.NickName}");
            StartCoroutine(SceneMove(1, 1));
        }
        catch (Exception e)
        {
            // Auth는 성공했는데 Account가 없는 경우도 있으니 안전하게 signout
            try { Auth?.SignOut(); } catch { }

            CurrentAccount = null;
            string k, en;
            ExtractFriendlyError(e, out k, out en);
            ToastMessageManager.instance.ShowToast($"로그인 실패: {k}", $"Login failed: {en}");
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

        DiceInventorySort.SortDiceInventory(CurrentAccount);

        // 인벤토리 로드 후 장착 복원
        DiceEquipService.RestoreEquippedDice(CurrentAccount);

        // 로그인 날짜 갱신
        CurrentAccount.LoginDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        // 자동 출석 처리 전에 LoginDate를 먼저 오늘로 갱신하지 않는다.
        // 그래야 저장되어 있던 최근 로그인 날짜가 어제인지, 오늘인지 출석 서비스에서 판단할 수 있다.
        Debug.Log("[Login] Before HandleAfterLoginAsync");
        await AttendanceLoginSessionBridge.HandleAfterLoginAsync(CurrentAccount);
        Debug.Log("[Login] After HandleAfterLoginAsync");

        // 출석 처리 판단이 끝난 뒤, 이번 로그인 날짜를 오늘로 갱신한다.
        // 날짜 비교는 AttendanceService에서 yyyy-MM-dd만 사용한다.
        CurrentAccount.LoginDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        // 로그인 날짜 갱신분 저장
        await AccountCloudStore.SaveFullAsync(CurrentAccount);

        if (presence != null)
            await presence.StartPresenceAsync();
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
            Exp = 0,
            Shard = 0,
            PromoteExp = 0
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
            IsAttendanceCheckedToday = false,
            AttendanceMonthKey = DateTime.Now.ToString("yyyy-MM"),
            AttendanceCountThisMonth = 0,

            ClaimedAttendanceDays = new List<int>(),
            DiceInventory = new List<OwnedDice>(),
            EquippedDice = null
        };

        acc.DiceInventory.Add(starterDice);
        acc.EquippedDice = starterDice;
        acc.EquippedDiceKey = starterDice.EquipKey;

        return acc;
    }
    //비밀번호 찾기(이메일로 재설정 메일 발송)
    public async Task SendPasswordResetEmailAsync(string email)
    {
        EnsureReady();

        if (string.IsNullOrWhiteSpace(email))
            throw new Exception(T("이메일을 입력하세요.", "Please enter your email."));

        await Auth.SendPasswordResetEmailAsync(email.Trim());
        ToastMessageManager.instance.ShowToast("비밀번호 재설정 메일을 전송했습니다.", "Password reset email has been sent.");
    }

    public void EnsureReady()
    {
        if (!IsReady || Auth == null)
            throw new Exception(T(
                "Firebase가 아직 준비되지 않았습니다. (Start 초기화 대기)",
                "Firebase is not ready yet. (Wait for Start initialization)"
            ));
    }

    #region 토스 메시지 표시
    private string T(string kor, string eng)
    {
        if (LaguageManager.Instance == null) return kor;
        return (LaguageManager.Instance.currentLang == Lauaguage.Eng) ? eng : kor;
    }

    private void Fail(string kor, string eng)
    {
        ToastMessageManager.instance.ShowToast(kor, eng);
        throw new Exception(T(kor, eng));
    }


    private void ExtractFriendlyError(Exception e, out string kor, out string eng)
    {
        e = Unwrap(e);

        // 닉네임 관련은 그대로 노출(너가 던진 메시지)
        if (!string.IsNullOrEmpty(e.Message) && e.Message.Contains("닉네임"))
        {
            kor = e.Message;
            eng = e.Message; // 이미 영어로 던질 수도 있으니 그대로
            return;
        }

        if (e is FirebaseException fe)
        {
            try
            {
                var authErr = (AuthError)fe.ErrorCode;
                AuthErrorToText(authErr, out kor, out eng);
                return;
            }
            catch
            {
                kor = $"Firebase 오류 코드: {fe.ErrorCode}";
                eng = $"Firebase error code: {fe.ErrorCode}";
                return;
            }
        }

        kor = string.IsNullOrWhiteSpace(e.Message) ? e.GetType().Name : e.Message;
        eng = kor;
    }

    private void AuthErrorToText(AuthError err, out string kor, out string eng)
    {
        switch (err)
        {
            case AuthError.InvalidEmail:
                kor = "이메일 형식이 올바르지 않습니다.";
                eng = "Invalid email format.";
                break;
            case AuthError.WrongPassword:
                kor = "비밀번호가 틀렸습니다.";
                eng = "Incorrect password.";
                break;
            case AuthError.UserNotFound:
                kor = "존재하지 않는 계정입니다.";
                eng = "Account not found.";
                break;
            case AuthError.EmailAlreadyInUse:
                kor = "이미 사용 중인 이메일입니다.";
                eng = "Email is already in use.";
                break;
            case AuthError.WeakPassword:
                kor = "비밀번호가 너무 약합니다.";
                eng = "Password is too weak.";
                break;
            case AuthError.MissingEmail:
                kor = "이메일을 입력하세요.";
                eng = "Please enter your email.";
                break;
            case AuthError.MissingPassword:
                kor = "비밀번호를 입력하세요.";
                eng = "Please enter your password.";
                break;
            case AuthError.NetworkRequestFailed:
                kor = "네트워크 연결을 확인하세요.";
                eng = "Please check your network connection.";
                break;
            case AuthError.RequiresRecentLogin:
                kor = "보안을 위해 다시 로그인 후 진행해 주세요.";
                eng = "For security, please re-login and try again.";
                break;
            default:
                kor = "인증 처리 중 오류가 발생했습니다.";
                eng = "An authentication error occurred.";
                break;
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

    private void Friendly(Exception e, out string kor, out string eng)
    {
        e = Unwrap(e);

        // 닉네임/이름 중복은 그대로 쓰는 게 제일 명확
        if (!string.IsNullOrEmpty(e.Message) && e.Message.Contains("닉네임"))
        {
            kor = e.Message;
            eng = e.Message;
            return;
        }
        if (!string.IsNullOrEmpty(e.Message) && e.Message.Contains("이름"))
        {
            kor = e.Message;
            eng = e.Message;
            return;
        }

        string msg = e.Message ?? "";

        if (msg.Contains("Permission denied") || msg.Contains("permission_denied"))
        {
            kor = "DB 권한 거부입니다. Realtime Database Rules에 /names, /nicknames 쓰기 허용이 있는지 확인하세요.";
            eng = "Database permission denied. Check RTDB rules for /names and /nicknames write access.";
            return;
        }

        if (e is FirebaseException fe)
        {
            try
            {
                var authErr = (AuthError)fe.ErrorCode;
                AuthErrorToText(authErr, out kor, out eng);
                return;
            }
            catch
            {
                kor = $"Firebase 오류: {fe.Message}";
                eng = $"Firebase error: {fe.Message}";
                return;
            }
        }

        kor = string.IsNullOrWhiteSpace(msg) ? e.GetType().Name : msg;
        eng = kor;
    }
    #endregion

    #region 회원 탈퇴
    public async Task DeleteCurrentAccountAsync(string password)
    {
        EnsureReady();

        var user = Auth.CurrentUser;
        if (user == null)
            throw new Exception(T("로그인 상태가 아닙니다.", "Not logged in."));

        try
        {
            await DeleteAllUserDataAndAuthAsync(user);
        }
        catch (FirebaseException fe)
        {
            // RequiresRecentLogin이면 그때만 비번 요구
            if ((AuthError)fe.ErrorCode != AuthError.RequiresRecentLogin)
            {
                string k, en;
                ExtractFriendlyError(fe, out k, out en);
                ToastMessageManager.instance.ShowToast(
                    $"회원 탈퇴 실패: {k}",
                    $"Account deletion failed: {en}"
                );
                throw;
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                // 여기서 토스트도 같이
                ToastMessageManager.instance.ShowToast(
                    "보안을 위해 비밀번호를 한 번 더 입력해 주세요.",
                    "For security, please enter your password again."
                );
                throw new Exception(T(
                    "보안을 위해 비밀번호를 한 번 더 입력해 주세요.",
                    "For security, please enter your password again."
                ));
            }

            try
            {
                // 재인증
                var cred = EmailAuthProvider.GetCredential(user.Email, password);
                await user.ReauthenticateAsync(cred);

                // 재시도
                await DeleteAllUserDataAndAuthAsync(user);
            }
            catch (Exception e2)
            {
                string k2, en2;
                ExtractFriendlyError(e2, out k2, out en2);
                ToastMessageManager.instance.ShowToast(
                    $"회원 탈퇴 실패: {k2}",
                    $"Account deletion failed: {en2}"
                );
                throw;
            }
        }
        catch (Exception e)
        {
            string k, en;
            ExtractFriendlyError(e, out k, out en);
            ToastMessageManager.instance.ShowToast(
                $"회원 탈퇴 실패: {k}",
                $"Account deletion failed: {en}"
            );
            throw;
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
            [$"users/{uid}"] = null,
            [$"userPublic/{uid}"] = null,
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

        ToastMessageManager.instance.ShowToast("회원 탈퇴가 완료되었습니다.", "Account deletion completed.");
        StartCoroutine(CoMoveSceneAfterToast(0));
    }

    private IEnumerator CoMoveSceneAfterToast(int sceneIndex)
    {
        // 토스트가 1프레임은 그려지도록
        yield return null;
        // 살짝 보여주고 이동 (원하면 0.3~0.5로)
        yield return new WaitForSeconds(0.35f);
        SceneManager.LoadScene(sceneIndex);
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
        {
            ToastMessageManager.instance.ShowToast(
                "로그인 상태가 아닙니다. (로그아웃 불가)",
                "Not logged in. (Cannot logout)"
            );
            throw new Exception(T("로그인 상태가 아닙니다. (로그아웃 불가)", "Not logged in. (Cannot logout)"));
        }

        if (CurrentAccount == null)
        {
            ToastMessageManager.instance.ShowToast(
                "계정 데이터가 없습니다. 저장 후 로그아웃할 수 없습니다.",
                "Account data is missing. Cannot logout without saving."
            );
            throw new Exception(T("계정 데이터가 없습니다. 저장 후 로그아웃할 수 없습니다.",
                                  "Account data is missing. Cannot logout without saving."));
        }

        try
        {
            // 1) 로그아웃 날짜 갱신
            CurrentAccount.LogoutDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            // 2) 저장
            await AccountCloudStore.SaveFullAsync(CurrentAccount);

            // 3) signout
            Auth.SignOut();
            CurrentAccount = null;

            // 성공 토스트 + 씬 이동(딜레이)
            ToastMessageManager.instance.ShowToast(
                "로그아웃 완료!",
                "Logged out successfully!"
            );

            StartCoroutine(CoMoveSceneAfterToast(0));
        }
        catch (Exception e)
        {
            string k, en;
            ExtractFriendlyError(e, out k, out en);

            ToastMessageManager.instance.ShowToast(
                $"로그아웃 실패: {k}",
                $"Logout failed: {en}"
            );
            throw;
        }

    }
    #endregion
}
