using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Auth;
using Firebase.Database;
using Newtonsoft.Json;


/// <summary>
/// Realtime Database에 Account 저장/로드 담당
/// - accountEnc : Account 전체를 JSON 직렬화 후 CryptoUtil로 암호화(Base64)해서 저장
/// - money/nick : 자주 쓰는 값 or 최적화/중복체크용으로 별도 평문 저장(규칙으로 본인만 접근)
///
/// DB 구조:
/// /users/{uid}/
///    accountEnc : "BASE64..."
///    money      : 123
///    nick       : "nick"
/// </summary>

public static class AccountCloudStore
{
    static DatabaseReference Root => FirebaseDatabase.DefaultInstance.RootReference;

    static string Uid
    {
        get
        {
            var u = FirebaseAuth.DefaultInstance.CurrentUser;
            if (u == null) throw new Exception("Not logged in.");
            return u.UserId;
        }
    }

    static DatabaseReference UserRef(string uid) => Root.Child("users").Child(uid);

    static DatabaseReference EncRef(string uid) => UserRef(uid).Child("accountEnc");
    static DatabaseReference MoneyRef(string uid) => UserRef(uid).Child("money");
    static DatabaseReference NickRef(string uid) => UserRef(uid).Child("nick");


    /// <summary>
    /// DB에서 Account를 불러오되, 없으면 factory로 생성해서 저장 후 반환
    /// </summary>
    public static async Task<Account> LoadOrCreateAsync(Func<FirebaseUser, Account> factory)
    {
        var user = FirebaseAuth.DefaultInstance.CurrentUser;
        if (user == null) throw new Exception("Not logged in.");

        string uid = user.UserId;

        // 1) accountEnc 로드
        var encSnap = await EncRef(uid).GetValueAsync();
        Account acc;

        if (!encSnap.Exists || encSnap.Value == null)
        {
            // 없으면 생성 후 저장
            acc = factory(user);
            await SaveFullAsync(acc);
            return acc;
        }

        // 2) 복호화 후 Account 복원
        string enc = encSnap.Value.ToString();
        string json = CryptoUtil.DecryptFromBase64(enc, uid);
        acc = JsonConvert.DeserializeObject<Account>(json);

        // 3) null 방어(컬렉션)
        acc.ClaimedAttendanceDays ??= new List<int>();
        acc.DiceInventory ??= new List<OwnedDice>();

        // 4) money/nick 별도 필드가 존재하면 최신값으로 덮기
        var moneySnap = await MoneyRef(uid).GetValueAsync();
        if (moneySnap.Exists && moneySnap.Value != null)
            acc.Money = Convert.ToSingle(moneySnap.Value);

        var nickSnap = await NickRef(uid).GetValueAsync();
        if (nickSnap.Exists && nickSnap.Value != null)
            acc.NickName = nickSnap.Value.ToString();

        return acc;
    }

    /// <summary>
    /// Account 전체 저장:
    /// - Account를 JSON 직렬화 → AES/HMAC 암호화 → accountEnc에 저장
    /// - money, nick도 같이 업데이트 (UpdateChildrenAsync로 한 번에)
    /// </summary>
    public static async Task SaveFullAsync(Account acc)
    {
        string uid = Uid;

        string json = JsonConvert.SerializeObject(acc);
        string enc = CryptoUtil.EncryptToBase64(json, uid);

        var updates = new Dictionary<string, object>
        {
            [$"users/{uid}/accountEnc"] = enc,
            [$"users/{uid}/money"] = acc.Money,
            [$"users/{uid}/nick"] = acc.NickName ?? ""
        };

        await Root.UpdateChildrenAsync(updates);
    }

    /// <summary>
    /// Money만 최적화 저장 (accountEnc 전체 재암호화/업로드 안 함)
    /// </summary>
    public static async Task SaveMoneyAsync(float money)
    {
        string uid = Uid;
        await MoneyRef(uid).SetValueAsync(money);
    }

    /// <summary>
    /// Nick만 저장(필요할 때만)
    /// </summary>
    public static async Task SaveNickAsync(string nick)
    {
        string uid = Uid;
        await NickRef(uid).SetValueAsync(nick ?? "");
    }
}
