using Firebase.Auth;
using Firebase.Database;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Realtime Database에 Account 저장/로드 담당
/// - accountEnc : Account 전체를 JSON 직렬화 후 CryptoUtil로 암호화(Base64)해서 저장
/// - cash/nick/nameKey/accountLevel/accountExp/equippedDiceKey/diceInventory : 빠른 조회/보정용 평문 저장
/// </summary>
/// 
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
    static DatabaseReference MoneyRef(string uid) => UserRef(uid).Child("cash");
    static DatabaseReference NickRef(string uid) => UserRef(uid).Child("nick");

    static DatabaseReference NameKeyRef(string uid) => UserRef(uid).Child("nameKey");

    static DatabaseReference LevelRef(string uid) => UserRef(uid).Child("accountLevel");
    static DatabaseReference ExpRef(string uid) => UserRef(uid).Child("accountExp");

    static DatabaseReference EquippedDiceKeyRef(string uid) => UserRef(uid).Child("equippedDiceKey");

    static DatabaseReference DiceInventoryRef(string uid) => UserRef(uid).Child("diceInventory"); // (원하면)

    #region 토스트 Helper
    static void ShowToast(string kor, string eng)
    {
        if (ToastMessageManager.instance != null)
            ToastMessageManager.instance.ShowToast(kor, eng);
    }
    #endregion

    /// <summary>
    /// Account 전체 저장:
    /// - Account를 JSON 직렬화 → AES/HMAC 암호화 → accountEnc에 저장
    /// - money, nick도 같이 업데이트 (UpdateChildrenAsync로 한 번에)
    /// </summary>
    public static async Task SaveFullAsync(Account acc)
    {
        string uid = Uid;

        ShowToast("파이어베이스에 저장중입니다...", "Saving to Firebase...");

        try
        {
            // null 방어
            acc.ClaimedAttendanceDays ??= new List<int>();
            acc.DiceInventory ??= new List<OwnedDice>();

            // 장착키 정합성 보정
            if (string.IsNullOrWhiteSpace(acc.EquippedDiceKey) && acc.EquippedDice != null)
            {
                // 프로젝트 구조상 장착키는 보통 Grade|Star 기준이 더 안전
                acc.EquippedDiceKey = acc.EquippedDice.EquipKey;
            }

            string json = JsonConvert.SerializeObject(acc);
            string enc = CryptoUtil.EncryptToBase64(json, uid);

            string nickKey = NicknameService.ToNickKey(acc.NickName);
            string nameKey = NameService.ToNameKey(acc.Name);

            // 주사위 인벤토리 평문 저장용 변환
            var diceInvMap = BuildDiceInventoryMap(acc);

            // EquippedDice.Key(Grade|Star|Level)가 아니라
            // acc.EquippedDiceKey를 그대로 저장
            string equippedKey = acc.EquippedDiceKey ?? "";

            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var updates = new Dictionary<string, object>
            {
                [$"users/{uid}/accountEnc"] = enc,
                [$"users/{uid}/cash"] = acc.Cash,
                [$"users/{uid}/nick"] = nickKey,
                [$"users/{uid}/nameKey"] = nameKey,
                [$"users/{uid}/accountLevel"] = acc.AccountLevel,
                [$"users/{uid}/accountExp"] = acc.AccountExp,

                // 뽑기 관련 메타
                [$"users/{uid}/diceTotalPullCount"] = acc.DiceTotalPullCount,
                [$"users/{uid}/dicePityCount"] = Mathf.Clamp(acc.DicePityCount, 0, 49),
                [$"users/{uid}/lastDicePullAt"] = nowMs,
                [$"users/{uid}/equippedDiceKey"] = equippedKey,

                // 주사위 인벤토리 평문 저장
                [$"users/{uid}/diceInventory"] = diceInvMap
            };

            await Root.UpdateChildrenAsync(updates);

            Debug.Log("[AccountCloudStore] SaveFullAsync 완료");
            ShowToast("저장 완료되었습니다.", "Save completed.");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            ShowToast("저장 실패", "Save failed.");
            throw;
        }
    }

    static Dictionary<string, object> BuildDiceInventoryMap(Account acc)
    {
        var result = new Dictionary<string, object>();

        if (acc.DiceInventory == null) return result;

        foreach (var od in acc.DiceInventory)
        {
            if (od == null) continue;

            // rules에서 $diceKey는 길이 제한이 없지만, 안전하게 너무 길면 패스 가능
            // 여기서는 Key가 "Grade|Star|Level" 이므로 안전
            string diceKey = od.SaveKey;

            var node = new Dictionary<string, object>
            {
                ["grade"] = od.Grade.ToString(), // Common/Rare/Epic/Legendary
                ["star"] = od.Star,
                ["level"] = od.Level,
                ["count"] = od.Count,
                ["exp"] = od.Exp,
                ["shard"] = Mathf.Max(0, od.Shard),
                ["promoteExp"] = Mathf.Max(0, od.PromoteExp)
            };

            result[diceKey] = node;
        }

        return result;
    }

    /// <summary>
    /// Money만 최적화 저장 (accountEnc 전체 재암호화/업로드 안 함)
    /// </summary>
    public static async Task SaveMoneyAsync(float money)
    {
        string uid = Uid;
        await MoneyRef(uid).SetValueAsync(money);
    }

    //계정 경험치 만 빠르게 저장하는 함수
    public static async Task SaveProgressAsync(int level, long exp)
    {
        string uid = Uid;

        var updates = new Dictionary<string, object>
        {
            [$"users/{uid}/accountLevel"] = level,
            [$"users/{uid}/accountExp"] = exp
        };

        await Root.UpdateChildrenAsync(updates);
    }

    /// <summary>
    /// Nick만 저장(필요할 때만)
    /// </summary>
    public static async Task SaveNickAsync(string nick)
    {
        string uid = Uid;
        await NickRef(uid).SetValueAsync(nick ?? "");
    }

    public static async Task SaveEquippedDiceKeyAsync(string equippedDiceKey)
    {
        string uid = Uid;
        await EquippedDiceKeyRef(uid).SetValueAsync(equippedDiceKey ?? "");
    }

    public static async Task<Account> LoadAsync()
    {
        var user = FirebaseAuth.DefaultInstance.CurrentUser;
        if (user == null) throw new Exception("Not logged in.");

        string uid = user.UserId;

        ShowToast("파이어베이스에서 불러오는 중입니다...", "Loading from Firebase...");

        Account acc = null;

        try
        {
            // 1) accountEnc 로드
            var encSnap = await EncRef(uid).GetValueAsync();
            if (encSnap.Exists && encSnap.Value != null)
            {
                try
                {
                    string enc = encSnap.Value.ToString();
                    string json = CryptoUtil.DecryptFromBase64(enc, uid);
                    acc = JsonConvert.DeserializeObject<Account>(json);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[AccountCloudStore] accountEnc 복호화 실패: {e}");
                }
            }

            // accountEnc 실패 시 기본 객체 생성
            acc ??= new Account();

            // null 방어
            acc.ClaimedAttendanceDays ??= new List<int>();
            acc.DiceInventory ??= new List<OwnedDice>();

            // 별도 평문 필드 최신값 덮기
            var moneySnap = await MoneyRef(uid).GetValueAsync();
            if (moneySnap.Exists && moneySnap.Value != null)
                acc.Cash = Convert.ToSingle(moneySnap.Value);

            var nickSnap = await NickRef(uid).GetValueAsync();
            if (nickSnap.Exists && nickSnap.Value != null)
                acc.NickName = nickSnap.Value.ToString();

            var nameSnap = await NameKeyRef(uid).GetValueAsync();
            if (nameSnap.Exists && nameSnap.Value != null)
                acc.Name = nameSnap.Value.ToString();

            var lvSnap = await LevelRef(uid).GetValueAsync();
            if (lvSnap.Exists && lvSnap.Value != null)
                acc.AccountLevel = Convert.ToInt32(lvSnap.Value);

            var expSnap = await ExpRef(uid).GetValueAsync();
            if (expSnap.Exists && expSnap.Value != null)
                acc.AccountExp = Convert.ToInt64(expSnap.Value);

            var eqSnap = await EquippedDiceKeyRef(uid).GetValueAsync();
            if (eqSnap.Exists && eqSnap.Value != null)
                acc.EquippedDiceKey = eqSnap.Value.ToString();

            // 평문 diceInventory가 있으면 최신값으로 덮어쓰기
            var diceSnap = await DiceInventoryRef(uid).GetValueAsync();
            if (diceSnap.Exists && diceSnap.HasChildren)
            {
                acc.DiceInventory = ParseDiceInventory(diceSnap);
            }

            // equippedDiceKey 기준으로 EquippedDice 다시 연결
            RelinkEquippedDice(acc);

            ShowToast("로드 완료되었습니다.", "Load completed.");
            return acc;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            ShowToast("로드 실패", "Load failed.");
            throw;
        }
    }

    public static async Task<Account> LoadOrThrowAsync()
    {
        var acc = await LoadAsync();
        if (acc == null)
            throw new Exception("계정 데이터가 없습니다. 회원가입을 진행해 주세요.");
        return acc;
    }

    static List<OwnedDice> ParseDiceInventory(DataSnapshot diceSnap)
    {
        var list = new List<OwnedDice>();

        foreach (var child in diceSnap.Children)
        {
            try
            {
                string gradeStr = child.Child("grade")?.Value?.ToString() ?? "Common";

                int star = ToInt(child.Child("star")?.Value, 1);
                int level = ToInt(child.Child("level")?.Value, 1);
                int count = ToInt(child.Child("count")?.Value, 1);
                int exp = ToInt(child.Child("exp")?.Value, 0);
                int shard = ToInt(child.Child("shard")?.Value, 0);
                int promoteExp = ToInt(child.Child("promoteExp")?.Value, 0);

                if (!Enum.TryParse(gradeStr, out DiceGrade grade))
                    grade = DiceGrade.Common;

                var od = new OwnedDice
                {
                    Grade = grade,
                    Star = Mathf.Max(1, star),
                    Level = Mathf.Max(1, level),
                    Count = Mathf.Max(1, count),
                    Exp = Mathf.Max(0, exp),
                    Shard = Mathf.Max(0, shard),
                    PromoteExp = Mathf.Max(0, promoteExp)
                };

                list.Add(od);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AccountCloudStore] ParseDiceInventory 실패: {e}");
            }
        }

        return list;
    }

    static void RelinkEquippedDice(Account acc)
    {
        acc.EquippedDice = null;

        if (acc == null || acc.DiceInventory == null || acc.DiceInventory.Count == 0)
            return;

        string eqKey = acc.EquippedDiceKey ?? "";
        if (string.IsNullOrWhiteSpace(eqKey))
            return;

        // 1차: EquipKey(Grade|Star) 기준
        for (int i = 0; i < acc.DiceInventory.Count; i++)
        {
            var od = acc.DiceInventory[i];
            if (od == null) continue;

            if (string.Equals(od.EquipKey, eqKey, StringComparison.Ordinal))
            {
                acc.EquippedDice = od;
                return;
            }
        }

        // 2차: 혹시 예전 데이터가 Grade|Star|Level 로 저장됐을 경우
        for (int i = 0; i < acc.DiceInventory.Count; i++)
        {
            var od = acc.DiceInventory[i];
            if (od == null) continue;

            if (string.Equals(od.SaveKey, eqKey, StringComparison.Ordinal))
            {
                acc.EquippedDice = od;

                // 이후부터는 EquipKey 기준으로 정규화
                acc.EquippedDiceKey = od.EquipKey;
                return;
            }
        }
    }

    static int ToInt(object value, int defaultValue)
    {
        if (value == null) return defaultValue;

        try
        {
            return Convert.ToInt32(value);
        }
        catch
        {
            return defaultValue;
        }
    }
}
