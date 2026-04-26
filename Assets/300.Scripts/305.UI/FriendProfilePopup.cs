using System;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class FriendProfilePopup : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject root;   // 팝업 전체 루트
    [SerializeField] private Button btnClose;   // 닫기 버튼

    [Header("Image")]
    [SerializeField] private RawImage profileImage;  // 친구 프로필 이미지
    [SerializeField] private Image equipDiceImage;   // 장착 주사위 이미지(등급 색상 표시용)

    [Header("Texts")]
    [SerializeField] private Text txtTitle;          // 팝업 제목
    [SerializeField] private TMP_Text txtNick;       // 친구 닉네임
    [SerializeField] private TMP_Text txtLevel;      // 친구 레벨
    [SerializeField] private TMP_Text txtLastSeen;   // 마지막 접속일 / 온라인 여부
    [SerializeField] private TMP_Text txtEquippedDice; // 장착 주사위 텍스트

    [Header("Default")]
    [SerializeField] private Texture defaultProfileTexture; // 기본 프로필 텍스처
    [SerializeField] private Sprite defaultDiceSprite;      // 기본 주사위 스프라이트

    private RainbowImageEffect _rainbow;

    private void Awake()
    {
        if (root != null) root.SetActive(false);
        if (btnClose != null) btnClose.onClick.AddListener(Close);
    }

    /// <summary>
    /// 친구 리스트에서 이미 표시 중인 프로필 이미지 텍스처를 그대로 받아와 팝업에 적용한다.
    /// </summary>
    public void SetProfileTexture(Texture texture)
    {
        if (profileImage == null) return;
        profileImage.texture = texture != null ? texture : defaultProfileTexture;
    }

    /// <summary>
    /// friendUid 기준으로 친구 프로필 데이터를 조회하고 팝업에 반영한다.
    /// 프로필 이미지는 SetProfileTexture로 미리 전달받는 구조.
    /// </summary>
    public async void Open(string friendUid)
    {
        if (string.IsNullOrWhiteSpace(friendUid)) return;

        if (root != null)
            root.SetActive(true);

        SetLoadingUI();

        try
        {
            var data = await FriendService.GetFriendProfileAsync(friendUid);
            Apply(data);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[FriendProfilePopup] {e}");
            ToastMessageManager.instance?.ShowToast(
                "친구 프로필을 불러오지 못했습니다.",
                "Failed to load friend profile."
            );
            Close();
        }
    }

    public void Close()
    {
        if (root != null)
            root.SetActive(false);
    }

    /// <summary>
    /// 로딩 전 임시 UI
    /// </summary>
    void SetLoadingUI()
    {
        bool isEng = IsEnglish();

        if (txtTitle != null) txtTitle.text = isEng ? "Friend Profile" : "친구 프로필";
        if (txtNick != null) txtNick.text = isEng ? "Nickname : Loading..." : "닉네임 : 불러오는 중...";
        if (txtLevel != null) txtLevel.text = isEng ? "Level : -" : "레벨 : -";
        if (txtLastSeen != null) txtLastSeen.text = isEng ? "Last Seen : -" : "접속 날짜 : -";
        if (txtEquippedDice != null) txtEquippedDice.text = isEng ? "Equipped Dice : -" : "장착 주사위 : -";

        if (profileImage != null && profileImage.texture == null)
            profileImage.texture = defaultProfileTexture;

        ApplyDiceVisual(null);
    }

    /// <summary>
    /// 조회된 친구 프로필 데이터를 실제 UI에 반영
    /// </summary>
    void Apply(FriendProfileData data)
    {
        bool isEng = IsEnglish();

        if (txtTitle != null)
            txtTitle.text = isEng ? "Friend Profile" : "친구 프로필";

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

        if (txtNick != null)
            txtNick.text = FormatNickname(data.nick);

        if (txtLevel != null)
            txtLevel.text = FormatLevel(data.accountLevel);

        if (txtLastSeen != null)
            txtLastSeen.text = FormatLastSeen(data);

        if (txtEquippedDice != null)
            txtEquippedDice.text = FormatEquippedDiceText(data.equippedDiceKey);

        ApplyDiceVisual(data.equippedDiceKey);

        if (profileImage != null && profileImage.texture == null)
            profileImage.texture = defaultProfileTexture;
    }

    string FormatNickname(string nick)
    {
        bool isEng = IsEnglish();
        string safeNick = string.IsNullOrWhiteSpace(nick) ? "-" : nick;
        return isEng ? $"Nickname : {safeNick}" : $"닉네임 : {safeNick}";
    }

    string FormatLevel(int level)
    {
        bool isEng = IsEnglish();
        int safeLevel = Mathf.Max(1, level);
        return isEng ? $"Level : {safeLevel}" : $"레벨 : {safeLevel}";
    }

    string FormatLastSeen(FriendProfileData data)
    {
        bool isEng = IsEnglish();

        if (data.isOnline)
            return isEng ? "Last Seen : Online" : "접속 날짜 : 온라인";

        if (data.lastSeenUnix <= 0)
            return isEng ? "Last Seen : No record" : "접속 날짜 : 기록 없음";

        try
        {
            string dateText = DateTimeOffset.FromUnixTimeSeconds(data.lastSeenUnix)
                .ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm");

            return isEng ? $"Last Seen : {dateText}" : $"접속 날짜 : {dateText}";
        }
        catch
        {
            return isEng ? "Last Seen : No record" : "접속 날짜 : 기록 없음";
        }
    }

    string FormatEquippedDiceText(string key)
    {
        bool isEng = IsEnglish();

        if (string.IsNullOrWhiteSpace(key))
            return isEng ? "Equipped Dice : Not Equipped" : "장착 주사위 : 장착 안 함";

        string[] split = key.Split('|');
        if (split.Length < 2)
            return isEng ? $"Equipped Dice : {key}" : $"장착 주사위 : {key}";

        DiceGrade? grade = ParseGrade(key);
        string star = split[1];

        string gradeText = grade.HasValue
            ? GetGradeText(grade.Value, isEng)
            : split[0];

        return isEng
            ? $"Equipped Dice : {gradeText} / ★{star}"
            : $"장착 주사위 : {gradeText} / {star}성";
    }

    void ApplyDiceVisual(string equippedDiceKey)
    {
        DiceGrade? grade = ParseGrade(equippedDiceKey);

        if (equipDiceImage != null)
        {
            if (defaultDiceSprite != null)
                equipDiceImage.sprite = defaultDiceSprite;

            DiceGradeColorUtil.Apply(
                equipDiceImage,
                grade ?? DiceGrade.Common,
                ref _rainbow
            );
        }

        if (txtEquippedDice != null)
        {
            if (grade.HasValue)
                ApplyGradeTextColor(txtEquippedDice, grade.Value);
            else
                txtEquippedDice.color = Color.white;
        }
    }

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

    bool IsEnglish()
    {
        return LaguageManager.Instance != null &&
               LaguageManager.Instance.currentLang == Lauaguage.Eng;
    }
}