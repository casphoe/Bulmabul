using System;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class FriendProfilePopup : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject root;   // 팝업 전체 루트 오브젝트
    [SerializeField] private Button btnClose;   // 팝업 닫기 버튼

    [Header("Image")]
    [SerializeField] private RawImage profileImage;  // 친구 프로필 이미지
    [SerializeField] private Image equipDiceImage;   // 장착 주사위 이미지(등급 색상/효과 적용용)

    [Header("Texts")]
    [SerializeField] private Text txtTitle;          // 팝업 제목
    [SerializeField] private TMP_Text txtNick;       // 친구 닉네임
    [SerializeField] private TMP_Text txtLevel;      // 친구 레벨
    [SerializeField] private TMP_Text txtLastSeen;   // 마지막 접속일 / 온라인 여부
    [SerializeField] private TMP_Text txtEquippedDice; // 장착 주사위 텍스트

    [Header("Default")]
    [SerializeField] private Texture defaultProfileTexture; // 프로필 이미지가 없을 때 사용할 기본 이미지
    [SerializeField] private Sprite defaultDiceSprite;      // 주사위 기본 스프라이트

    // Legendary 같은 등급에서 사용하는 무지개 효과 참조
    private RainbowImageEffect _rainbow;

    private string _currentFriendUid;

    private void Awake()
    {
        // 시작 시 팝업은 닫힌 상태로 둔다
        if (root != null) root.SetActive(false);

        // 닫기 버튼 클릭 시 Close 함수 실행
        if (btnClose != null) btnClose.onClick.AddListener(Close);
    }

    /// <summary>
    /// 친구 리스트에 이미 표시 중인 프로필 이미지 텍스처를 그대로 받아와 팝업에 적용한다.
    /// 다시 다운로드하지 않고 현재 친구 리스트에서 보이는 이미지를 재사용하기 위한 함수.
    /// </summary>
    public void SetProfileTexture(Texture texture)
    {
        if (profileImage == null) return;

        // 넘겨받은 텍스처가 있으면 그대로 사용하고,
        // 없으면 기본 프로필 텍스처를 사용
        profileImage.texture = texture != null ? texture : defaultProfileTexture;
    }

    /// <summary>
    /// 친구 uid 기준으로 친구 프로필 데이터를 조회하고 팝업에 반영한다.
    /// 프로필 이미지는 SetProfileTexture로 미리 넣어두고,
    /// 여기서는 텍스트/주사위 정보 위주로 채운다.
    /// </summary>
    public async void Open(string friendUid)
    {
        // uid가 비어 있으면 열지 않음
        if (string.IsNullOrWhiteSpace(friendUid)) return;

        _currentFriendUid = friendUid;

        // 팝업 활성화
        if (root != null)
            root.SetActive(true);

        // 로딩 중 표시용 UI 먼저 세팅
        SetLoadingUI();

        try
        {
            var requestedUid = friendUid;
            // uid 기준으로 친구 프로필 데이터 읽기
            var data = await FriendService.GetFriendProfileAsync(friendUid);

            // 지금 보고자 하는 최신 친구 프로필이 아니면 무시
            if (_currentFriendUid != requestedUid)
                return;

            // 조회 완료 후 실제 UI 반영
            Apply(data);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[FriendProfilePopup] {e}");

            if (_currentFriendUid != friendUid)
                return;

            // 조회 실패 시 토스트 표시
            ToastMessageManager.instance?.ShowToast(
                "친구 프로필을 불러오지 못했습니다.",
                "Failed to load friend profile."
            );

            // 실패하면 팝업 닫기
            Close();
        }
    }

    /// <summary>
    /// 팝업 닫기
    /// </summary>
    public void Close()
    {
        if (root != null)
            root.SetActive(false);
    }

    /// <summary>
    /// 데이터 조회 전, 로딩 상태를 표시하는 UI
    /// 언어에 따라 한국어 / 영어 텍스트를 다르게 보여준다.
    /// </summary>
    void SetLoadingUI()
    {
        bool isEng = IsEnglish();

        if (txtTitle != null) txtTitle.text = isEng ? "Friend Profile" : "친구 프로필";
        if (txtNick != null) txtNick.text = isEng ? "Nickname : Loading..." : "닉네임 : 불러오는 중...";
        if (txtLevel != null) txtLevel.text = isEng ? "Level : -" : "레벨 : -";
        if (txtLastSeen != null) txtLastSeen.text = isEng ? "Last Seen : -" : "접속 날짜 : -";
        if (txtEquippedDice != null) txtEquippedDice.text = isEng ? "Equipped Dice : -" : "장착 주사위 : -";

        // 프로필 이미지가 아직 비어 있으면 기본 이미지 사용
        if (profileImage != null && profileImage.texture == null)
            profileImage.texture = defaultProfileTexture;

        // 주사위 비주얼도 기본값으로 초기화
        ApplyDiceVisual(null);
    }

    /// <summary>
    /// 조회된 친구 프로필 데이터를 실제 UI에 반영한다.
    /// </summary>
    void Apply(FriendProfileData data)
    {
        bool isEng = IsEnglish();

        // 제목은 항상 현재 언어에 맞게 표시
        if (txtTitle != null)
            txtTitle.text = isEng ? "Friend Profile" : "친구 프로필";

        // 데이터가 없으면 기본 문구로 표시
        if (data == null)
        {
            if (txtNick != null) txtNick.text = isEng ? "Nickname : -" : "닉네임 : -";
            if (txtLevel != null) txtLevel.text = isEng ? "Level : -" : "레벨 : -";
            if (txtLastSeen != null) txtLastSeen.text = isEng ? "Last Seen : No record" : "접속 날짜 : 기록 없음";
            if (txtEquippedDice != null) txtEquippedDice.text = isEng ? "Equipped Dice : Not Equipped" : "장착 주사위 : 장착 안 함";

            if (profileImage != null && profileImage.texture == null)
                profileImage.texture = defaultProfileTexture;

            ApplyDiceVisual(null);
            return;
        }

        // 닉네임 표시
        if (txtNick != null)
            txtNick.text = FormatNickname(data.nick);

        // 레벨 표시
        if (txtLevel != null)
            txtLevel.text = FormatLevel(data.accountLevel);

        // 마지막 접속일 / 온라인 여부 표시
        if (txtLastSeen != null)
            txtLastSeen.text = FormatLastSeen(data);

        // 장착 주사위 텍스트 표시
        if (txtEquippedDice != null)
            txtEquippedDice.text = FormatEquippedDiceText(data.equippedDiceKey);

        // 장착 주사위 등급에 맞는 색상/효과 적용
        ApplyDiceVisual(data.equippedDiceKey);

        // 프로필 이미지가 비어 있으면 기본 이미지 사용
        if (profileImage != null && profileImage.texture == null)
            profileImage.texture = defaultProfileTexture;
    }

    /// <summary>
    /// 닉네임 표시 포맷
    /// 한국어: 닉네임 : xxx
    /// 영어: Nickname : xxx
    /// </summary>
    string FormatNickname(string nick)
    {
        bool isEng = IsEnglish();
        string safeNick = string.IsNullOrWhiteSpace(nick) ? "-" : nick;
        return isEng ? $"Nickname : {safeNick}" : $"닉네임 : {safeNick}";
    }

    /// <summary>
    /// 레벨 표시 포맷
    /// 한국어: 레벨 : 10
    /// 영어: Level : 10
    /// 최소 1레벨 보장
    /// </summary>
    string FormatLevel(int level)
    {
        bool isEng = IsEnglish();
        int safeLevel = Mathf.Max(1, level);
        return isEng ? $"Level : {safeLevel}" : $"레벨 : {safeLevel}";
    }

    /// <summary>
    /// 마지막 접속일 표시 포맷
    /// 
    /// 규칙:
    /// - 현재 온라인이면 "온라인" / "Online"
    /// - 기록이 없으면 "기록 없음" / "No record"
    /// - 오프라인이면 절대 시간 + 상대 시간 표시
    ///   예) 접속 날짜 : 2026-04-27 10:30 (3시간 전 접속)
    ///   예) Last Seen : 2026-04-27 10:30 (3 hours ago)
    /// </summary>
    string FormatLastSeen(FriendProfileData data)
    {
        bool isEng = IsEnglish();

        // 현재 온라인 상태면 날짜 대신 온라인 표시
        if (data.isOnline)
            return isEng ? "Last Seen : Online" : "접속 날짜 : 온라인";

        // lastSeen 값이 비어 있으면 기록 없음 처리
        if (data.lastSeenUnix <= 0)
            return isEng ? "Last Seen : No record" : "접속 날짜 : 기록 없음";

        try
        {
            // Unix Time -> 로컬 시간 변환
            DateTimeOffset lastSeen = DateTimeOffset.FromUnixTimeSeconds(data.lastSeenUnix).ToLocalTime();

            // 절대 시간 문자열
            string dateText = lastSeen.ToString("yyyy-MM-dd HH:mm");

            // 현재 시간 기준 상대 시간 문자열
            string relativeText = FormatRelativeLastSeen(lastSeen, DateTimeOffset.Now, isEng);

            return isEng
                ? $"Last Seen : {dateText} ({relativeText})"
                : $"접속 날짜 : {dateText} ({relativeText})";
        }
        catch
        {
            return isEng ? "Last Seen : No record" : "접속 날짜 : 기록 없음";
        }
    }

    /// <summary>
    /// 현재 시간(now)과 마지막 접속 시간(lastSeen)의 차이를 계산해서
    /// "몇 분 전", "몇 시간 전", "몇 일 전" 형태의 상대 시간 문자열을 만든다.
    /// 
    /// 한국어 예:
    /// - 방금 전 접속
    /// - 15분 전 접속
    /// - 3시간 전 접속
    /// - 2일 전 접속
    /// - 1개월 전 접속
    /// 
    /// 영어 예:
    /// - just now
    /// - 15 minutes ago
    /// - 3 hours ago
    /// - 2 days ago
    /// - 1 month ago
    /// </summary>
    string FormatRelativeLastSeen(DateTimeOffset lastSeen, DateTimeOffset now, bool isEng)
    {
        TimeSpan diff = now - lastSeen;

        // 미래 시간이 들어온 경우 방어 처리
        if (diff.TotalSeconds < 0)
            diff = TimeSpan.Zero;

        // 1분 미만
        if (diff.TotalMinutes < 1)
            return isEng ? "just now" : "방금 전 접속";

        // 1시간 미만
        if (diff.TotalHours < 1)
        {
            int minutes = Mathf.Max(1, Mathf.FloorToInt((float)diff.TotalMinutes));
            return isEng
                ? $"{minutes} minute{(minutes > 1 ? "s" : "")} ago"
                : $"{minutes}분 전 접속";
        }

        // 1일 미만
        if (diff.TotalDays < 1)
        {
            int hours = Mathf.Max(1, Mathf.FloorToInt((float)diff.TotalHours));
            return isEng
                ? $"{hours} hour{(hours > 1 ? "s" : "")} ago"
                : $"{hours}시간 전 접속";
        }

        // 30일 미만
        if (diff.TotalDays < 30)
        {
            int days = Mathf.Max(1, Mathf.FloorToInt((float)diff.TotalDays));
            return isEng
                ? $"{days} day{(days > 1 ? "s" : "")} ago"
                : $"{days}일 전 접속";
        }

        // 30일 이상이면 개월 단위
        int months = Mathf.Max(1, Mathf.FloorToInt((float)diff.TotalDays / 30f));
        return isEng
            ? $"{months} month{(months > 1 ? "s" : "")} ago"
            : $"{months}개월 전 접속";
    }

    /// <summary>
    /// 장착 주사위 텍스트 포맷
    /// 
    /// 예:
    /// - Rare|3 -> 희귀 / 3성
    /// - Rare|3 -> Rare / ★3
    /// - 비어 있으면 장착 안 함 / Not Equipped
    /// </summary>
    string FormatEquippedDiceText(string key)
    {
        bool isEng = IsEnglish();

        if (string.IsNullOrWhiteSpace(key))
            return isEng ? "Equipped Dice : Not Equipped" : "장착 주사위 : 장착 안 함";

        string[] split = key.Split('|');
        if (split.Length < 2)
            return isEng ? $"Equipped Dice : {key}" : $"장착 주사위 : {key}";

        // 등급 파싱
        DiceGrade? grade = ParseGrade(key);
        string star = split[1];

        // 등급명을 현재 언어 기준으로 변환
        string gradeText = grade.HasValue
            ? GetGradeText(grade.Value, isEng)
            : split[0];

        return isEng
            ? $"Equipped Dice : {gradeText} / ★{star}"
            : $"장착 주사위 : {gradeText} / {star}성";
    }

    /// <summary>
    /// 장착 주사위 등급에 맞게
    /// - 이미지 색상/효과를 적용
    /// - 텍스트 색상도 변경
    /// </summary>
    void ApplyDiceVisual(string equippedDiceKey)
    {
        DiceGrade? grade = ParseGrade(equippedDiceKey);

        if (equipDiceImage != null)
        {
            // 기본 스프라이트 세팅
            if (defaultDiceSprite != null)
                equipDiceImage.sprite = defaultDiceSprite;

            // 등급 색상/효과 적용
            DiceGradeColorUtil.Apply(
                equipDiceImage,
                grade ?? DiceGrade.Common,
                ref _rainbow
            );
        }

        if (txtEquippedDice != null)
        {
            // 등급이 있으면 등급별 색상, 없으면 흰색
            if (grade.HasValue)
                ApplyGradeTextColor(txtEquippedDice, grade.Value);
            else
                txtEquippedDice.color = Color.white;
        }
    }

    /// <summary>
    /// 주사위 등급에 따라 텍스트 색상 적용
    /// </summary>
    void ApplyGradeTextColor(TMP_Text text, DiceGrade grade)
    {
        if (text == null) return;

        switch (grade)
        {
            case DiceGrade.Common:
                text.color = Color.white;
                break;

            case DiceGrade.Rare:
                text.color = new Color(0.35f, 0.8f, 1f);
                break;

            case DiceGrade.Epic:
                text.color = new Color(0.82f, 0.45f, 1f);
                break;

            case DiceGrade.Legendary:
                text.color = new Color(1f, 0.78f, 0.2f);
                break;

            default:
                text.color = Color.white;
                break;
        }
    }

    /// <summary>
    /// equippedDiceKey에서 등급만 파싱
    /// 예: Legendary|5 -> DiceGrade.Legendary
    /// 실패 시 null
    /// </summary>
    DiceGrade? ParseGrade(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        string[] split = key.Split('|');
        if (split.Length < 1)
            return null;

        if (Enum.TryParse(split[0], out DiceGrade grade))
            return grade;

        return null;
    }

    /// <summary>
    /// DiceGrade를 현재 언어 기준 표시명으로 변환
    /// </summary>
    string GetGradeText(DiceGrade grade, bool isEng)
    {
        if (isEng)
        {
            return grade switch
            {
                DiceGrade.Common => "Common",
                DiceGrade.Rare => "Rare",
                DiceGrade.Epic => "Epic",
                DiceGrade.Legendary => "Legendary",
                _ => "Unknown"
            };
        }

        return grade switch
        {
            DiceGrade.Common => "일반",
            DiceGrade.Rare => "희귀",
            DiceGrade.Epic => "영웅",
            DiceGrade.Legendary => "전설",
            _ => "알 수 없음"
        };
    }

    /// <summary>
    /// 현재 언어가 영어인지 확인
    /// </summary>
    bool IsEnglish()
    {
        return LaguageManager.Instance != null &&
               LaguageManager.Instance.currentLang == Lauaguage.Eng;
    }
}