using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 게임씬에서 플레이어 1명분의 정보 UI를 표시하는 스크립트.
/// 
/// 연결 대상:
/// - 프로필 이미지
/// - 닉네임
/// - 레벨
/// - 게임 재화
/// - 팀 표시
/// - 턴 순서
/// - 리더 이미지
/// </summary>
public class BulmabulPlayerInfoUI : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject root;

    [Header("Profile")]
    [SerializeField] private RawImage imgProfile;

    [Header("Texts")]
    [SerializeField] private TMP_Text txtNickname;
    [SerializeField] private TMP_Text txtLevel;
    [SerializeField] private TMP_Text txtCash;
    [SerializeField] private TMP_Text txtTeam;
    [SerializeField] private TMP_Text txtOrder;

    [Header("Icons")]
    [SerializeField] private Image imgLeader;

    [Header("Default")]
    [SerializeField] private Texture defaultProfileTexture;

    public RawImage ProfileImage => imgProfile;
    public Texture DefaultProfileTexture => defaultProfileTexture;

    /// <summary>
    /// 플레이어가 없을 때 UI 숨김.
    /// </summary>
    public void Hide()
    {
        if (root != null)
            root.SetActive(false);
        else
            gameObject.SetActive(false);
    }

    /// <summary>
    /// 플레이어 정보 표시.
    /// </summary>
    public void Show(
        string nickname,
        int level,
        int cash,
        int teamSideInt,
        int turnOrder,
        bool isLeader,
        bool isBankrupt)
    {
        if (root != null)
            root.SetActive(true);
        else
            gameObject.SetActive(true);

        if (txtNickname != null)
            txtNickname.text = string.IsNullOrWhiteSpace(nickname) ? "-" : nickname;

        if (txtLevel != null)
            txtLevel.text = $"Lv.{level}";

        if (txtCash != null)
            txtCash.text = $"{cash:N0}";

        if (txtOrder != null)
            txtOrder.text = $"{turnOrder}번";

        if (txtTeam != null)
            txtTeam.text = GetTeamText(teamSideInt);

        if (imgLeader != null)
            imgLeader.gameObject.SetActive(isLeader);

        if (isBankrupt && txtCash != null)
            txtCash.text = "파산";
    }

    /// <summary>
    /// 프로필 이미지 적용.
    /// </summary>
    public void SetProfileTexture(Texture texture)
    {
        if (imgProfile == null)
            return;

        imgProfile.texture = texture != null ? texture : defaultProfileTexture;
    }

    private string GetTeamText(int teamSideInt)
    {
        if (teamSideInt == (int)TeamSide.Red)
            return "RED";

        if (teamSideInt == (int)TeamSide.Blue)
            return "BLUE";

        return "개인전";
    }
}