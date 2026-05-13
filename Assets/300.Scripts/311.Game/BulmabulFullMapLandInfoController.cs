using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// BulmabulCameraFollow의 전체 맵 보기 상태와
/// BulmabulMapLandInfoView를 연결하는 컨트롤러.
/// 
/// 역할:
/// 1. 전체 맵 보기 ON  -> 모든 땅 정보판 표시
/// 2. 전체 맵 보기 OFF -> 모든 땅 정보판 숨김
/// 3. 땅 소유자 / 가격 / 통행료 / 건물 상태 갱신
/// 
/// 이 스크립트는 전체 맵 보기를 직접 하지 않는다.
/// 전체 맵 보기 자체는 BulmabulCameraFollow가 담당한다.
/// </summary>
public class BulmabulFullMapLandInfoController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private BulmabulCameraFollow cameraFollow;
    [SerializeField] private BulmabulBoard board;

    [Header("Options")]
    [Tooltip("Board / CameraFollow를 자동으로 찾을지 여부")]
    [SerializeField] private bool autoFindReferences = true;

    [Tooltip("셀 오브젝트에 BulmabulMapLandInfoView가 없으면 자동으로 붙인다")]
    [SerializeField] private bool autoAddInfoView = true;

    [Tooltip("구매되지 않은 땅도 전체 맵에서 가격과 소유 없음 표시")]
    [SerializeField] private bool showNoOwnerLand = true;

    [Tooltip("전체 맵 보기 중 일정 간격으로 강제 갱신")]
    [SerializeField] private float refreshInterval = 0.35f;

    private readonly List<BulmabulMapLandInfoView> infoViews = new List<BulmabulMapLandInfoView>();

    private bool _initialized;
    private float _refreshTimer;
    private bool _lastFullMapState;

    private void Awake()
    {
        FindReferencesIfNeeded();
    }

    private void OnEnable()
    {
        FindReferencesIfNeeded();
        SubscribeCameraEvent();
    }

    private void Start()
    {
        BuildInfoViews();
        RefreshVisibleState();
    }

    private void OnDisable()
    {
        UnsubscribeCameraEvent();
    }

    private void Update()
    {
        FindReferencesIfNeeded();

        if (!_initialized)
        {
            BuildInfoViews();
            RefreshVisibleState();
        }

        if (cameraFollow == null)
            return;

        bool isFullMap = cameraFollow.IsFullMapView;

        if (_lastFullMapState != isFullMap)
        {
            _lastFullMapState = isFullMap;
            RefreshVisibleState();
        }

        if (!isFullMap)
            return;

        if (refreshInterval <= 0f)
            return;

        _refreshTimer += Time.deltaTime;

        if (_refreshTimer >= refreshInterval)
        {
            _refreshTimer = 0f;
            RefreshVisibleState();
        }
    }

    /// <summary>
    /// 전체 맵 보기 상태가 바뀌었을 때 카메라에서 호출된다.
    /// </summary>
    private void HandleFullMapViewChanged(bool isFullMapView)
    {
        _lastFullMapState = isFullMapView;
        RefreshVisibleState();
    }

    /// <summary>
    /// Board 안의 셀들을 찾아서 BulmabulMapLandInfoView와 연결한다.
    /// </summary>
    public void BuildInfoViews()
    {
        infoViews.Clear();

        FindReferencesIfNeeded();

        if (board == null)
        {
            Debug.LogWarning("[BulmabulFullMapLandInfoController] Board가 없습니다.");
            _initialized = false;
            return;
        }

        int count = board.CellCount;

        for (int i = 0; i < count; i++)
        {
            Transform cellTransform = board.GetCellTransform(i);

            if (cellTransform == null)
                continue;

            BulmabulMapLandInfoView infoView =
                cellTransform.GetComponent<BulmabulMapLandInfoView>();

            if (infoView == null)
                infoView = cellTransform.GetComponentInChildren<BulmabulMapLandInfoView>(true);

            if (infoView == null && autoAddInfoView)
                infoView = cellTransform.gameObject.AddComponent<BulmabulMapLandInfoView>();

            if (infoView == null)
                continue;

            BulmabulCellData cellData = board.GetCell(i);

            infoView.Setup(i, cellData);
            infoView.SetVisible(false);

            if (!infoViews.Contains(infoView))
                infoViews.Add(infoView);
        }

        _initialized = true;

        Debug.Log($"[BulmabulFullMapLandInfoController] 연결된 땅 정보판 수: {infoViews.Count}");
    }

    /// <summary>
    /// 현재 전체 맵 보기 상태에 맞춰 정보판 표시/숨김 및 내용 갱신.
    /// </summary>
    public void RefreshVisibleState()
    {
        if (cameraFollow == null)
        {
            HideAll();
            return;
        }

        bool isFullMap = cameraFollow.IsFullMapView;
        BulmabulGameState state = BulmabulGameState.Instance;

        for (int i = 0; i < infoViews.Count; i++)
        {
            BulmabulMapLandInfoView infoView = infoViews[i];

            if (infoView == null)
                continue;

            BulmabulCellData data = infoView.CellData;

            bool visible = isFullMap;

            if (data == null)
                visible = false;

            if (data != null && data.cellType != BulmabulCellType.Land)
                visible = false;

            if (visible && state != null && data != null)
            {
                int ownerIndex = state.LandOwnerByCell.Get(infoView.CellIndex);

                if (ownerIndex < 0 && !showNoOwnerLand)
                    visible = false;
            }

            infoView.SetVisible(visible);

            if (visible)
                infoView.Refresh(state);
        }
    }

    /// <summary>
    /// 모든 정보판 숨김.
    /// </summary>
    public void HideAll()
    {
        for (int i = 0; i < infoViews.Count; i++)
        {
            if (infoViews[i] != null)
                infoViews[i].SetVisible(false);
        }
    }

    private void SubscribeCameraEvent()
    {
        if (cameraFollow == null)
            return;

        cameraFollow.OnFullMapViewChanged -= HandleFullMapViewChanged;
        cameraFollow.OnFullMapViewChanged += HandleFullMapViewChanged;
    }

    private void UnsubscribeCameraEvent()
    {
        if (cameraFollow == null)
            return;

        cameraFollow.OnFullMapViewChanged -= HandleFullMapViewChanged;
    }

    private void FindReferencesIfNeeded()
    {
        if (!autoFindReferences)
            return;

        if (cameraFollow == null)
            cameraFollow = FindFirstObjectByType<BulmabulCameraFollow>();

        if (board == null)
            board = FindFirstObjectByType<BulmabulBoard>();

        SubscribeCameraEvent();
    }
}