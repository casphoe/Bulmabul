using UnityEngine;

/// <summary>
/// Cell Prefab의 자식 오브젝트에 붙이는 스크립트.
/// 
/// 역할:
/// - 땅 소유자가 없으면 Flag를 제거한다.
/// - 땅 구매 후 소유자가 생기면 Flag Prefab을 생성한다.
/// - 개인전이면 플레이어 번호에 맞는 Flag Material을 적용한다.
/// - 팀전이면 Red / Blue 팀에 맞는 Flag Material을 적용한다.
/// 
/// 위치:
/// Cell_XXX
///  └─ LandFlagBinder
///       └─ Flag(Clone)
/// </summary>
public class BulmabulLandFlagBinder : MonoBehaviour
{
    [Header("References")]
    [Tooltip("현재 Cell의 View. 비워두면 부모에서 자동으로 찾는다.")]
    [SerializeField] private BulmabulBoardCellView cellView;

    [Tooltip("생성할 Flag Prefab")]
    [SerializeField] private BulmabulFlagView flagPrefab;

    [Header("Spawn Settings")]
    [Tooltip("Cell 중심 기준 Flag 생성 위치. Pawn보다 위에 보이도록 Y 3 권장")]
    [SerializeField] private Vector3 flagLocalPosition = new Vector3(0f, 3, 0f);

    [Tooltip("Flag 회전값")]
    [SerializeField] private Vector3 flagLocalEulerAngles = Vector3.zero;

    [Tooltip("Flag 크기")]
    [SerializeField] private Vector3 flagLocalScale = Vector3.one;

    private BulmabulFlagView _spawnedFlag;

    private int _lastRevision = -1;
    private int _lastOwnerIndex = -999;
    private bool _lastIsTeamMode = false;
    private TeamSide _lastOwnerTeamSide = TeamSide.None;

    private void Awake()
    {
        if (cellView == null)
            cellView = GetComponentInParent<BulmabulBoardCellView>();

        // Binder 자체 위치는 Cell 기준 0,0,0으로 고정
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;
    }

    private void Start()
    {
        RefreshFlag(true);
    }

    private void Update()
    {
        RefreshFlag(false);
    }

    /// <summary>
    /// 현재 Cell의 소유자 정보를 보고 Flag를 생성 / 제거 / 색상 갱신한다.
    /// </summary>
    private void RefreshFlag(bool force)
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null || !state.IsSpawnReady)
        {
            RemoveFlag();
            return;
        }

        if (cellView == null)
            return;

        int cellIndex = cellView.CellIndex;

        if (cellIndex < 0 || cellIndex >= BulmabulGameState.MaxCells)
        {
            RemoveFlag();
            return;
        }

        BulmabulCellData cell = cellView.CellData;

        if (cell == null || cell.cellType != BulmabulCellType.Land)
        {
            RemoveFlag();
            return;
        }

        int revision = state.Revision;

        if (!force && revision == _lastRevision)
            return;

        _lastRevision = revision;

        int ownerIndex = state.LandOwnerByCell.Get(cellIndex);

        // 소유자가 없으면 Flag 제거
        if (ownerIndex < 0)
        {
            _lastOwnerIndex = ownerIndex;
            _lastIsTeamMode = false;
            _lastOwnerTeamSide = TeamSide.None;

            RemoveFlag();
            return;
        }

        bool isTeamMode = IsTeamMode();

        TeamSide ownerTeamSide = GetOwnerTeamSide(state, ownerIndex);

        if (!force &&
            ownerIndex == _lastOwnerIndex &&
            isTeamMode == _lastIsTeamMode &&
            ownerTeamSide == _lastOwnerTeamSide)
        {
            return;
        }

        _lastOwnerIndex = ownerIndex;
        _lastIsTeamMode = isTeamMode;
        _lastOwnerTeamSide = ownerTeamSide;

        EnsureFlagCreated();

        if (_spawnedFlag == null)
            return;

        _spawnedFlag.RefreshFlag(isTeamMode, ownerIndex, ownerTeamSide);
    }

    /// <summary>
    /// Flag가 없으면 생성한다.
    /// 생성 위치는 LandFlagBinder 기준 localPosition 0, 1.5, 0.
    /// </summary>
    private void EnsureFlagCreated()
    {
        if (_spawnedFlag != null)
            return;

        if (flagPrefab == null)
        {
            Debug.LogWarning("[BulmabulLandFlagBinder] Flag Prefab이 연결되지 않았습니다.", this);
            return;
        }

        _spawnedFlag = Instantiate(flagPrefab, transform);

        Transform flagTransform = _spawnedFlag.transform;
        flagTransform.localPosition = flagLocalPosition;
        flagTransform.localRotation = Quaternion.Euler(flagLocalEulerAngles);
        flagTransform.localScale = flagLocalScale;
    }

    /// <summary>
    /// 소유자가 없어졌을 때 Flag 제거.
    /// </summary>
    private void RemoveFlag()
    {
        if (_spawnedFlag == null)
            return;

        Destroy(_spawnedFlag.gameObject);
        _spawnedFlag = null;
    }

    /// <summary>
    /// 현재 게임이 팀전인지 확인한다.
    /// 기존 프로젝트의 MatchMode.Team 값을 사용한다.
    /// </summary>
    private bool IsTeamMode()
    {
        return BulmabulGameStartCache.HasCache &&
               BulmabulGameStartCache.ModeInt == (int)MatchMode.Team;
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
}