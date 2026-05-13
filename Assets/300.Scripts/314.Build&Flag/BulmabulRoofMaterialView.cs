using UnityEngine;

/// <summary>
/// 건물 지붕 색상(Material) 교체 담당.
/// Material.color는 절대 변경하지 않고, 미리 만들어둔 지붕 Material을 통째로 교체한다.
///
/// 지붕 Material 슬롯 규칙:
/// - SmallHouse / House  = Element 1
/// - BigHouse            = 마지막 Element
/// - Hotel               = Element 0
///
/// 개인전:
/// 0번 플레이어 = 빨강 지붕
/// 1번 플레이어 = 파랑 지붕
/// 2번 플레이어 = 회색 지붕
/// 3번 플레이어 = 노랑 지붕
///
/// 팀전:
/// Red Team = 빨강 지붕
/// Blue Team = 파랑 지붕
///
/// 소유자 없음:
/// 기본 지붕 Material
/// </summary>
public class BulmabulRoofMaterialView : MonoBehaviour
{
    [Header("Roof Renderer")]
    [Tooltip("지붕 색상을 바꿀 Renderer. 비워두면 자식 Renderer에서 자동 탐색한다.")]
    [SerializeField] private Renderer roofRenderer;

    [Header("Material Slot")]
    [Tooltip("건물 이름에 따라 지붕 Material Index를 자동 결정한다.")]
    [SerializeField] private bool autoDetectRoofMaterialIndex = true;

    [Tooltip("자동 결정 OFF일 때 사용할 지붕 Material 슬롯 번호")]
    [SerializeField] private int roofMaterialIndex = 0;

    [Header("Roof Materials")]
    [Tooltip("소유자가 없거나 기본 상태일 때 사용할 지붕 Material")]
    [SerializeField] private Material defaultRoofMaterial;

    [Tooltip("빨강 지붕 Material")]
    [SerializeField] private Material redRoofMaterial;

    [Tooltip("파랑 지붕 Material")]
    [SerializeField] private Material blueRoofMaterial;

    [Tooltip("회색 지붕 Material")]
    [SerializeField] private Material grayRoofMaterial;

    [Tooltip("노랑 지붕 Material")]
    [SerializeField] private Material yellowRoofMaterial;

    private void Awake()
    {
        if (roofRenderer == null)
            roofRenderer = GetComponentInChildren<Renderer>(true);

        RefreshRoofMaterialIndex();

        SetDefaultRoof();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (roofRenderer == null)
            roofRenderer = GetComponentInChildren<Renderer>(true);

        RefreshRoofMaterialIndex();
    }
#endif

    /// <summary>
    /// 자동 감지가 켜져 있으면 오브젝트 이름에 따라 지붕 Material 슬롯 번호를 갱신한다.
    /// </summary>
    private void RefreshRoofMaterialIndex()
    {
        if (!autoDetectRoofMaterialIndex)
            return;

        roofMaterialIndex = GetRoofMaterialIndexByObjectName();
    }

    /// <summary>
    /// 오브젝트 이름에 따라 지붕 Material 슬롯 번호를 결정한다.
    ///
    /// SmallHouse / House = Element 1
    /// BigHouse           = 마지막 Element
    /// Hotel              = Element 0
    /// </summary>
    private int GetRoofMaterialIndexByObjectName()
    {
        int materialCount = GetMaterialCount();

        if (materialCount <= 0)
            return 0;

        string lowerName = gameObject.name.ToLowerInvariant();

        // Hotel은 0번 슬롯
        if (lowerName.Contains("hotel"))
            return 0;

        // BigHouse는 마지막 슬롯
        // BigHouse에는 house라는 글자도 들어가므로 house보다 먼저 검사해야 한다.
        if (lowerName.Contains("bighouse"))
            return Mathf.Max(0, materialCount - 1);

        // SmallHouse / House는 1번 슬롯
        if (lowerName.Contains("smallhouse") || lowerName.Contains("house"))
            return materialCount > 1 ? 1 : 0;

        // 이름으로 판단할 수 없으면 현재 설정값 유지
        if (roofMaterialIndex < 0 || roofMaterialIndex >= materialCount)
            return 0;

        return roofMaterialIndex;
    }

    /// <summary>
    /// 현재 Renderer의 Material 개수를 가져온다.
    /// </summary>
    private int GetMaterialCount()
    {
        if (roofRenderer == null)
            return 0;

        Material[] materials = roofRenderer.sharedMaterials;

        if (materials == null)
            return 0;

        return materials.Length;
    }

    /// <summary>
    /// 지붕을 기본 Material로 되돌린다.
    /// </summary>
    public void SetDefaultRoof()
    {
        ApplyMaterial(defaultRoofMaterial);
    }

    /// <summary>
    /// 소유자 / 팀 모드에 따라 지붕 Material을 갱신한다.
    /// </summary>
    public void RefreshRoof(bool isTeamMode, int ownerIndex, TeamSide ownerTeamSide)
    {
        RefreshRoofMaterialIndex();

        if (ownerIndex < 0)
        {
            SetDefaultRoof();
            return;
        }

        if (isTeamMode)
        {
            SetTeamRoof(ownerTeamSide);
            return;
        }

        SetIndividualRoof(ownerIndex);
    }

    /// <summary>
    /// 개인전 기준 지붕 Material 적용.
    /// </summary>
    private void SetIndividualRoof(int ownerIndex)
    {
        switch (ownerIndex)
        {
            case 0:
                ApplyMaterial(redRoofMaterial);
                break;

            case 1:
                ApplyMaterial(blueRoofMaterial);
                break;

            case 2:
                ApplyMaterial(grayRoofMaterial);
                break;

            case 3:
                ApplyMaterial(yellowRoofMaterial);
                break;

            default:
                SetDefaultRoof();
                break;
        }
    }

    /// <summary>
    /// 팀전 기준 지붕 Material 적용.
    /// </summary>
    private void SetTeamRoof(TeamSide teamSide)
    {
        switch (teamSide)
        {
            case TeamSide.Red:
                ApplyMaterial(redRoofMaterial);
                break;

            case TeamSide.Blue:
                ApplyMaterial(blueRoofMaterial);
                break;

            default:
                SetDefaultRoof();
                break;
        }
    }

    /// <summary>
    /// Material.color를 바꾸지 않고, 지정 슬롯의 Material 자체를 교체한다.
    /// 팔레트 텍스처 방식에서는 이 방식이 가장 안전하다.
    /// </summary>
    private void ApplyMaterial(Material targetMaterial)
    {
        if (roofRenderer == null)
            return;

        if (targetMaterial == null)
        {
            Debug.LogWarning("[BulmabulRoofMaterialView] 지붕 Material이 연결되지 않았습니다.", this);
            return;
        }

        Material[] materials = roofRenderer.sharedMaterials;

        if (materials == null || materials.Length == 0)
            return;

        if (roofMaterialIndex < 0 || roofMaterialIndex >= materials.Length)
        {
            Debug.LogWarning(
                $"[BulmabulRoofMaterialView] roofMaterialIndex가 잘못되었습니다. Index: {roofMaterialIndex}, Length: {materials.Length}",
                this
            );
            return;
        }

        materials[roofMaterialIndex] = targetMaterial;
        roofRenderer.sharedMaterials = materials;
    }
}