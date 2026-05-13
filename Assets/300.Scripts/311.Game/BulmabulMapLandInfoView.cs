using TMPro;
using UnityEngine;

/// <summary>
/// 전체 맵 보기 상태에서 땅 위에 표시되는 정보판.
/// 
/// 역할:
/// - 소유자
/// - 구매 가격
/// - 통행료
/// - 건물 상태
/// 를 3D TextMeshPro로 자동 표시한다.
/// 
/// 중요:
/// - 프리팹에 TextMeshPro를 직접 만들 필요 없음.
/// - 이 스크립트가 MapInfoRoot, Background, TxtMapInfo를 자동 생성함.
/// - 전체 맵 보기일 때만 켜고, 평소에는 꺼두는 용도.
/// </summary>
public class BulmabulMapLandInfoView : MonoBehaviour
{
    public enum MapInfoSide
    {
        AutoByIndex,
        BottomSide,
        LeftSide,
        TopSide,
        RightSide
    }

    [Header("Runtime Data")]
    [SerializeField, HideInInspector] private int cellIndex = -1;
    [SerializeField, HideInInspector] private BulmabulCellData cellData;

    [Header("Auto Create")]
    [SerializeField] private bool autoCreate = true;
    [SerializeField] private string rootName = "MapInfoRoot";
    [SerializeField] private string backgroundName = "MapInfoBackground";
    [SerializeField] private string textName = "TxtMapInfo";

    [Header("References")]
    [SerializeField] private Transform root;
    [SerializeField] private SpriteRenderer background;
    [SerializeField] private TextMeshPro txtInfo;

    [Header("Position")]
    [Tooltip("전체 맵 정보판이 발판 위에 떠 있는 높이")]
    [SerializeField] private float height = 0.82f;

    [Tooltip("셀 중심에서 정보판을 어느 방향으로 밀어낼지")]
    [SerializeField] private float distanceFromCenter = 0.15f;

    [Tooltip("추가 X 보정")]
    [SerializeField] private float extraXOffset = 0f;

    [Tooltip("추가 Z 보정")]
    [SerializeField] private float extraZOffset = 0f;

    [Tooltip("텍스트가 바닥에 눕는 형태로 표시된다")]
    [SerializeField] private bool horizontalOnBoard = true;

    [Header("Layout")]
    [Tooltip("자동 방향 계산. 160칸 / 40칸씩 4면 기준")]
    [SerializeField] private MapInfoSide infoSide = MapInfoSide.AutoByIndex;

    [Tooltip("한 변의 칸 수. 총 160칸이면 40")]
    [SerializeField] private int cellsPerSide = 40;

    [Header("Text Style")]
    [SerializeField] private TMP_FontAsset defaultFontAsset;

    [Tooltip("전체 맵에서 멀리서 보여야 하므로 기존 이름 텍스트보다 작게")]
    [SerializeField] private float fontSize = 10f;

    [Tooltip("TextMeshPro 오브젝트 스케일")]
    [SerializeField] private float textScale = 0.075f;

    [Tooltip("텍스트 박스 가로 크기")]
    [SerializeField] private float textWidth = 8.5f;

    [Tooltip("텍스트 박스 세로 크기")]
    [SerializeField] private float textHeight = 4.5f;

    [Header("Background")]
    [SerializeField] private bool useBackground = true;

    [Tooltip("정보판 배경 크기")]
    [SerializeField] private Vector2 backgroundSize = new Vector2(0.85f, 0.48f);

    [SerializeField] private Color noOwnerBackgroundColor = new Color(1f, 1f, 1f, 0.78f);

    [SerializeField] private Color[] ownerBackgroundColors =
    {
        new Color(1f, 0.25f, 0.25f, 0.82f),
        new Color(0.25f, 0.45f, 1f, 0.82f),
        new Color(0.25f, 1f, 0.35f, 0.82f),
        new Color(1f, 0.82f, 0.2f, 0.82f)
    };

    [Header("Text Colors")]
    [SerializeField] private Color noOwnerTextColor = Color.black;
    [SerializeField] private Color ownerTextColor = Color.white;

    [Header("Display")]
    [Tooltip("구매되지 않은 땅도 전체 맵 보기에서 표시")]
    [SerializeField] private bool showNoOwnerLand = true;

    [Tooltip("구매된 땅만 자세히 표시하고, 빈 땅은 가격만 표시")]
    [SerializeField] private bool compactNoOwnerText = true;

    public int CellIndex => cellIndex;
    public BulmabulCellData CellData => cellData;

    private static Sprite _whiteSprite;

    /// <summary>
    /// 외부에서 셀 정보 연결.
    /// Controller 또는 BoardCellView에서 호출한다.
    /// </summary>
    public void Setup(int index, BulmabulCellData data)
    {
        cellIndex = index;
        cellData = data;

        EnsureCreated();
        RefreshLayout();
        SetVisible(false);
    }

    private void Awake()
    {
        if (!Application.isPlaying)
            return;

        EnsureCreated();
        RefreshLayout();
        SetVisible(false);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {

        // Prefab Asset 상태에서는 자식 오브젝트 생성/부모 변경을 하면 Unity가 막는다.
        // 그래서 에디터 검증 중에는 런타임 자동 생성을 하지 않는다.
        if (!Application.isPlaying)
            return;

        EnsureCreated();
        RefreshLayout();
        ApplyStyle();
    }
#endif

    /// <summary>
    /// 전체 맵 보기 상태에 따라 표시/숨김.
    /// 땅 칸이 아니면 무조건 숨김.
    /// </summary>
    public void SetVisible(bool visible)
    {
        EnsureCreated();

        bool finalVisible = visible;

        if (cellData == null)
            finalVisible = false;

        if (cellData != null && cellData.cellType != BulmabulCellType.Land)
            finalVisible = false;

        if (root != null)
            root.gameObject.SetActive(finalVisible);
    }

    /// <summary>
    /// 현재 네트워크 상태 기준으로 정보판 내용 갱신.
    /// </summary>
    public void Refresh(BulmabulGameState state)
    {
        EnsureCreated();
        RefreshLayout();
        ApplyStyle();

        if (txtInfo == null)
            return;

        if (cellData == null)
        {
            txtInfo.text = "";
            SetVisible(false);
            return;
        }

        if (cellData.cellType != BulmabulCellType.Land)
        {
            txtInfo.text = "";
            SetVisible(false);
            return;
        }

        if (state == null || !state.IsSpawnReady)
        {
            txtInfo.text = GetWaitingText();
            ApplyBackgroundColor(-1);
            return;
        }

        int ownerIndex = state.LandOwnerByCell.Get(cellIndex);
        int buildFlags = state.LandBuildingFlagsByCell.Get(cellIndex);

        if (ownerIndex < 0 && !showNoOwnerLand)
        {
            SetVisible(false);
            return;
        }

        txtInfo.text = BuildInfoText(state, ownerIndex, buildFlags);
        txtInfo.color = ownerIndex >= 0 ? ownerTextColor : noOwnerTextColor;

        ApplyBackgroundColor(ownerIndex);
    }

    /// <summary>
    /// 루트 / 배경 / 텍스트 자동 생성.
    /// </summary>
    private void EnsureCreated()
    {
        if (!autoCreate)
            return;

        // 에디터에서 Prefab Asset 자체를 검사하는 중에는 자식 생성/부모 변경 금지.
        if (!Application.isPlaying)
            return;

        EnsureRoot();
        EnsureBackground();
        EnsureText();
        ApplyStyle();
    }

    private void EnsureRoot()
    {
        if (root != null)
        {
            root.SetParent(transform, false);
            return;
        }

        Transform existing = transform.Find(rootName);

        if (existing != null)
        {
            root = existing;
        }
        else
        {
            GameObject rootObj = new GameObject(rootName);
            root = rootObj.transform;
            root.SetParent(transform, false);
        }

        root.localScale = Vector3.one;
    }

    private void EnsureBackground()
    {
        if (!useBackground)
            return;

        EnsureRoot();

        if (background != null)
        {
            background.transform.SetParent(root, false);
            return;
        }

        Transform existing = root.Find(backgroundName);

        GameObject go;

        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            go = new GameObject(backgroundName);
            go.transform.SetParent(root, false);
        }

        background = go.GetComponent<SpriteRenderer>();

        if (background == null)
            background = go.AddComponent<SpriteRenderer>();

        background.sprite = GetWhiteSprite();
        background.sortingOrder = 50;

        go.transform.localPosition = new Vector3(0f, -0.005f, 0f);
        go.transform.localScale = new Vector3(backgroundSize.x, backgroundSize.y, 1f);

        if (horizontalOnBoard)
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        else
            go.transform.localRotation = Quaternion.identity;
    }

    private void EnsureText()
    {
        EnsureRoot();

        if (txtInfo != null)
        {
            txtInfo.transform.SetParent(root, false);
            return;
        }

        Transform existing = root.Find(textName);

        GameObject go;

        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            go = new GameObject(textName);
            go.transform.SetParent(root, false);
        }

        TextMeshPro tmp = go.GetComponent<TextMeshPro>();

        if (tmp == null)
            tmp = go.AddComponent<TextMeshPro>();

        txtInfo = tmp;
    }

    /// <summary>
    /// 셀 위치 기준으로 정보판 위치 자동 배치.
    /// 160칸 / 40칸씩 4면 기준.
    /// </summary>
    private void RefreshLayout()
    {
        EnsureRoot();

        if (root == null)
            return;

        MapInfoSide side = ResolveSide();

        Vector3 pos;

        switch (side)
        {
            case MapInfoSide.BottomSide:
                pos = new Vector3(
                    extraXOffset,
                    height,
                    distanceFromCenter + extraZOffset
                );
                break;

            case MapInfoSide.LeftSide:
                pos = new Vector3(
                    distanceFromCenter + extraXOffset,
                    height,
                    extraZOffset
                );
                break;

            case MapInfoSide.TopSide:
                pos = new Vector3(
                    extraXOffset,
                    height,
                    -distanceFromCenter + extraZOffset
                );
                break;

            case MapInfoSide.RightSide:
                pos = new Vector3(
                    -distanceFromCenter + extraXOffset,
                    height,
                    extraZOffset
                );
                break;

            default:
                pos = new Vector3(extraXOffset, height, extraZOffset);
                break;
        }

        root.localPosition = pos;
        root.localScale = Vector3.one;
        root.localRotation = Quaternion.identity;

        ApplyChildRotation();
    }

    private MapInfoSide ResolveSide()
    {
        if (infoSide != MapInfoSide.AutoByIndex)
            return infoSide;

        int perSide = Mathf.Max(1, cellsPerSide);

        if (cellIndex < perSide)
            return MapInfoSide.BottomSide;

        if (cellIndex < perSide * 2)
            return MapInfoSide.LeftSide;

        if (cellIndex < perSide * 3)
            return MapInfoSide.TopSide;

        return MapInfoSide.RightSide;
    }

    private void ApplyChildRotation()
    {
        Quaternion rot = horizontalOnBoard
            ? Quaternion.Euler(90f, 0f, 0f)
            : Quaternion.identity;

        if (txtInfo != null)
            txtInfo.transform.localRotation = rot;

        if (background != null)
            background.transform.localRotation = rot;
    }

    private void ApplyStyle()
    {
        if (txtInfo != null)
        {
            if (defaultFontAsset != null)
                txtInfo.font = defaultFontAsset;

            txtInfo.fontSize = fontSize;
            txtInfo.alignment = TextAlignmentOptions.Center;
            txtInfo.enableWordWrapping = false;
            txtInfo.overflowMode = TextOverflowModes.Overflow;
            txtInfo.richText = false;
            txtInfo.sortingOrder = 60;

            RectTransform rect = txtInfo.rectTransform;

            if (rect != null)
                rect.sizeDelta = new Vector2(textWidth, textHeight);

            txtInfo.transform.localPosition = Vector3.zero;
            txtInfo.transform.localScale = Vector3.one * textScale;
        }

        if (background != null)
        {
            background.gameObject.SetActive(useBackground);
            background.transform.localPosition = new Vector3(0f, -0.005f, 0f);
            background.transform.localScale = new Vector3(backgroundSize.x, backgroundSize.y, 1f);
        }

        ApplyChildRotation();
    }

    private string BuildInfoText(BulmabulGameState state, int ownerIndex, int buildFlags)
    {
        bool eng = IsEnglish();

        string landName = string.IsNullOrWhiteSpace(cellData.cellName)
            ? (eng ? "Land" : "땅")
            : cellData.cellName;

        int buyCost = Mathf.Max(0, cellData.buyCost);
        int toll = BulmabulLandSystem.CalculateToll(cellData, buildFlags);

        if (ownerIndex < 0)
        {
            if (compactNoOwnerText)
            {
                return eng
                    ? $"{landName}\nPrice {buyCost:N0}\nNo Owner"
                    : $"{landName}\n가격 {buyCost:N0}\n소유 없음";
            }

            return eng
                ? $"{landName}\nPrice {buyCost:N0}\nOwner None"
                : $"{landName}\n가격 {buyCost:N0}\n소유 없음";
        }

        string ownerName = GetOwnerName(state, ownerIndex);
        string buildText = GetBuildText(buildFlags, eng);

        return eng
            ? $"{landName}\nOwner {ownerName}\nToll {toll:N0}\n{buildText}"
            : $"{landName}\n소유 {ownerName}\n통행료 {toll:N0}\n{buildText}";
    }

    private string GetWaitingText()
    {
        bool eng = IsEnglish();

        string landName = cellData != null && !string.IsNullOrWhiteSpace(cellData.cellName)
            ? cellData.cellName
            : eng ? "Land" : "땅";

        return eng
            ? $"{landName}\nLoading..."
            : $"{landName}\n불러오는 중";
    }

    private string GetOwnerName(BulmabulGameState state, int ownerIndex)
    {
        if (state == null)
            return "None";

        if (ownerIndex < 0 || ownerIndex >= BulmabulGameState.MaxPlayers)
            return IsEnglish() ? "Unknown" : "알 수 없음";

        var player = state.Players.Get(ownerIndex);
        string nickname = player.nickname.ToString();

        if (!string.IsNullOrWhiteSpace(nickname))
            return nickname;

        return IsEnglish()
            ? $"P{ownerIndex + 1}"
            : $"P{ownerIndex + 1}";
    }

    private string GetBuildText(int flags, bool eng)
    {
        if (flags == BulmabulBuildFlags.None)
            return eng ? "No Building" : "건물 없음";

        string result = "";

        if (BulmabulBuildFlags.Has(flags, BulmabulBuildFlags.SmallHouse))
            result += eng ? "Small " : "작은집 ";

        if (BulmabulBuildFlags.Has(flags, BulmabulBuildFlags.House))
            result += eng ? "House " : "집 ";

        if (BulmabulBuildFlags.Has(flags, BulmabulBuildFlags.BigHouse))
            result += eng ? "Big " : "큰집 ";

        if (BulmabulBuildFlags.Has(flags, BulmabulBuildFlags.Hotel))
            result += eng ? "Hotel " : "호텔 ";

        return string.IsNullOrWhiteSpace(result)
            ? eng ? "No Building" : "건물 없음"
            : result.Trim();
    }

    private void ApplyBackgroundColor(int ownerIndex)
    {
        if (background == null)
            return;

        if (ownerIndex < 0)
        {
            background.color = noOwnerBackgroundColor;
            return;
        }

        if (ownerBackgroundColors != null &&
            ownerIndex >= 0 &&
            ownerIndex < ownerBackgroundColors.Length)
        {
            background.color = ownerBackgroundColors[ownerIndex];
            return;
        }

        background.color = noOwnerBackgroundColor;
    }

    private bool IsEnglish()
    {
        var lang = (LaguageManager.Instance != null)
            ? LaguageManager.Instance.currentLang
            : Lauaguage.Kor;

        return lang == Lauaguage.Eng;
    }

    private static Sprite GetWhiteSprite()
    {
        if (_whiteSprite != null)
            return _whiteSprite;

        Texture2D tex = new Texture2D(1, 1);
        tex.name = "RuntimeWhiteSpriteTexture";
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();

        _whiteSprite = Sprite.Create(
            tex,
            new Rect(0, 0, 1, 1),
            new Vector2(0.5f, 0.5f),
            1f
        );

        _whiteSprite.name = "RuntimeWhiteSprite";

        return _whiteSprite;
    }
}