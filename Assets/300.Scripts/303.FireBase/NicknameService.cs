using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Firebase.Database;

public static class NicknameService
{
    static DatabaseReference Root => FirebaseDatabase.DefaultInstance.RootReference;

    /// <summary>
    /// 입력 닉네임을 RTDB Key로 안전하게 변환한다.
    /// - RTDB Key로 쓰려면 금지문자(. $ # [ ] /) 등을 피해야 함
    /// - 여기서는 정책을 "소문자 + 숫자 + _(언더바)"만 허용하도록 강제
    /// - 정책: 3~12자, 영문/숫자/_
    /// </summary>
    public static string ToNickKey(string nick)
    {
        if (nick == null) nick = "";
        nick = nick.Trim();

        // 예: 공백 -> _, 대문자 -> 소문자
        nick = Regex.Replace(nick, @"\s+", "_").ToLowerInvariant();

        // 허용 문자만 남기기(원하면 여기 정책 더 엄격/완화 가능)
        if (!Regex.IsMatch(nick, @"^[a-z0-9_]{3,12}$"))
            throw new Exception("닉네임은 3~12자, 영문/숫자/_ 만 가능");

        // RTDB 금지문자(. $ # [ ] /)는 위 regex로 차단됨
        return nick;
    }

    /// <summary>
    /// 닉네임 선점(중복 체크) 함수.
    /// 
    /// 목표:
    /// - 여러 사람이 동시에 같은 닉네임을 시도해도 오직 1명만 가져가게(원자성)
    /// 
    /// 방식:
    /// 1) /nicknames/{nickKey} 노드를 트랜잭션으로 "비어있으면 uid 기록" 시도
    ///    - 이미 값이 있으면 Abort(중복)
    /// 2) 일부 Firebase Unity SDK는 트랜잭션 완료 콜백/await가 없거나 제한적이라,
    ///    GetValueAsync()로 짧게 재시도하며 실제로 내 uid로 들어갔는지 확인한다.
    /// </summary>
    /// <param name="uid">FirebaseAuth의 user.UserId</param>
    /// <param name="nick">사용자가 입력한 원본 닉네임</param>
    public static async Task ClaimAsync(string uid, string nick)
    {
        string nickKey = ToNickKey(nick);
        var r = Root.Child("nicknames").Child(nickKey);

        _ = r.RunTransaction(mutable =>
        {
            // 비어있으면 선점
            if (mutable.Value == null)
            {
                mutable.Value = uid;
                return TransactionResult.Success(mutable);
            }
            // 이미 내가 소유한 닉이면 그대로 성공
            if (mutable.Value != null && mutable.Value.ToString() == uid)
                return TransactionResult.Success(mutable);
            // 다른 사람이 이미 사용 중
            return TransactionResult.Abort();
        }, true);


        // 2) 결과 확인 (네트워크/동기화 타이밍 때문에 짧게 재시도)
        // - 값이 uid면 성공
        // - 값이 다른 uid면 중복(실패)
        // - 여전히 null이면 반영 지연일 수 있어 몇 번 더 확인
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(80 + i * 80);
            var snap = await r.GetValueAsync();

            if (snap.Exists && snap.Value != null)
            {
                string owner = snap.Value.ToString();
                if (owner == uid) return;              // 성공
                throw new Exception("이미 사용 중인 닉네임입니다."); // 실패
            }
        }

        // 여기까지 왔다면 반영/확인이 애매한 상태(거의 규칙/네트워크)
        throw new Exception("닉네임 선점 결과 확인에 실패했습니다. 네트워크/DB 규칙을 확인하세요.");
    }


    /// <summary>
    /// 닉네임 해제(닉 변경/탈퇴용).
    /// 
    /// 동작:
    /// - /nicknames/{oldNickKey} 값을 읽어서
    /// - 그 값이 내 uid일 때만 삭제(RemoveValue)
    /// 
    /// 이유:
    /// - 다른 사람이 사용하는 닉네임을 실수로 삭제하면 안 되므로 "소유자 확인"을 한다.
    /// </summary>
    public static async Task ReleaseAsync(string uid, string oldNick)
    {
        if (string.IsNullOrWhiteSpace(oldNick)) return;

        var nickKey = ToNickKey(oldNick);

        var r = Root.Child("nicknames").Child(nickKey);
        var snap = await r.GetValueAsync();
        if (snap.Exists && snap.Value != null && snap.Value.ToString() == uid)
            await r.RemoveValueAsync();
    }
}
