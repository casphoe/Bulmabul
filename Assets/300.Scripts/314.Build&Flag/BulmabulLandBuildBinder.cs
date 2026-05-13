using UnityEngine;

/// <summary>
/// Cell Prefab의 자식 오브젝트에 붙이는 건물 표시용 Binder.
/// 
/// 역할:
/// - LandBuildingFlagsByCell 값을 보고 건물 프리팹을 생성/삭제한다.
/// - 땅 소유자가 없으면 건물을 전부 제거한다.
/// - 건물 상태가 바뀌면 필요한 건물만 다시 갱신한다.
/// 
/// 주의:
/// - 건물 프리팹은 NetworkObject로 Spawn하지 않는다.
/// - LandBuildingFlagsByCell 값이 네트워크로 동기화되므로,
///   각 클라이언트가 자기 화면에서 로컬로 생성하면 된다.
/// </summary>
public class BulmabulLandBuildBinder : MonoBehaviour
{
    [Header("References")]
    [Tooltip("현재 Cell View. 비워두면 부모에서 자동으로 찾는다.")]
    [SerializeField] private BulmabulBoardCellView cellView;

    [Header("Build Prefabs")]
    [Tooltip("작은집 프리팹")]
    [SerializeField] private GameObject smallHousePrefab;

    [Tooltip("집 프리팹")]
    [SerializeField] private GameObject housePrefab;

    [Tooltip("큰집 프리팹")]
    [SerializeField] private GameObject bigHousePrefab;

    [Tooltip("호텔 프리팹")]
    [SerializeField] private GameObject hotelPrefab;

    [Header("Spawn Root")]
    [Tooltip("건물들이 생성될 부모. 비워두면 이 오브젝트 밑에 생성한다.")]
    [SerializeField] private Transform buildRoot;

    [Header("Build Local Positions")]
    [Tooltip("작은집 위치")]
    [SerializeField] private Vector3 smallHouseLocalPosition = new Vector3(-0.4f, 0.467f, 0.15f);

    [Tooltip("집 위치. 작은집 기준으로 X 값만 다르게 배치")]
    [SerializeField] private Vector3 houseLocalPosition = new Vector3(0f, 0.467f, 0.15f);

    [Tooltip("큰집 위치. 작은집 기준으로 X 값만 다르게 배치")]
    [SerializeField] private Vector3 bigHouseLocalPosition = new Vector3(0.4f, 0.467f, 0.15f);

    [Tooltip("호텔은 하위 건물 대신 중앙에 하나만 표시")]
    [SerializeField] private Vector3 hotelLocalPosition = new Vector3(0f, 0.467f, 0.15f);

    [Header("Build Local Rotation")]
    [SerializeField] private Vector3 buildLocalEulerAngles = Vector3.zero;

    [Header("Build Local Scale")]
    [Tooltip("작은집 / 집 / 큰집 크기")]
    [SerializeField] private Vector3 normalBuildLocalScale = new Vector3(0.05f, 0.05f, 0.05f);

    [Tooltip("호텔 크기")]
    [SerializeField] private Vector3 hotelBuildLocalScale = new Vector3(0.03f, 0.03f, 0.03f);

    private GameObject _smallHouseInstance;
    private GameObject _houseInstance;
    private GameObject _bigHouseInstance;
    private GameObject _hotelInstance;

    private int _lastRevision = -1;
    private int _lastOwnerIndex = -999;
    private int _lastBuildFlags = -999;

    private void Awake()
    {
        if (cellView == null)
            cellView = GetComponentInParent<BulmabulBoardCellView>();

        if (buildRoot == null)
            buildRoot = transform;

        // Binder 자체는 Cell 기준 0,0,0 고정
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;
    }

    private void Start()
    {
        RefreshBuilds(true);
    }

    private void Update()
    {
        RefreshBuilds(false);
    }

    /// <summary>
    /// 현재 Cell의 건물 상태를 읽고 프리팹 생성/삭제를 갱신한다.
    /// </summary>
    private void RefreshBuilds(bool force)
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null || !state.IsSpawnReady)
        {
            ClearAllBuilds();
            return;
        }

        if (cellView == null)
            return;

        int cellIndex = cellView.CellIndex;

        if (cellIndex < 0 || cellIndex >= BulmabulGameState.MaxCells)
        {
            ClearAllBuilds();
            return;
        }

        BulmabulCellData cell = cellView.CellData;

        if (cell == null || cell.cellType != BulmabulCellType.Land)
        {
            ClearAllBuilds();
            return;
        }

        int revision = state.Revision;

        if (!force && revision == _lastRevision)
            return;

        _lastRevision = revision;

        int ownerIndex = state.LandOwnerByCell.Get(cellIndex);
        int buildFlags = state.LandBuildingFlagsByCell.Get(cellIndex);

        // 소유자가 없으면 건물도 전부 제거
        if (ownerIndex < 0)
        {
            _lastOwnerIndex = ownerIndex;
            _lastBuildFlags = BulmabulBuildFlags.None;

            ClearAllBuilds();
            return;
        }

        if (!force &&
            ownerIndex == _lastOwnerIndex &&
            buildFlags == _lastBuildFlags)
        {
            return;
        }

        _lastOwnerIndex = ownerIndex;
        _lastBuildFlags = buildFlags;

        ApplyBuildFlags(buildFlags);
    }

    /// <summary>
    /// buildFlags에 맞게 건물 프리팹을 생성/삭제한다.
    /// 
    /// 중요:
    /// - 호텔이 있으면 작은집/집/큰집은 전부 제거하고 호텔만 표시한다.
    /// - 호텔이 없을 때만 작은집/집/큰집을 표시한다.
    /// </summary>
    private void ApplyBuildFlags(int buildFlags)
    {
        bool hasHotel = BulmabulBuildFlags.Has(buildFlags, BulmabulBuildFlags.Hotel);

        // 호텔이 있으면 하위 건물은 전부 제거하고 호텔만 표시
        if (hasHotel)
        {
            UpdateBuildInstance(false, ref _smallHouseInstance, smallHousePrefab, smallHouseLocalPosition, "SmallHouse");
            UpdateBuildInstance(false, ref _houseInstance, housePrefab, houseLocalPosition, "House");
            UpdateBuildInstance(false, ref _bigHouseInstance, bigHousePrefab, bigHouseLocalPosition, "BigHouse");

            UpdateBuildInstance(true, ref _hotelInstance, hotelPrefab, hotelLocalPosition, "Hotel");
            return;
        }

        // 호텔이 없으면 호텔 제거
        UpdateBuildInstance(false, ref _hotelInstance, hotelPrefab, hotelLocalPosition, "Hotel");

        bool hasSmallHouse = BulmabulBuildFlags.Has(buildFlags, BulmabulBuildFlags.SmallHouse);
        bool hasHouse = BulmabulBuildFlags.Has(buildFlags, BulmabulBuildFlags.House);
        bool hasBigHouse = BulmabulBuildFlags.Has(buildFlags, BulmabulBuildFlags.BigHouse);

        UpdateBuildInstance(
            hasSmallHouse,
            ref _smallHouseInstance,
            smallHousePrefab,
            smallHouseLocalPosition,
            "SmallHouse"
        );

        UpdateBuildInstance(
            hasHouse,
            ref _houseInstance,
            housePrefab,
            houseLocalPosition,
            "House"
        );

        UpdateBuildInstance(
            hasBigHouse,
            ref _bigHouseInstance,
            bigHousePrefab,
            bigHouseLocalPosition,
            "BigHouse"
        );
    }

    /// <summary>
    /// 특정 건물 하나를 생성하거나 삭제한다.
    /// SmallHouse / House / BigHouse는 0.05 크기,
    /// Hotel은 0.03 크기로 생성한다.
    /// </summary>
    private void UpdateBuildInstance(
        bool shouldExist,
        ref GameObject instance,
        GameObject prefab,
        Vector3 localPosition,
        string objectName)
    {
        if (shouldExist)
        {
            if (instance != null)
                return;

            if (prefab == null)
            {
                Debug.LogWarning($"[BulmabulLandBuildBinder] {objectName} Prefab이 연결되지 않았습니다.", this);
                return;
            }

            instance = Instantiate(prefab, buildRoot);
            instance.name = objectName;

            Transform tr = instance.transform;
            tr.localPosition = localPosition;
            tr.localRotation = Quaternion.Euler(buildLocalEulerAngles);
            tr.localScale = GetBuildScale(objectName);

            BulmabulRoofMaterialView roofView = instance.GetComponentInChildren<BulmabulRoofMaterialView>(true);

            if (roofView != null)
            {
                bool isTeamMode = BulmabulGameStartCache.HasCache &&
                                  BulmabulGameStartCache.ModeInt == (int)MatchMode.Team;

                TeamSide ownerTeamSide = GetOwnerTeamSide(BulmabulGameState.Instance, _lastOwnerIndex);

                roofView.RefreshRoof(isTeamMode, _lastOwnerIndex, ownerTeamSide);
            }

            return;
        }

        if (instance == null)
            return;

        Destroy(instance);
        instance = null;
    }

    /// <summary>
    /// 건물 이름에 따라 생성 크기를 반환한다.
    /// Hotel은 0.03, 나머지 건물은 0.05 크기.
    /// </summary>
    private Vector3 GetBuildScale(string objectName)
    {
        if (!string.IsNullOrEmpty(objectName) &&
            objectName.ToLowerInvariant().Contains("hotel"))
        {
            return hotelBuildLocalScale;
        }

        return normalBuildLocalScale;
    }

    /// <summary>
    /// 소유자 플레이어의 팀 정보를 가져온다.
    /// 개인전이면 TeamSide.None이어도 상관없다.
    /// </summary>
    private TeamSide GetOwnerTeamSide(BulmabulGameState state, int ownerIndex)
    {
        if (state == null)
            return TeamSide.None;

        if (ownerIndex < 0 || ownerIndex >= BulmabulGameState.MaxPlayers)
            return TeamSide.None;

        BulmabulGameState.PlayerGameSlot slot = state.Players.Get(ownerIndex);

        if (slot.occupied == 0)
            return TeamSide.None;

        return (TeamSide)slot.teamSideInt;
    }

    /// <summary>
    /// 이 Cell에 표시된 모든 건물을 제거한다.
    /// 땅 소유자가 없어지거나 게임 상태가 초기화될 때 사용한다.
    /// </summary>
    private void ClearAllBuilds()
    {
        DestroyInstance(ref _smallHouseInstance);
        DestroyInstance(ref _houseInstance);
        DestroyInstance(ref _bigHouseInstance);
        DestroyInstance(ref _hotelInstance);
    }

    private void DestroyInstance(ref GameObject instance)
    {
        if (instance == null)
            return;

        Destroy(instance);
        instance = null;
    }
}