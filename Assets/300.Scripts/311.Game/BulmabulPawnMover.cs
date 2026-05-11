using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// 말 이동 연출 담당.
/// 네트워크 값은 직접 수정하지 않는다.
/// 
/// 역할:
/// - 플레이어 말 프리팹 생성
/// - 보드 칸 위치를 따라 말 이동
/// - 모든 말 시작 지점에 배치
/// - 말 위에 3D TextMeshPro로 플레이어 번호 표시
/// 
/// 주의:
/// - Pawn 3D 오브젝트 자체에 색이 들어가 있으므로 코드에서 색상을 변경하지 않는다.
/// - 플레이어 이름 텍스트는 현재 언어에 따라 한국어/영어로 표시한다.
/// </summary>
public class BulmabulPawnMover : MonoBehaviour
{
    [Header("Board")]
    [SerializeField] private BulmabulBoard board;

    [Header("Pawn Root")]
    [Tooltip("생성된 말들이 들어갈 부모 Transform. 비워두면 이 오브젝트 아래에 생성됨")]
    [SerializeField] private Transform pawnRoot;

    [Header("Pawn Prefabs")]
    [Tooltip("공통 말 프리팹. 개별 프리팹이 비어있으면 이 프리팹을 사용")]
    [SerializeField] private GameObject defaultPawnPrefab;

    [Tooltip("0~3번 플레이어별 말 프리팹. 비워두면 Default Pawn Prefab 사용")]
    [SerializeField] private GameObject[] pawnPrefabs = new GameObject[4];

    [Header("Pawn Visuals")]
    [Tooltip("실제 보드 위에서 움직이는 말 Transform. 비워두면 프리팹으로 자동 생성됨")]
    [SerializeField] private Transform[] pawnVisuals = new Transform[4];

    [Header("Pawn Name Text")]
    [Tooltip("말 위에 플레이어 번호 텍스트를 표시할지 여부")]
    [SerializeField] private bool usePlayerNameText = true;

    [Tooltip("말 위에 생성되는 3D TextMeshPro에 사용할 폰트")]
    [SerializeField] private TMP_FontAsset pawnNameFont;

    [Tooltip("말 위에 표시될 텍스트의 Y 오프셋")]
    [SerializeField] private float nameTextYOffset = 1.4f;

    [Tooltip("텍스트 크기")]
    [SerializeField] private float nameTextSize = 3f;

    [Tooltip("텍스트 영역 가로 크기")]
    [SerializeField] private float nameTextWidth = 6f;

    [Tooltip("텍스트 영역 세로 크기")]
    [SerializeField] private float nameTextHeight = 1.2f;

    [Tooltip("텍스트가 카메라를 바라보게 할지 여부")]
    [SerializeField] private bool nameTextLookAtCamera = true;

    [Tooltip("한국어 플레이어 이름")]
    [SerializeField]
    private string[] playerNameTextsKor =
    {
        "플레이어 1",
        "플레이어 2",
        "플레이어 3",
        "플레이어 4"
    };

    [Tooltip("영어 플레이어 이름")]
    [SerializeField]
    private string[] playerNameTextsEng =
    {
        "Player 1",
        "Player 2",
        "Player 3",
        "Player 4"
    };

    [Header("Animation")]
    [SerializeField] private float moveStepSeconds = 0.22f;
    [SerializeField] private float directMoveSeconds = 0.8f;

    [Header("3D Offset")]
    [Tooltip("말이 발판 위에 살짝 떠 있도록 올리는 Y 오프셋")]
    [SerializeField] private float pawnHeightOffset = 0.6f;

    [Tooltip("같은 칸에 여러 말이 있을 때 겹치지 않게 벌리는 간격")]
    [SerializeField] private float pawnSideOffset = 0.55f;

    private TextMeshPro[] pawnNameTexts = new TextMeshPro[4];

    public float MoveStepSeconds => moveStepSeconds;
    public float DirectMoveSeconds => directMoveSeconds;

    public void SetBoard(BulmabulBoard targetBoard)
    {
        board = targetBoard;
    }

    /// <summary>
    /// 게임 시작 전에 말 프리팹을 생성한다.
    /// 이미 연결된 말이 있으면 새로 만들지 않는다.
    /// </summary>
    public void EnsurePawnsCreated()
    {
        if (pawnRoot == null)
            pawnRoot = transform;

        if (pawnVisuals == null || pawnVisuals.Length != 4)
            pawnVisuals = new Transform[4];

        if (pawnNameTexts == null || pawnNameTexts.Length != 4)
            pawnNameTexts = new TextMeshPro[4];

        for (int i = 0; i < 4; i++)
        {
            if (pawnVisuals[i] != null)
            {
                EnsureNameTextCreated(i, pawnVisuals[i]);
                RefreshNameText(i);
                continue;
            }

            GameObject prefab = GetPawnPrefab(i);

            if (prefab == null)
            {
                Debug.LogWarning($"[BulmabulPawnMover] {i + 1}번 플레이어 말 프리팹이 없습니다.");
                continue;
            }

            GameObject pawnObj = Instantiate(prefab, pawnRoot);
            pawnObj.name = $"Pawn_Player_{i + 1}";

            pawnVisuals[i] = pawnObj.transform;

            EnsureNameTextCreated(i, pawnVisuals[i]);
            RefreshNameText(i);
        }
    }

    /// <summary>
    /// 모든 말을 시작 지점 Cell 0에 배치한다.
    /// 실제 참가하지 않은 플레이어 말은 비활성화 여부를 PlacePawnImmediate에서 처리한다.
    /// </summary>
    public void PlaceAllPawnsAtStart()
    {
        EnsurePawnsCreated();

        for (int i = 0; i < pawnVisuals.Length; i++)
        {
            if (pawnVisuals[i] == null)
                continue;

            pawnVisuals[i].position = GetPawnWorldPosition(0, i);
            UpdateNameTextPosition(i);
            RefreshNameText(i);
        }
    }

    /// <summary>
    /// 카메라가 현재 턴 플레이어 말을 따라갈 때 사용.
    /// </summary>
    public Transform GetPawnTransform(int playerIndex)
    {
        if (!IsValidPawn(playerIndex))
            return null;

        return pawnVisuals[playerIndex];
    }

    /// <summary>
    /// 특정 플레이어 말을 즉시 특정 칸에 배치한다.
    /// </summary>
    public void PlacePawnImmediate(int playerIndex, int tileIndex, bool active)
    {
        EnsurePawnsCreated();

        if (!IsValidPawn(playerIndex))
            return;

        pawnVisuals[playerIndex].gameObject.SetActive(active);
        SetNameTextActive(playerIndex, active);

        if (!active)
            return;

        if (board == null || board.CellCount <= 0)
            return;

        tileIndex = board.ClampCellIndex(tileIndex);

        pawnVisuals[playerIndex].position = GetPawnWorldPosition(tileIndex, playerIndex);
        UpdateNameTextPosition(playerIndex);
        RefreshNameText(playerIndex);
    }

    public void PlayStepMove(int playerIndex, int fromIndex, int moveCount)
    {
        if (!gameObject.activeInHierarchy)
            return;

        StartCoroutine(CoStepMove(playerIndex, fromIndex, moveCount));
    }

    public void PlayDirectMove(int playerIndex, int fromIndex, int targetIndex)
    {
        if (!gameObject.activeInHierarchy)
            return;

        StartCoroutine(CoDirectMove(playerIndex, fromIndex, targetIndex));
    }

    private IEnumerator CoStepMove(int playerIndex, int fromIndex, int moveCount)
    {
        EnsurePawnsCreated();

        if (!IsValidPawn(playerIndex))
            yield break;

        if (board == null || board.CellCount <= 0)
            yield break;

        pawnVisuals[playerIndex].gameObject.SetActive(true);
        SetNameTextActive(playerIndex, true);
        RefreshNameText(playerIndex);

        int boardCount = board.CellCount;
        int current = board.ClampCellIndex(fromIndex);

        for (int i = 0; i < moveCount; i++)
        {
            int next = current + 1;

            if (next >= boardCount)
                next = 0;

            Vector3 startPos = GetPawnWorldPosition(current, playerIndex);
            Vector3 endPos = GetPawnWorldPosition(next, playerIndex);

            float t = 0f;

            while (t < moveStepSeconds)
            {
                t += Time.deltaTime;
                float lerp = Mathf.Clamp01(t / moveStepSeconds);

                pawnVisuals[playerIndex].position = Vector3.Lerp(startPos, endPos, lerp);
                UpdateNameTextPosition(playerIndex);

                yield return null;
            }

            pawnVisuals[playerIndex].position = endPos;
            UpdateNameTextPosition(playerIndex);

            current = next;
        }
    }

    private IEnumerator CoDirectMove(int playerIndex, int fromIndex, int targetIndex)
    {
        EnsurePawnsCreated();

        if (!IsValidPawn(playerIndex))
            yield break;

        if (board == null || board.CellCount <= 0)
            yield break;

        pawnVisuals[playerIndex].gameObject.SetActive(true);
        SetNameTextActive(playerIndex, true);
        RefreshNameText(playerIndex);

        fromIndex = board.ClampCellIndex(fromIndex);
        targetIndex = board.ClampCellIndex(targetIndex);

        Vector3 startPos = GetPawnWorldPosition(fromIndex, playerIndex);
        Vector3 endPos = GetPawnWorldPosition(targetIndex, playerIndex);

        float t = 0f;

        while (t < directMoveSeconds)
        {
            t += Time.deltaTime;
            float lerp = Mathf.Clamp01(t / directMoveSeconds);

            pawnVisuals[playerIndex].position = Vector3.Lerp(startPos, endPos, lerp);
            UpdateNameTextPosition(playerIndex);

            yield return null;
        }

        pawnVisuals[playerIndex].position = endPos;
        UpdateNameTextPosition(playerIndex);
    }

    private Vector3 GetPawnWorldPosition(int tileIndex, int playerIndex)
    {
        if (board == null)
            return Vector3.zero;

        Vector3 cellPos = board.GetCellPosition(tileIndex);
        Vector3 offset = GetPawnOffset(playerIndex);

        return cellPos + offset;
    }

    private GameObject GetPawnPrefab(int playerIndex)
    {
        if (pawnPrefabs != null &&
            playerIndex >= 0 &&
            playerIndex < pawnPrefabs.Length &&
            pawnPrefabs[playerIndex] != null)
        {
            return pawnPrefabs[playerIndex];
        }

        return defaultPawnPrefab;
    }

    private bool IsValidPawn(int playerIndex)
    {
        if (pawnVisuals == null)
            return false;

        if (playerIndex < 0 || playerIndex >= pawnVisuals.Length)
            return false;

        return pawnVisuals[playerIndex] != null;
    }

    private Vector3 GetPawnOffset(int playerIndex)
    {
        float s = pawnSideOffset;
        float y = pawnHeightOffset;

        switch (playerIndex)
        {
            case 0:
                return new Vector3(-s, y, -s);

            case 1:
                return new Vector3(s, y, -s);

            case 2:
                return new Vector3(-s, y, s);

            case 3:
                return new Vector3(s, y, s);
        }

        return new Vector3(0f, y, 0f);
    }

    /// <summary>
    /// 말 위에 3D TextMeshPro 텍스트를 생성한다.
    /// </summary>
    private void EnsureNameTextCreated(int playerIndex, Transform pawn)
    {
        if (!usePlayerNameText)
            return;

        if (pawn == null)
            return;

        if (pawnNameTexts == null || pawnNameTexts.Length != 4)
            pawnNameTexts = new TextMeshPro[4];

        if (pawnNameTexts[playerIndex] != null)
            return;

        GameObject textObj = new GameObject($"Pawn_Name_Text_{playerIndex + 1}");

        // Pawn 자식으로 넣지 않고 PawnRoot 아래에 둔다.
        // 이유: Pawn 모델이 회전하거나 스케일이 바뀌어도 텍스트가 영향을 덜 받게 하기 위해서.
        textObj.transform.SetParent(pawnRoot != null ? pawnRoot : transform);

        TextMeshPro tmp = textObj.AddComponent<TextMeshPro>();

        if (pawnNameFont != null)
            tmp.font = pawnNameFont;

        tmp.text = GetPlayerNameText(playerIndex);
        tmp.fontSize = nameTextSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;

        // 3D TextMeshPro 영역 크기
        tmp.rectTransform.sizeDelta = new Vector2(nameTextWidth, nameTextHeight);

        pawnNameTexts[playerIndex] = tmp;

        UpdateNameTextPosition(playerIndex);
    }

    /// <summary>
    /// 현재 언어에 따라 플레이어 이름 텍스트를 반환한다.
    /// </summary>
    private string GetPlayerNameText(int playerIndex)
    {
        string kor = $"플레이어 {playerIndex + 1}";
        string eng = $"Player {playerIndex + 1}";

        if (playerNameTextsKor != null &&
            playerIndex >= 0 &&
            playerIndex < playerNameTextsKor.Length &&
            !string.IsNullOrEmpty(playerNameTextsKor[playerIndex]))
        {
            kor = playerNameTextsKor[playerIndex];
        }

        if (playerNameTextsEng != null &&
            playerIndex >= 0 &&
            playerIndex < playerNameTextsEng.Length &&
            !string.IsNullOrEmpty(playerNameTextsEng[playerIndex]))
        {
            eng = playerNameTextsEng[playerIndex];
        }

        return GetByLanguage(kor, eng);
    }

    /// <summary>
    /// 현재 언어에 맞게 플레이어 이름 텍스트를 갱신한다.
    /// </summary>
    private void RefreshNameText(int playerIndex)
    {
        if (!usePlayerNameText)
            return;

        if (pawnNameTexts == null)
            return;

        if (playerIndex < 0 || playerIndex >= pawnNameTexts.Length)
            return;

        TextMeshPro tmp = pawnNameTexts[playerIndex];

        if (tmp == null)
            return;

        if (pawnNameFont != null && tmp.font != pawnNameFont)
            tmp.font = pawnNameFont;

        tmp.text = GetPlayerNameText(playerIndex);
        tmp.fontSize = nameTextSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.rectTransform.sizeDelta = new Vector2(nameTextWidth, nameTextHeight);
    }

    private void UpdateNameTextPosition(int playerIndex)
    {
        if (!usePlayerNameText)
            return;

        if (!IsValidPawn(playerIndex))
            return;

        if (pawnNameTexts == null ||
            playerIndex < 0 ||
            playerIndex >= pawnNameTexts.Length ||
            pawnNameTexts[playerIndex] == null)
        {
            return;
        }

        Transform pawn = pawnVisuals[playerIndex];
        Transform textTransform = pawnNameTexts[playerIndex].transform;

        textTransform.position = pawn.position + new Vector3(0f, nameTextYOffset, 0f);

        if (nameTextLookAtCamera && Camera.main != null)
        {
            // 카메라와 같은 방향을 바라보게 해서 화면에서 항상 잘 보이게 한다.
            textTransform.forward = Camera.main.transform.forward;
        }
    }

    private void SetNameTextActive(int playerIndex, bool active)
    {
        if (pawnNameTexts == null)
            return;

        if (playerIndex < 0 || playerIndex >= pawnNameTexts.Length)
            return;

        if (pawnNameTexts[playerIndex] == null)
            return;

        pawnNameTexts[playerIndex].gameObject.SetActive(active);
    }

    /// <summary>
    /// 현재 언어에 따라 한국어/영어 문구를 반환한다.
    /// LaguageManager가 없으면 한국어를 기본값으로 사용한다.
    /// </summary>
    private string GetByLanguage(string kor, string eng)
    {
        if (LaguageManager.Instance == null)
            return kor;

        return LaguageManager.Instance.currentLang == Lauaguage.Eng ? eng : kor;
    }

    private void LateUpdate()
    {
        if (!usePlayerNameText)
            return;

        if (pawnVisuals == null)
            return;

        for (int i = 0; i < pawnVisuals.Length; i++)
        {
            RefreshNameText(i);
            UpdateNameTextPosition(i);
        }
    }
}