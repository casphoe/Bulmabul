using System;
using UnityEngine;

/// <summary>
/// 부루마불 게임용 카메라.
/// 
/// 역할:
/// 1. 기본 상태에서는 로컬 플레이어 Pawn을 45도 사선 시점으로 따라간다.
/// 2. 전체 맵 보기 버튼을 누르면 90도 탑뷰로 보드 전체를 보여준다.
/// 3. 전체 맵 보기 중 마우스 휠로 확대/축소할 수 있다.
/// 4. 전체 맵 보기 중 방향키 또는 WASD로 맵을 이동해서 볼 수 있다.
/// 5. 전체 맵 보기 중 주사위를 굴리면 즉시 전체 맵 보기를 해제하고 Pawn 따라가기 모드로 돌아간다.
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
    [SerializeField] private float followOrthographicSize = 12f;

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

    [Header("Full Map Zoom / Pan")]
    [Tooltip("전체 맵 보기 상태에서 마우스 휠 확대/축소 사용")]
    [SerializeField] private bool enableFullMapMouseWheelZoom = true;

    [Tooltip("전체 맵 보기 상태에서 방향키 / WASD 이동 사용")]
    [SerializeField] private bool enableFullMapKeyboardMove = true;

    [Tooltip("전체 맵 보기에서 휠 확대/축소 속도")]
    [SerializeField] private float fullMapZoomSpeed = 35f;

    [Tooltip("전체 맵 보기에서 확대 가능한 최소 Orthographic Size. followOrthographicSize보다 작게 내려가지 않게 보정된다.")]
    [SerializeField] private float minFullMapZoomOrthographicSize = 12f;

    [Tooltip("전체 맵 보기에서 방향키 이동 속도")]
    [SerializeField] private float fullMapKeyboardMoveSpeed = 55f;

    [Tooltip("전체 맵 보기에서 확대된 상태일 때 보드 밖으로 살짝 이동 가능한 여백")]
    [SerializeField] private float fullMapPanPadding = 4f;

    [Tooltip("전체 맵에서 좌우로 추가 이동 가능한 여유 범위")]
    [SerializeField] private float fullMapExtraPanX = 35f;

    [Tooltip("전체 맵에서 위아래로 추가 이동 가능한 여유 범위")]
    [SerializeField] private float fullMapExtraPanZ = 20f;

    [Tooltip("전체 맵 상태에서 매 프레임 보드 크기를 다시 계산할지 여부")]
    [SerializeField] private bool recalculateFullMapSizeEveryFrame = false;

    [Tooltip("전체 맵 보기를 켤 때마다 줌/이동 상태를 기본 전체 보기 상태로 초기화")]
    [SerializeField] private bool resetFullMapViewStateOnEnter = true;

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

    private Vector3 _fullMapPanOffset = Vector3.zero;
    private float _fullMapCurrentOrthographicSize;
    private float _fullMapTargetOrthographicSize;
    private float _fullMapDefaultOrthographicSize;

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

        float defaultSize = CalculateFullMapOrthographicSize();

        _fullMapDefaultOrthographicSize = defaultSize;
        _fullMapCurrentOrthographicSize = defaultSize;
        _fullMapTargetOrthographicSize = defaultSize;
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
            HandleFullMapInput();
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

        float defaultSize = CalculateFullMapOrthographicSize();

        _fullMapDefaultOrthographicSize = defaultSize;

        if (_isFullMapView)
        {
            _fullMapCurrentOrthographicSize = Mathf.Clamp(
                _fullMapCurrentOrthographicSize,
                GetFullMapMinZoomSize(),
                _fullMapDefaultOrthographicSize
            );

            _fullMapTargetOrthographicSize = Mathf.Clamp(
                _fullMapTargetOrthographicSize,
                GetFullMapMinZoomSize(),
                _fullMapDefaultOrthographicSize
            );

            ClampFullMapPanOffset();
        }
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
            if (resetFullMapViewStateOnEnter)
                ResetFullMapViewState();

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
    /// 전체 맵 보기의 줌/이동 상태를 기본 전체 보기 상태로 초기화한다.
    /// </summary>
    public void ResetFullMapViewState()
    {
        _fullMapPanOffset = Vector3.zero;

        _fullMapDefaultOrthographicSize = CalculateFullMapOrthographicSize();
        _fullMapCurrentOrthographicSize = _fullMapDefaultOrthographicSize;
        _fullMapTargetOrthographicSize = _fullMapDefaultOrthographicSize;
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
    /// 마우스 휠 확대/축소와 방향키 이동 오프셋을 반영한다.
    /// </summary>
    private void FollowFullMapView()
    {
        Vector3 center = GetBoardCenterPosition();

        ClampFullMapPanOffset();

        Vector3 desiredPosition = GetFullMapCameraPosition(center + _fullMapPanOffset);
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
    /// 전체 맵 보기 상태에서 마우스 휠 줌 / 방향키 이동을 처리한다.
    /// 
    /// 수정 내용:
    /// - 기존에는 매 프레임 _fullMapDefaultOrthographicSize를 다시 계산했다.
    /// - 이 값이 계속 바뀌면 줌/이동 제한값이 흔들려서 카메라가 가운데로 되돌아가는 현상이 생길 수 있다.
    /// - 기본값은 전체맵 진입 시 또는 Board 변경 시에만 계산하고,
    ///   필요할 때만 recalculateFullMapSizeEveryFrame 옵션으로 매 프레임 계산한다.
    /// </summary>
    private void HandleFullMapInput()
    {
        if (_camera == null)
            return;

        if (!_camera.orthographic)
            return;

        if (recalculateFullMapSizeEveryFrame)
            _fullMapDefaultOrthographicSize = CalculateFullMapOrthographicSize();

        if (enableFullMapMouseWheelZoom)
            HandleFullMapMouseWheelZoom();

        if (enableFullMapKeyboardMove)
            HandleFullMapKeyboardMove();
    }

    /// <summary>
    /// 전체 맵 보기 중 마우스 휠 확대/축소.
    /// 휠 위: 확대
    /// 휠 아래: 축소
    /// 축소는 전체 맵 기본 상태까지만 가능하다.
    /// 확대는 minFullMapZoomOrthographicSize 또는 followOrthographicSize까지 가능하다.
    /// </summary>
    private void HandleFullMapMouseWheelZoom()
    {
        float wheel = Input.GetAxis("Mouse ScrollWheel");

        if (Mathf.Abs(wheel) <= 0.0001f)
            return;

        float minZoomSize = GetFullMapMinZoomSize();
        float maxZoomSize = _fullMapDefaultOrthographicSize;

        /*
         * wheel > 0 : 확대 = Orthographic Size 감소
         * wheel < 0 : 축소 = Orthographic Size 증가
         */
        _fullMapTargetOrthographicSize -= wheel * fullMapZoomSpeed;
        _fullMapTargetOrthographicSize = Mathf.Clamp(
            _fullMapTargetOrthographicSize,
            minZoomSize,
            maxZoomSize
        );

        ClampFullMapPanOffset();
    }

    /// <summary>
    /// 전체 맵 보기 중 방향키 또는 WASD로 카메라 이동.
    /// 탑뷰 기준이므로 X/Z 평면으로 이동한다.
    /// </summary>
    private void HandleFullMapKeyboardMove()
    {
        float horizontal = 0f;
        float vertical = 0f;

        if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A))
            horizontal -= 1f;

        if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D))
            horizontal += 1f;

        if (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W))
            vertical += 1f;

        if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S))
            vertical -= 1f;

        Vector3 input = new Vector3(horizontal, 0f, vertical);

        if (input.sqrMagnitude <= 0.0001f)
            return;

        input.Normalize();

        /*
         * 줌을 많이 당긴 상태에서는 같은 이동 속도가 너무 느리게 느껴질 수 있어서,
         * 현재 Orthographic Size 비율을 살짝 반영한다.
         */
        float zoomRatio = Mathf.Clamp01(_fullMapTargetOrthographicSize / Mathf.Max(1f, _fullMapDefaultOrthographicSize));
        float speedMultiplier = Mathf.Lerp(0.45f, 1f, zoomRatio);

        _fullMapPanOffset += input * fullMapKeyboardMoveSpeed * speedMultiplier * Time.deltaTime;

        ClampFullMapPanOffset();
    }

    /// <summary>
    /// 전체 맵 보기에서 카메라 이동 오프셋을 제한한다.
    /// 
    /// 수정 내용:
    /// - 기존에는 카메라 화면이 보드보다 넓으면 X/Z 이동값을 강제로 0으로 돌렸다.
    /// - 그래서 왼쪽/오른쪽 이동 중 갑자기 가운데로 되돌아가는 문제가 있었다.
    /// - 이제 화면이 보드보다 넓어도 fullMapExtraPanX / fullMapExtraPanZ 만큼 이동을 허용한다.
    /// - 오른쪽 이동이 부족한 문제도 추가 여유 범위로 해결한다.
    /// </summary>
    private void ClampFullMapPanOffset()
    {
        if (_camera == null)
            return;

        if (!_camera.orthographic)
            return;

        if (!TryGetBoardBounds(out Bounds bounds))
            return;

        Vector3 boardCenter = bounds.center;

        float size = Mathf.Clamp(
            _fullMapTargetOrthographicSize,
            GetFullMapMinZoomSize(),
            _fullMapDefaultOrthographicSize
        );

        float halfHeight = size;
        float halfWidth = size * _camera.aspect;

        float desiredCenterX = boardCenter.x + _fullMapPanOffset.x;
        float desiredCenterZ = boardCenter.z + _fullMapPanOffset.z;

        desiredCenterX = ClampFullMapCenterAxis(
            desiredCenterX,
            boardCenter.x,
            bounds.min.x,
            bounds.max.x,
            halfWidth,
            fullMapPanPadding + fullMapExtraPanX
        );

        desiredCenterZ = ClampFullMapCenterAxis(
            desiredCenterZ,
            boardCenter.z,
            bounds.min.z,
            bounds.max.z,
            halfHeight,
            fullMapPanPadding + fullMapExtraPanZ
        );

        _fullMapPanOffset.x = desiredCenterX - boardCenter.x;
        _fullMapPanOffset.y = 0f;
        _fullMapPanOffset.z = desiredCenterZ - boardCenter.z;
    }

    /// <summary>
    /// 전체맵 카메라의 한 축 이동 범위를 계산해서 제한한다.
    /// 
    /// 기존 방식은 화면이 보드보다 넓으면 무조건 중앙 고정했지만,
    /// 이 함수는 화면이 더 넓어도 extraPan 만큼 이동을 허용한다.
    /// </summary>
    private float ClampFullMapCenterAxis(
        float desiredCenter,
        float boardCenter,
        float contentMin,
        float contentMax,
        float cameraHalfSize,
        float extraPan
    )
    {
        extraPan = Mathf.Max(0f, extraPan);

        float minCenter = contentMin - extraPan + cameraHalfSize;
        float maxCenter = contentMax + extraPan - cameraHalfSize;

        /*
         * 카메라 화면이 보드보다 넓은 경우.
         * 기존에는 여기서 boardCenter로 강제 고정했기 때문에
         * 왼쪽/오른쪽 이동 중 가운데로 튕기는 현상이 발생했다.
         * 이제는 중앙 기준으로 extraPan 만큼 이동을 허용한다.
         */
        if (minCenter > maxCenter)
        {
            return Mathf.Clamp(
                desiredCenter,
                boardCenter - extraPan,
                boardCenter + extraPan
            );
        }

        return Mathf.Clamp(desiredCenter, minCenter, maxCenter);
    }

    /// <summary>
    /// 전체 맵 보기에서 확대 가능한 최소 Orthographic Size.
    /// followOrthographicSize보다 작아지지 않게 보정한다.
    /// </summary>
    private float GetFullMapMinZoomSize()
    {
        float minSize = Mathf.Max(1f, minFullMapZoomOrthographicSize);

        /*
         * 전체 맵 보기에서 너무 과하게 확대되는 것을 막기 위해
         * Pawn 따라가기 시점의 크기보다 작게 내려가지 않게 한다.
         */
        minSize = Mathf.Max(minSize, followOrthographicSize);

        return minSize;
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
    /// 
    /// 기존 문제:
    /// - 매 프레임 CalculateFullMapOrthographicSize()를 다시 호출해서
    ///   전체맵 최대 크기 값이 계속 흔들릴 수 있었다.
    /// - 그 결과 줌 상태나 이동 제한이 다시 계산되면서 가운데로 돌아가는 현상이 발생할 수 있었다.
    /// </summary>
    private void ApplyFullMapCameraSize(bool instant)
    {
        if (_camera == null)
            return;

        if (!_camera.orthographic)
            return;

        if (recalculateFullMapSizeEveryFrame)
            _fullMapDefaultOrthographicSize = CalculateFullMapOrthographicSize();

        _fullMapTargetOrthographicSize = Mathf.Clamp(
            _fullMapTargetOrthographicSize,
            GetFullMapMinZoomSize(),
            _fullMapDefaultOrthographicSize
        );

        if (instant)
        {
            _fullMapCurrentOrthographicSize = _fullMapTargetOrthographicSize;
            _camera.orthographicSize = _fullMapCurrentOrthographicSize;
            return;
        }

        _fullMapCurrentOrthographicSize = Mathf.Lerp(
            _fullMapCurrentOrthographicSize,
            _fullMapTargetOrthographicSize,
            Time.deltaTime * fullMapMoveSpeed
        );

        _camera.orthographicSize = _fullMapCurrentOrthographicSize;
    }

    /// <summary>
    /// 전체 맵 보기 기본 Orthographic Size 계산.
    /// 이 값이 전체 맵 상태에서 가장 넓게 보이는 최대 줌아웃 값이다.
    /// </summary>
    private float CalculateFullMapOrthographicSize()
    {
        float targetSize = minFullMapOrthographicSize;

        if (autoFullMapOrthographicSize && TryGetBoardBounds(out Bounds bounds))
        {
            float sizeX = bounds.size.x * 0.5f;
            float sizeZ = bounds.size.z * 0.5f;

            targetSize = Mathf.Max(sizeX, sizeZ) + fullMapOrthographicPadding;
            targetSize = Mathf.Max(targetSize, minFullMapOrthographicSize);
        }

        return Mathf.Max(1f, targetSize);
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