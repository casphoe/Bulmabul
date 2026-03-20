using Firebase.Auth;
using Firebase.Database;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Unity.VisualScripting;
using UnityEngine;

public class AdminCheck : MonoBehaviour
{
    [Header("지급 설정")]
    [SerializeField] private int addCashAmount = 10000; // F10 누를 때 지급할 캐시

    [Header("F11 테스트용 주사위 설정")]
    [SerializeField] private int testDiceCount = 1; // 지급할 개수(최소 1)


    private FirebaseAuth _auth;
    private DatabaseReference _dbRoot;

    private bool _isProcessing; // 중복 실행 방지용

    private void Awake()
    {
        _auth = FirebaseAuth.DefaultInstance;
        _dbRoot = FirebaseDatabase.DefaultInstance.RootReference;
    }

    private void Update()
    {
        // 이미 처리 중이면 중복 실행 막기
        if (_isProcessing)
            return;

        // F10 = 관리자 캐시 지급
        if (Input.GetKeyDown(KeyCode.F10))
        {
            _ = GrantAdminCashAsync();
            return;
        }

        // F11 = 관리자 승급 테스트용 주사위 지급
        if (Input.GetKeyDown(KeyCode.F11))
        {
            _ = GrantPromotionTestDiceAsync();
            return;
        }
    }


    /// <summary>
    /// 관리자 UID 해시를 반환한다.
    /// 원본 UID를 한 줄 문자열로 노출하지 않기 위해 나눠서 합친다.
    /// </summary>
    private string GetAdminUidHash()
    {
        string a = "6d0feb57277d4563";
        string b = "54d5df422a6cd342";
        string c = "77d30080f31250ec";
        string d = "007601ae2f79934f";
        return a + b + c + d;
    }

    /// <summary>
    /// 현재 로그인한 사용자가 관리자 계정인지 검사한다.
    /// 검사 방식:
    /// 1) 현재 로그인한 UID를 가져온다.
    /// 2) SHA-256 해시로 변환한다.
    /// 3) 저장된 관리자 UID 해시와 비교한다.
    /// </summary>
    private bool IsAdmin()
    {
        if (_auth == null || _auth.CurrentUser == null)
            return false;

        string currentUid = _auth.CurrentUser.UserId;
        if (string.IsNullOrEmpty(currentUid))
            return false;

        string currentUidHash = ToSha256(currentUid);
        string adminUidHash = GetAdminUidHash();

        return string.Equals(currentUidHash, adminUidHash, StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>
    /// 관리자 계정일 때만 캐시를 지급한다.
    /// </summary>
    private async Task GrantAdminCashAsync()
    {
        _isProcessing = true;

        try
        {
            // 1. 로그인 여부 확인
            if (_auth == null || _auth.CurrentUser == null)
            {
                Debug.LogWarning("[AdminCheck] 로그인된 계정이 없습니다.");
                ShowToast("로그인된 계정이 없습니다.", "No logged-in account.");
                return;
            }

            // 2. 관리자 계정인지 확인
            if (!IsAdmin())
            {
                Debug.LogWarning("[AdminCheck] 관리자 계정이 아닙니다.");
                ShowToast("관리자 계정이 아닙니다.", "This is not an admin account.");
                return;
            }

            string uid = _auth.CurrentUser.UserId;

            // 3. users/{uid}/cash 경로 참조
            DatabaseReference cashRef = _dbRoot.Child("users").Child(uid).Child("cash");

            // 4. 현재 cash 값 읽기
            DataSnapshot beforeSnap = await cashRef.GetValueAsync();

            double currentCash = 0;

            if (beforeSnap.Exists && beforeSnap.Value != null)
            {
                double.TryParse(beforeSnap.Value.ToString(), out currentCash);
            }

            // 5. 캐시 증가
            double nextCash = currentCash + addCashAmount;

            // 6. 증가된 캐시를 Firebase에 저장
            if (FireBaseAuthManager.Instance != null && FireBaseAuthManager.Instance.CurrentAccount != null)
            {
                FireBaseAuthManager.Instance.CurrentAccount.Cash = (float)nextCash;
                Debug.Log($"[AdminCheck] CurrentAccount.Cash 갱신 완료 = {FireBaseAuthManager.Instance.CurrentAccount.Cash}");
            }
            else
            {
                Debug.LogWarning("[AdminCheck] CurrentAccount가 null 입니다.");
            }

            await cashRef.SetValueAsync(nextCash);

            if(FireBaseAuthManager.Instance != null && FireBaseAuthManager.Instance.CurrentAccount != null)
            {
               
                await AccountCloudStore.SaveFullAsync(FireBaseAuthManager.Instance.CurrentAccount);

                Debug.Log("[AdminCheck] accountEnc 사용 구조라면 전체 계정 저장 함수도 같이 호출해야 함");
            }

            // 7) 저장 후 재확인
            DataSnapshot afterSnap = await cashRef.GetValueAsync();
            if (afterSnap.Exists && afterSnap.Value != null)
            {
                Debug.Log($"[AdminCheck] 저장 후 Firebase cash 확인 = {afterSnap.Value}");
            }
            else
            {
                Debug.LogWarning("[AdminCheck] 저장 후 cash 값이 비어 있습니다.");
            }


            // 8. 토스트 출력
            ShowToast(
                $"관리자 보상 지급 완료! 캐시 +{addCashAmount}",
                $"Admin reward applied! Cash +{addCashAmount}"
            );

            LobbyUIManager.instance.RefreshPlayerUI();
        }
        catch (Exception e)
        {
            Debug.LogError($"[AdminCheck] 캐시 지급 중 오류 발생: {e}");
            ShowToast("관리자 캐시 지급 실패", "Failed to grant admin cash.");
        }
        finally
        {
            _isProcessing = false;
        }
    }

    /// <summary>
    /// F11:
    /// 관리자 전용 테스트 기능.
    /// 등급 승급 직전 상태의 노말(Common) 주사위를 인벤토리에 넣는다.
    ///
    /// 승급 가능 최소 조건:
    /// - Common
    /// - Star = 5
    /// - Level = 10
    /// - PromoteExp = 5
    /// </summary>
    private async Task GrantPromotionTestDiceAsync()
    {
        _isProcessing = true;

        try
        {
            // 1. 로그인 여부 확인
            if (_auth == null || _auth.CurrentUser == null)
            {
                Debug.LogWarning("[AdminCheck] 로그인된 계정이 없습니다.");
                ShowToast("로그인된 계정이 없습니다.", "No logged-in account.");
                return;
            }

            // 2. 관리자 계정 확인
            if (!IsAdmin())
            {
                Debug.LogWarning("[AdminCheck] 관리자 계정이 아닙니다.");
                ShowToast("관리자 계정만 사용할 수 있습니다.", "Admin only.");
                return;
            }

            // 3. 현재 계정 확인
            if (FireBaseAuthManager.Instance == null || FireBaseAuthManager.Instance.CurrentAccount == null)
            {
                Debug.LogWarning("[AdminCheck] CurrentAccount가 없습니다.");
                ShowToast("현재 계정 데이터를 찾을 수 없습니다.", "Current account data not found.");
                return;
            }

            Account acc = FireBaseAuthManager.Instance.CurrentAccount;

            if (acc.DiceInventory == null)
                acc.DiceInventory = new System.Collections.Generic.List<OwnedDice>();

            // 4. Common ★5 주사위 찾기
            // 현재 프로젝트는 인벤토리 중복 판단이 Grade + Star 기준으로 많이 사용됨
            // 그래서 Common ★5가 있으면 그 항목을 승급 직전 상태로 맞춰준다.
            OwnedDice target = acc.DiceInventory.Find(x =>
                x != null &&
                x.Grade == DiceGrade.Common &&
                x.Star == 5);

            if (target == null)
            {
                // 없으면 새로 생성
                target = new OwnedDice
                {
                    Grade = DiceGrade.Common,
                    Star = 5,
                    Level = 10,
                    Count = Mathf.Max(1, testDiceCount),
                    Exp = 0,
                    Shard = 0,
                    PromoteExp = DiceProgression.GRADE_UP_COST
                };

                acc.DiceInventory.Add(target);

                Debug.Log("[AdminCheck] Common ★5 승급 테스트용 주사위 신규 추가");
            }
            else
            {
                // 있으면 승급 직전 상태로 강제 보정
                target.Level = 10;
                target.Exp = 0;
                target.Shard = 0;
                target.PromoteExp = DiceProgression.GRADE_UP_COST;
                target.Count = Mathf.Max(target.Count, Mathf.Max(1, testDiceCount));

                Debug.Log("[AdminCheck] 기존 Common ★5 주사위를 승급 직전 상태로 보정");
            }

            // 장착이 필요하면 여기서 강제 장착 가능
            // acc.EquippedDice = target;
            // acc.EquippedDiceKey = DiceEquipService.MakeEquipKey(target);

            // 인벤 정렬
            DiceInventorySort.SortDiceInventory(acc);

            // 장착 정합성 보정
            DiceEquipService.NormalizeEquippedDice(acc);

            // 5. 전체 저장(accountEnc + diceInventory + equippedDiceKey 등)
            await AccountCloudStore.SaveFullAsync(acc);

            Debug.Log(
                $"[AdminCheck] F11 테스트 주사위 저장 완료 " +
                $"[{target.Grade} | ★{target.Star} | Lv{target.Level} | PromoteExp={target.PromoteExp} | Count={target.Count}]"
            );

          
            // 6. 토스트
            ShowToast(
                "승급 직전 테스트 주사위 지급 완료! (Common ★5 Lv10 / PromoteExp 5)",
                "Promotion-ready test dice added! (Common ★5 Lv10 / PromoteExp 5)"
            );
        }
        catch (Exception e)
        {
            Debug.LogError($"[AdminCheck] F11 테스트 주사위 지급 중 오류 발생: {e}");
            ShowToast("테스트 주사위 지급 실패", "Failed to add promotion test dice.");
        }
        finally
        {
            _isProcessing = false;
        }
    }

    /// <summary>
    /// 문자열을 SHA-256 해시 문자열(소문자 hex)로 변환한다.
    /// </summary>
    private string ToSha256(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        using (SHA256 sha = SHA256.Create())
        {
            byte[] bytes = Encoding.UTF8.GetBytes(input);
            byte[] hash = sha.ComputeHash(bytes);

            StringBuilder sb = new StringBuilder(hash.Length * 2);

            for (int i = 0; i < hash.Length; i++)
                sb.Append(hash[i].ToString("x2"));

            return sb.ToString();
        }
    }

    /// <summary>
    /// 프로젝트의 토스트 매니저를 통해 메시지 출력
    /// </summary>
    private void ShowToast(string kor, string eng)
    {
        if (ToastMessageManager.instance != null)
            ToastMessageManager.instance.ShowToast(kor, eng);
    }
}
