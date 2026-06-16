using System;
using UnityEngine;

/// <summary>
/// 부루마불 게임용 카메라.
/// 
/// 역할:
/// 1. 기본 상태에서는 로컬 플레이어 Pawn을 45도 사선 시점으로 따라간다.
/// 2. 전체 맵 보기 버튼을 누르면 90도 탑뷰로 보드 전체를 보여준다.
/// 3. 전체 맵 보기 중 주사위를 굴리면 즉시 전체 맵 보기를 해제하고 Pawn 따라가기 모드로 돌아간다.
/// 
/// 주의:
/// - 이 스크립트는 Main Camera에 붙인다.
/// - UI 카메라에는 붙이지 않는다.
/// - target은 전체 맵 보기 중에도 유지된다.
/// </summary>
public class BulmabulCameraFollow : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("현재 따라갈 대상. 보통 로컬 플레이어 Pawn")]
    [SerializeField] private Transform target;

    [Header("Board")]
    [Tooltip("전체 맵 보기 중심 계산에 사용할 보드")]
    [SerializeField] private BulmabulBoard board;

    [Header("Follow View - 45 Degree")]
    [Tooltip("Pawn 따라가기 시 카메라 위치 오프셋. 이 값이 45도 사선 시점을 만든다.")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 10f, -8f);

    [Tooltip("Pawn 따라가기 이동 속도")]
    [SerializeField] private float followSpeed = 5f;

    [Tooltip("Pawn을 바라보는 회전 속도")]
    [SerializeField] private float rotationSpeed = 5f;

    [Tooltip("Pawn 따라가기 상태에서 Orthographic 카메라 크기")]
    [SerializeField] private float followOrthographicSize = 12;

    [Header("Look Settings")]
    [Tooltip("Pawn을 바라보게 할지 여부")]
    [SerializeField] private bool lookAtTarget = true;

    [Tooltip("Pawn을 바라볼 때 기준점을 살짝 위로 올리는 값")]
    [SerializeField] private Vector3 lookOffset = new Vector3(0f, 0.5f, 0f);

    [Header("Full Map View - 90 Degree")]
    [Tooltip("전체 맵 보기 사용 여부")]
    [SerializeField] private bool useFullMapView = true;

    [Tooltip("전체 맵 보기 시 카메라 높이")]
    [SerializeField] private float fullMapHeight = 90f;

    [Tooltip("전체 맵 보기 시 카메라 회전값. X=90이면 위에서 아래를 보는 탑뷰")]
    [SerializeField] private Vector3 fullMapRotation = new Vector3(90f, 0f, 0f);

    [Tooltip("전체 맵 보기 전환 이동 속도")]
    [SerializeField] private float fullMapMoveSpeed = 5f;

    [Tooltip("전체 맵 보기에서 Orthographic Size를 보드 크기에 맞게 자동 계산할지 여부")]
    [SerializeField] private bool autoFullMapOrthographicSize = true;

    [Tooltip("전체 맵 보기 Orthographic Size 여백")]
    [SerializeField] private float fullMapOrthographicPadding = 8f;

    [Tooltip("전체 맵 보기 최소 Orthographic Size")]
    [SerializeField] private float minFullMapOrthographicSize = 55f;

    [Header("Clamp")]
    [Tooltip("Pawn 따라가기 상태에서 카메라 이동 범위를 제한할지 여부")]
    [SerializeField] private bool useClamp = false;

    [Tooltip("카메라 X/Z 최소 범위")]
    [SerializeField] private Vector2 minXZ = new Vector2(-100f, -100f);

    [Tooltip("카메라 X/Z 최대 범위")]
    [SerializeField] private Vector2 maxXZ = new Vector2(100f, 100f);

    [Header("Snap")]
    [Tooltip("시작 시 타겟이 있으면 바로 해당 위치로 이동")]
    [SerializeField] private bool snapOnStart = false;

    private Camera _camera;
    private bool _isFullMapView = false;

    /// <summary>
    /// 현재 따라가는 대상.
    /// </summary>
    public Transform CurrentTarget => target;

    /// <summary>
    /// 현재 전체 맵 보기 상태인지 여부.
    /// UI 버튼 문구 변경에 사용한다.
    /// </summary>
    public bool IsFullMapView => _isFullMapView;

    /// <summary>
    /// 전체 맵 보기 상태가 바뀔 때 호출된다.
    /// true  = 전체 맵 보기
    /// false = Pawn 따라가기 보기
    /// </summary>
    public event Action<bool> OnFullMapViewChanged;

    /// <summary>
    /// Pawn 따라가기 카메라 오프셋.
    /// </summary>
    public Vector3 Offset
    {
        get => offset;
        set => offset = value;
    }

    private void Awake()
    {
        _camera = GetComponent<Camera>();

        if (_camera == null)
            _camera = Camera.main;
    }

    private void Start()
    {
        if (snapOnStart && target != null)
            SnapToTarget();
    }

    private void LateUpdate()
    {
        if (_isFullMapView)
        {
            FollowFullMapView();
            return;
        }

        if (target == null)
            return;

        FollowTarget();
    }

    /// <summary>
    /// 외부에서 Board를 연결할 때 사용한다.
    /// </summary>
    public void SetBoard(BulmabulBoard targetBoard)
    {
        board = targetBoard;
    }

    /// <summary>
    /// 전체 맵 보기 / Pawn 따라가기 모드를 토글한다.
    /// 전체 맵 보기면 90도 탑뷰,
    /// Pawn 따라가기면 기존 45도 사선 시점이다.
    /// </summary>
    public void ToggleFullMapView()
    {
        if (!useFullMapView)
            return;

        SetFullMapView(!_isFullMapView, false);
    }

    /// <summary>
    /// 전체 맵 보기 상태를 직접 설정한다.
    /// active = true  : 90도 전체 맵 보기
    /// active = false : 45도 Pawn 따라가기
    /// instant = true : 즉시 이동
    /// instant = false: 부드럽게 이동
    /// </summary>
    public void SetFullMapView(bool active, bool instant)
    {
        if (!useFullMapView)
            return;

        if (_isFullMapView == active)
            return;

        _isFullMapView = active;

        if (_isFullMapView)
        {
            if (instant)
                SnapToFullMapView();
        }
        else
        {
            if (instant)
                SnapToTarget();
        }

        OnFullMapViewChanged?.Invoke(_isFullMapView);
    }

    /// <summary>
    /// 전체 맵 보기 중이어도 강제로 Pawn 따라가기 보기로 되돌린다.
    /// 주사위를 굴릴 때 호출하면 된다.
    /// </summary>
    public void ForceFollowView(bool instant)
    {
        if (!_isFullMapView)
            return;

        _isFullMapView = false;

        if (instant)
            SnapToTarget();

        OnFullMapViewChanged?.Invoke(_isFullMapView);
    }

    /// <summary>
    /// 카메라 타겟만 변경한다.
    /// 전체 맵 보기 중에는 화면은 전체 맵을 유지하고 target만 저장한다.
    /// </summary>
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }

    /// <summary>
    /// 카메라 타겟을 변경하고 즉시 해당 위치로 이동한다.
    /// 단, 전체 맵 보기 중이면 target만 저장하고 화면은 전체 맵을 유지한다.
    /// </summary>
    public void SetTargetAndSnap(Transform newTarget)
    {
        target = newTarget;

        if (_isFullMapView)
            return;

        SnapToTarget();
    }

    /// <summary>
    /// 현재 타겟 위치로 즉시 이동한다.
    /// Pawn 따라가기 모드의 45도 시점으로 이동한다.
    /// </summary>
    public void SnapToTarget()
    {
        if (target == null)
            return;

        SnapToPosition(target.position);
    }

    /// <summary>
    /// 특정 월드 위치를 기준으로 카메라를 즉시 이동한다.
    /// Pawn 따라가기 모드의 45도 시점으로 이동한다.
    /// </summary>
    public void SnapToPosition(Vector3 worldPosition)
    {
        Vector3 desiredPosition = GetDesiredCameraPosition(worldPosition);

        transform.position = desiredPosition;

        if (lookAtTarget)
            LookAtPosition(worldPosition);

        ApplyFollowCameraSize(true);
    }

    /// <summary>
    /// 시작 지점 Cell 0 기준으로 카메라를 즉시 배치한다.
    /// </summary>
    public void SnapToStartCellPosition(Vector3 startCellPosition)
    {
        if (_isFullMapView)
            return;

        SnapToPosition(startCellPosition);
    }

    /// <summary>
    /// 전체 맵 보기 위치로 즉시 이동한다.
    /// 90도 탑뷰로 보드 전체를 보여준다.
    /// </summary>
    public void SnapToFullMapView()
    {
        Vector3 center = GetBoardCenterPosition();
        Vector3 desiredPosition = GetFullMapCameraPosition(center);

        transform.position = desiredPosition;
        transform.rotation = Quaternion.Euler(fullMapRotation);

        ApplyFullMapCameraSize(true);
    }

    /// <summary>
    /// 현재 타겟을 45도 사선 시점으로 부드럽게 따라간다.
    /// </summary>
    private void FollowTarget()
    {
        Vector3 desiredPosition = GetDesiredCameraPosition(target.position);

        transform.position = Vector3.Lerp(
            transform.position,
            desiredPosition,
            Time.deltaTime * followSpeed
        );

        if (lookAtTarget)
            SmoothLookAtPosition(target.position);

        ApplyFollowCameraSize(false);
    }

    /// <summary>
    /// 전체 맵 보기 상태에서 보드 중앙 위로 이동한다.
    /// 회전은 90도 탑뷰를 사용한다.
    /// </summary>
    private void FollowFullMapView()
    {
        Vector3 center = GetBoardCenterPosition();
        Vector3 desiredPosition = GetFullMapCameraPosition(center);
        Quaternion desiredRotation = Quaternion.Euler(fullMapRotation);

        transform.position = Vector3.Lerp(
            transform.position,
            desiredPosition,
            Time.deltaTime * fullMapMoveSpeed
        );

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            desiredRotation,
            Time.deltaTime * fullMapMoveSpeed
        );

        ApplyFullMapCameraSize(false);
    }

    /// <summary>
    /// Pawn 따라가기용 카메라 위치 계산.
    /// offset 값으로 45도 사선 느낌을 만든다.
    /// </summary>
    private Vector3 GetDesiredCameraPosition(Vector3 targetPosition)
    {
        Vector3 desiredPosition = targetPosition + offset;

        if (useClamp)
        {
            desiredPosition.x = Mathf.Clamp(desiredPosition.x, minXZ.x, maxXZ.x);
            desiredPosition.z = Mathf.Clamp(desiredPosition.z, minXZ.y, maxXZ.y);
        }

        return desiredPosition;
    }

    /// <summary>
    /// 전체 맵 보기용 카메라 위치 계산.
    /// 보드 중앙 바로 위로 올라간다.
    /// </summary>
    private Vector3 GetFullMapCameraPosition(Vector3 boardCenter)
    {
        return new Vector3(
            boardCenter.x,
            boardCenter.y + fullMapHeight,
            boardCenter.z
        );
    }

    /// <summary>
    /// 보드 전체의 중앙 위치를 계산한다.
    /// Board가 없으면 target 위치를 사용하고,
    /// target도 없으면 원점을 사용한다.
    /// </summary>
    private Vector3 GetBoardCenterPosition()
    {
        if (TryGetBoardBounds(out Bounds bounds))
            return bounds.center;

        if (target != null)
            return target.position;

        return Vector3.zero;
    }

    /// <summary>
    /// Board의 모든 Cell 위치를 기준으로 Bounds 계산.
    /// 전체 맵 보기 중앙과 Orthographic Size 계산에 사용한다.
    /// </summary>
    private bool TryGetBoardBounds(out Bounds bounds)
    {
        bounds = new Bounds(Vector3.zero, Vector3.zero);

        if (board == null || board.CellCount <= 0)
            return false;

        bool initialized = false;

        for (int i = 0; i < board.CellCount; i++)
        {
            Transform cell = board.GetCellTransform(i);

            if (cell == null)
                continue;

            if (!initialized)
            {
                bounds = new Bounds(cell.position, Vector3.zero);
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(cell.position);
            }
        }

        return initialized;
    }

    /// <summary>
    /// Pawn 따라가기 상태의 Orthographic Size 적용.
    /// </summary>
    private void ApplyFollowCameraSize(bool instant)
    {
        if (_camera == null)
            return;

        if (!_camera.orthographic)
            return;

        if (instant)
        {
            _camera.orthographicSize = followOrthographicSize;
            return;
        }

        _camera.orthographicSize = Mathf.Lerp(
            _camera.orthographicSize,
            followOrthographicSize,
            Time.deltaTime * followSpeed
        );
    }

    /// <summary>
    /// 전체 맵 보기 상태의 Orthographic Size 적용.
    /// </summary>
    private void ApplyFullMapCameraSize(bool instant)
    {
        if (_camera == null)
            return;

        if (!_camera.orthographic)
            return;

        float targetSize = minFullMapOrthographicSize;

        if (autoFullMapOrthographicSize && TryGetBoardBounds(out Bounds bounds))
        {
            float sizeX = bounds.size.x * 0.5f;
            float sizeZ = bounds.size.z * 0.5f;

            targetSize = Mathf.Max(sizeX, sizeZ) + fullMapOrthographicPadding;
            targetSize = Mathf.Max(targetSize, minFullMapOrthographicSize);
        }

        if (instant)
        {
            _camera.orthographicSize = targetSize;
            return;
        }

        _camera.orthographicSize = Mathf.Lerp(
            _camera.orthographicSize,
            targetSize,
            Time.deltaTime * fullMapMoveSpeed
        );
    }

    /// <summary>
    /// 특정 위치를 즉시 바라본다.
    /// Pawn 따라가기 45도 시점에서 사용한다.
    /// </summary>
    private void LookAtPosition(Vector3 worldPosition)
    {
        Vector3 lookPoint = worldPosition + lookOffset;
        Vector3 direction = lookPoint - transform.position;

        if (direction.sqrMagnitude <= 0.0001f)
            return;

        transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    /// <summary>
    /// 특정 위치를 부드럽게 바라본다.
    /// Pawn 따라가기 45도 시점에서 사용한다.
    /// </summary>
    private void SmoothLookAtPosition(Vector3 worldPosition)
    {
        Vector3 lookPoint = worldPosition + lookOffset;
        Vector3 direction = lookPoint - transform.position;

        if (direction.sqrMagnitude <= 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            Time.deltaTime * rotationSpeed
        );
    }

    /// <summary>
    /// 카메라 따라가기 중지.
    /// </summary>
    public void ClearTarget()
    {
        target = null;
    }
}