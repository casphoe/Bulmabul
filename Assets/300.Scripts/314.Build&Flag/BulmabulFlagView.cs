using UnityEngine;

/// <summary>
/// 땅 소유자에 따라 Flag Material만 교체한다.
/// Material 색상값은 절대 변경하지 않는다.
/// 
/// 개인전:
/// 0번 플레이어 = 빨강
/// 1번 플레이어 = 파랑
/// 2번 플레이어 = 회색
/// 3번 플레이어 = 노랑
/// 
/// 팀전:
/// Red Team = 빨강
/// Blue Team = 파랑
/// 
/// 소유자 없음:
/// 흰색
/// </summary>
public class BulmabulFlagView : MonoBehaviour
{
    [Header("Flag Renderer")]
    [SerializeField] private Renderer flagRenderer;

    [Header("Flag Materials")]
    [SerializeField] private Material whiteFlagMaterial;
    [SerializeField] private Material redFlagMaterial;
    [SerializeField] private Material blueFlagMaterial;
    [SerializeField] private Material grayFlagMaterial;
    [SerializeField] private Material yellowFlagMaterial;

    private void Awake()
    {        
        SetWhiteFlag();
    }

    public void SetWhiteFlag()
    {
        ApplyMaterial(whiteFlagMaterial);
    }

    /// <summary>
    /// 개인전일 때 플레이어 순서에 맞는 깃발 적용.
    /// ownerIndex는 BulmabulGameState.Players 슬롯 인덱스 기준.
    /// </summary>
    public void SetIndividualFlag(int ownerIndex)
    {
        switch (ownerIndex)
        {
            case 0:
                ApplyMaterial(redFlagMaterial);
                break;

            case 1:
                ApplyMaterial(blueFlagMaterial);
                break;

            case 2:
                ApplyMaterial(grayFlagMaterial);
                break;

            case 3:
                ApplyMaterial(yellowFlagMaterial);
                break;

            default:
                SetWhiteFlag();
                break;
        }
    }

    /// <summary>
    /// 팀전일 때 팀에 맞는 깃발 적용.
    /// 기존 프로젝트의 TeamSide enum을 사용한다.
    /// </summary>
    public void SetTeamFlag(TeamSide teamSide)
    {
        switch (teamSide)
        {
            case TeamSide.Red:
                ApplyMaterial(redFlagMaterial);
                break;

            case TeamSide.Blue:
                ApplyMaterial(blueFlagMaterial);
                break;

            default:
                SetWhiteFlag();
                break;
        }
    }

    /// <summary>
    /// 최종 깃발 갱신 함수.
    /// isTeamMode가 true면 팀 색상 기준,
    /// false면 개인전 플레이어 색상 기준으로 처리한다.
    /// </summary>
    public void RefreshFlag(bool isTeamMode, int ownerIndex, TeamSide ownerTeamSide)
    {
        if (ownerIndex < 0)
        {
            SetWhiteFlag();
            return;
        }

        if (isTeamMode)
        {
            SetTeamFlag(ownerTeamSide);
            return;
        }

        SetIndividualFlag(ownerIndex);
    }

    private void ApplyMaterial(Material material)
    {
        if (flagRenderer == null)
            return;

        if (material == null)
        {
            Debug.LogWarning("[BulmabulFlagView] Flag Material이 연결되지 않았습니다.", this);
            return;
        }

        // 중요:
        // material.color 변경 금지.
        // 팔레트 텍스처 방식은 Material 교체만 해야 색이 안 깨진다.
        flagRenderer.sharedMaterial = material;
    }
}