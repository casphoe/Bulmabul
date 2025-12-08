using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Firebase.Database;

// 이름 중복 제거(중복 방지) 서비스
// 목적:
// - 여러 사용자가 동시에 같은 이름으로 회원가입을 시도해도
//   오직 1명만 성공하고 나머지는 실패하게 만들기
//
// 핵심 아이디어:
// - /names/{nameKey} 라는 "이름 인덱스 테이블"을 만들고,
// - 거기에 "이 이름의 소유자(uid)" 를 저장한다.
//   예) /names/moon = "uid123"
// - 새로 등록할 때는 트랜잭션으로 "비어있으면 내 uid를 써서 선점"한다.
// - 이미 값이 있으면(다른 uid) 중복이므로 실패(Abort)한다.
public static class NameService 
{
    static DatabaseReference Root => FirebaseDatabase.DefaultInstance.RootReference;

    // - DB의 key는 그대로 쓰면 위험/불편:
    //   1) 공백 여러 개, 앞뒤 공백 등 입력이 다양함 ("moon", " moon ", "moon   ")
    //   2) 대소문자 차이 ("Moon" vs "moon")
    //   3) 특수문자나 금지문자 등 (규칙/키 제한)
    //
    // 그래서 "중복 체크 기준"을 하나로 고정해야 한다.
    // 즉, 사용자 화면에 보이는 원본 이름은 따로 저장하고,
    // 중복 판정은 정규화된 키(nameKey)로만 한다.
    //
    // 정규화 규칙:
    // - Trim() : 앞뒤 공백 제거
    // - 여러 공백은 "_" 로 통일
    // - 영문은 소문자 통일
    // - 허용 문자만 남김(한글/영문/숫자/_)
    public static string ToNameKey(string name)
    {
        name = (name ?? "").Trim();

        // 여러 공백 -> _
        name = Regex.Replace(name, @"\s+", "_");

        // 영문만 소문자 (한글은 영향 없음)
        name = name.ToLowerInvariant();

        // 허용: 영문/숫자/_/한글(가-힣), 1~30
        if (!Regex.IsMatch(name, @"^[a-z0-9_\uac00-\ud7a3]{1,30}$"))
            throw new Exception("이름은 1~30자, 한글/영문/숫자/_ 만 가능합니다.");

        return name;
    }

    // - 동시에 2명이 같은 이름을 시도해도 단 1명만 성공
    //
    // 방법:
    // - /names/{nameKey} 노드에 대해 RunTransaction을 사용
    // - 트랜잭션 콜백은 "원자적"으로 동작:
    //   서버가 현재 값을 확인하고, 조건을 만족할 때만 변경을 커밋함.
    //
    // 동작 규칙:
    // 1) 값이 null(비어있음) -> 내 uid를 기록하고 Success -> 선점 성공
    // 2) 값이 내 uid(내가 이미 선점했던 이름) -> Success -> 그대로 OK
    // 3) 값이 다른 uid(누군가 사용중) -> Abort -> 중복 실패
    public static async Task ClaimAsync(string uid, string name)
    {
        string nameKey = ToNameKey(name);
        var r = Root.Child("names").Child(nameKey);

        await r.RunTransaction(mutable =>
        {
            if (mutable.Value == null)
            {
                mutable.Value = uid;
                return TransactionResult.Success(mutable);
            }

            if (mutable.Value != null && mutable.Value.ToString() == uid)
                return TransactionResult.Success(mutable);

            return TransactionResult.Abort();
        }, false);

        var snap = await r.GetValueAsync();
        if (snap.Exists && snap.Value != null && snap.Value.ToString() == uid)
            return;

        AuthUIController.instance.ShowToast("이미 사용 중인 이름입니다.");

        throw new Exception("이미 사용 중인 이름입니다.");
    }

    // - 회원 탈퇴 시
    // - 이름 변경 시(기존 이름 키를 반환)
    //
    // 안전장치:
    // - "내 uid가 소유한 이름"일 때만 삭제한다.
    // - 그렇지 않으면 남의 이름 키를 지워버리는 사고가 날 수 있음.
    public static async Task ReleaseAsync(string uid, string oldName)
    {
        if (string.IsNullOrWhiteSpace(oldName)) return;

        var nameKey = ToNameKey(oldName);
        var r = Root.Child("names").Child(nameKey);

        var snap = await r.GetValueAsync();
        if (snap.Exists && snap.Value != null && snap.Value.ToString() == uid)
            await r.RemoveValueAsync();
    }
}
