using TMPro;
using UnityEngine;

/// <summary>
/// 보드 칸 하나의 표시용 컴포넌트.
/// 
/// 역할:
/// - Generator가 넘겨준 칸 데이터 저장
/// - 발판 위에 3D TextMeshPro 표시
/// - 칸 이름 / 가격 자동 표시
/// - TextMeshPro 폰트 Asset 지정
/// - LabelRoot 기준으로 이름/가격 위치 자동 배치
/// 
/// 사용 방식:
/// - LabelRoot는 셀의 자식으로 localPosition 0,0,0에 생성된다.
/// - TxtName3D / TxtPrice3D는 LabelRoot의 자식으로 생성된다.
/// - 타입 텍스트는 사용하지 않는다.
/// - 발판 중앙은 건물 배치 공간으로 비워둔다.
/// </summary>
public class BulmabulBoardCellView : MonoBehaviour
{
    public enum CellLabelSide
    {
        AutoByIndex,
        BottomSide,
        LeftSide,
        TopSide,
        RightSide
    }

    [Header("Runtime Data")]
    [SerializeField, HideInInspector] private int cellIndex;
    [SerializeField, HideInInspector] private BulmabulCellData cellData;

    [Header("3D Text Auto Create")]
    [SerializeField] private bool autoCreate3DTexts = true;

    [Tooltip("체크하면 텍스트가 바닥에 눕는 형태로 표시된다.")]
    [SerializeField] private bool useHorizontalTextOnBoard = true;

    [Header("Label Root")]
    [Tooltip("이름/가격 텍스트를 묶는 루트. 비워두면 자동 생성된다.")]
    [SerializeField] private Transform labelRoot;

    [Tooltip("LabelRoot 이름")]
    [SerializeField] private string labelRootName = "LabelRoot";

    [Header("3D Text Layout")]
    [Tooltip("텍스트가 발판 위에 떠 있는 높이")]
    [SerializeField] private float textHeight = 0.65f;

    [Tooltip("셀 중심에서 이름/가격을 어느 정도 바깥 또는 안쪽으로 뺄지")]
    private float labelDistance = 2.45f;

    [Tooltip("이름과 가격 사이 간격")]
    [SerializeField] private float lineGap = 0.6f;

    [Tooltip("텍스트 오브젝트 스케일")]
    [SerializeField] private float textScale = 0.08f;

    [Tooltip("자동 방향 계산. 160칸, 40/40/40/40 구조 기준")]
    [SerializeField] private CellLabelSide labelSide = CellLabelSide.AutoByIndex;

    [Tooltip("한 변에 있는 칸 수. 총 160칸이면 40")]
    [SerializeField] private int cellsPerSide = 40;

    [Header("Fine Tune")]
    [Tooltip("전체 텍스트 X 위치 추가 보정")]
    [SerializeField] private float extraXOffset = 0f;

    [Tooltip("전체 텍스트 Z 위치 추가 보정")]
    [SerializeField] private float extraZOffset = 0f;

    [Header("Font Asset")]
    [Tooltip("전체 텍스트 공통 폰트. 개별 폰트가 비어있으면 이 폰트를 사용")]
    [SerializeField] private TMP_FontAsset defaultFontAsset;

    [Tooltip("칸 이름 전용 폰트. 비워두면 Default Font Asset 사용")]
    [SerializeField] private TMP_FontAsset nameFontAsset;

    [Tooltip("가격 전용 폰트. 비워두면 Default Font Asset 사용")]
    [SerializeField] private TMP_FontAsset priceFontAsset;

    [Header("Font Size")]
    [SerializeField] private float nameFontSize = 18f;
    [SerializeField] private float priceFontSize = 15f;

    [Header("Font Color")]
    [SerializeField] private Color nameColor = Color.black;
    [SerializeField] private Color priceColor = Color.black;

    [Header("Text Area")]
    [Tooltip("이름 텍스트 영역 가로 크기")]
    [SerializeField] private float nameTextWidth = 8f;

    [Tooltip("이름 텍스트 영역 세로 크기")]
    [SerializeField] private float nameTextHeight = 2f;

    [Tooltip("가격 텍스트 영역 가로 크기")]
    [SerializeField] private float priceTextWidth = 8f;

    [Tooltip("가격 텍스트 영역 세로 크기")]
    [SerializeField] private float priceTextHeight = 2f;

    [Header("Optional 3D Text References")]
    [Tooltip("칸 이름용 3D TextMeshPro")]
    [SerializeField] private TMP_Text txtName;

    [Tooltip("가격용 3D TextMeshPro")]
    [SerializeField] private TMP_Text txtPrice;

    public int CellIndex => cellIndex;
    public BulmabulCellData CellData => cellData;
    public BulmabulCellType CellType => cellData != null ? cellData.cellType : BulmabulCellType.Start;

    /// <summary>
    /// Generator가 호출.
    /// 칸 데이터 자동 세팅.
    /// </summary>
    public void Setup(int index, BulmabulCellData data)
    {
        cellIndex = index;
        cellData = data;

        if (cellData != null)
        {
            cellData.point = transform;
            gameObject.name = $"Cell_{index:000}_{cellData.cellType}";
        }
        else
        {
            gameObject.name = $"Cell_{index:000}_None";
        }

        EnsureLabelRoot();
        Ensure3DTexts();
        ApplyAllTextStyle();
        RefreshTextLayout();
        RefreshView();
    }

    private void Awake()
    {
        if (!Application.isPlaying)
            return;

        EnsureLabelRoot();
        Ensure3DTexts();
        ApplyAllTextStyle();
        RefreshTextLayout();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying)
            return;

        EnsureLabelRoot();
        ApplyAllTextStyle();
        RefreshTextLayout();
    }
#endif

    /// <summary>
    /// LabelRoot가 없으면 생성한다.
    /// LabelRoot는 항상 Cell의 자식이고 localPosition은 0,0,0이다.
    /// </summary>
    private void EnsureLabelRoot()
    {

        // 에디터에서 Prefab Asset 자체를 검사하는 중에는
        // 자식 생성/부모 변경을 하면 Unity가 에러를 낸다.
        if (!Application.isPlaying)
            return;

        if (labelRoot != null)
        {
            labelRoot.SetParent(transform, false);
            labelRoot.localPosition = Vector3.zero;
            labelRoot.localRotation = Quaternion.identity;
            labelRoot.localScale = Vector3.one;
            return;
        }

        Transform existing = transform.Find(labelRootName);

        if (existing != null)
        {
            labelRoot = existing;
        }
        else
        {
            GameObject rootObj = new GameObject(labelRootName);
            labelRoot = rootObj.transform;
            labelRoot.SetParent(transform, false);
        }

        labelRoot.localPosition = Vector3.zero;
        labelRoot.localRotation = Quaternion.identity;
        labelRoot.localScale = Vector3.one;
    }

    /// <summary>
    /// 3D 텍스트가 없으면 LabelRoot 아래에 자동 생성한다.
    /// 직접 만든 텍스트를 사용할 경우 txtName/txtPrice에 연결하면 된다.
    /// </summary>
    private void Ensure3DTexts()
    {
        if (!autoCreate3DTexts)
            return;

        if (!Application.isPlaying)
            return;

        EnsureLabelRoot();

        if (txtName == null)
            txtName = Create3DText("TxtName3D");

        if (txtPrice == null)
            txtPrice = Create3DText("TxtPrice3D");
    }

    private TMP_Text Create3DText(string objectName)
    {
        if (!Application.isPlaying)
            return null;

        EnsureLabelRoot();

        Transform existing = labelRoot.Find(objectName);

        GameObject go;

        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            go = new GameObject(objectName);
            go.transform.SetParent(labelRoot, false);
        }

        go.transform.localScale = Vector3.one * textScale;

        if (useHorizontalTextOnBoard)
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        else
            go.transform.localRotation = Quaternion.identity;

        TextMeshPro tmp = go.GetComponent<TextMeshPro>();

        if (tmp == null)
            tmp = go.AddComponent<TextMeshPro>();

        return tmp;
    }

    /// <summary>
    /// 셀 인덱스에 따라 이름/가격 위치를 다시 배치한다.
    /// LabelRoot 자체는 0,0,0이고, 텍스트 자식 위치만 이동한다.
    /// </summary>
    private void RefreshTextLayout()
    {
        if (labelRoot != null)
        {
            labelRoot.localPosition = Vector3.zero;
            labelRoot.localRotation = Quaternion.identity;
            labelRoot.localScale = Vector3.one;
        }

        CellLabelSide side = ResolveLabelSide();

        Vector3 namePos;
        Vector3 pricePos;

        switch (side)
        {
            // 0~39 라인이라고 가정.
            // 아래쪽 줄이면 텍스트를 위쪽, 즉 +Z 방향으로 보여준다.
            case CellLabelSide.BottomSide:
                namePos = new Vector3(extraXOffset, textHeight, labelDistance + lineGap * 0.5f + extraZOffset);
                pricePos = new Vector3(extraXOffset, textHeight, labelDistance - lineGap * 0.5f + extraZOffset);
                break;

            // 40~79 라인이라고 가정.
            // 왼쪽 줄이면 텍스트를 오른쪽, 즉 +X 방향으로 보여준다.
            case CellLabelSide.LeftSide:
                namePos = new Vector3(labelDistance + extraXOffset, textHeight, lineGap * 0.5f + extraZOffset);
                pricePos = new Vector3(labelDistance + extraXOffset, textHeight, -lineGap * 0.5f + extraZOffset);
                break;

            // 80~119 라인이라고 가정.
            // 위쪽 줄이면 텍스트를 아래쪽, 즉 -Z 방향으로 보여준다.
            case CellLabelSide.TopSide:
                namePos = new Vector3(extraXOffset, textHeight, -labelDistance + lineGap * 0.5f + extraZOffset);
                pricePos = new Vector3(extraXOffset, textHeight, -labelDistance - lineGap * 0.5f + extraZOffset);
                break;

            // 120~159 라인이라고 가정.
            // 오른쪽 줄이면 텍스트를 왼쪽, 즉 -X 방향으로 보여준다.
            case CellLabelSide.RightSide:
                namePos = new Vector3(-labelDistance + extraXOffset, textHeight, lineGap * 0.5f + extraZOffset);
                pricePos = new Vector3(-labelDistance + extraXOffset, textHeight, -lineGap * 0.5f + extraZOffset);
                break;

            default:
                namePos = new Vector3(extraXOffset, textHeight, labelDistance + lineGap * 0.5f + extraZOffset);
                pricePos = new Vector3(extraXOffset, textHeight, labelDistance - lineGap * 0.5f + extraZOffset);
                break;
        }

        if (txtName != null)
            txtName.transform.localPosition = namePos;

        if (txtPrice != null)
            txtPrice.transform.localPosition = pricePos;

        ApplyTextRotation();
    }

    /// <summary>
    /// 현재 칸이 어느 줄에 있는지 계산한다.
    /// 160칸, 40/40/40/40 구조 기준.
    /// </summary>
    private CellLabelSide ResolveLabelSide()
    {
        if (labelSide != CellLabelSide.AutoByIndex)
            return labelSide;

        int perSide = Mathf.Max(1, cellsPerSide);

        if (cellIndex < perSide)
            return CellLabelSide.BottomSide;

        if (cellIndex < perSide * 2)
            return CellLabelSide.LeftSide;

        if (cellIndex < perSide * 3)
            return CellLabelSide.TopSide;

        return CellLabelSide.RightSide;
    }

    /// <summary>
    /// 텍스트 회전값 적용.
    /// </summary>
    private void ApplyTextRotation()
    {
        Quaternion rot = useHorizontalTextOnBoard
            ? Quaternion.Euler(90f, 0f, 0f)
            : Quaternion.identity;

        if (txtName != null)
            txtName.transform.localRotation = rot;

        if (txtPrice != null)
            txtPrice.transform.localRotation = rot;
    }

    /// <summary>
    /// 연결된 모든 텍스트에 폰트/크기/색상/정렬 적용.
    /// 직접 만든 3D TextMeshPro에도 적용된다.
    /// </summary>
    private void ApplyAllTextStyle()
    {
        ApplyTextStyle(txtName, GetFont(nameFontAsset), nameFontSize, nameColor, nameTextWidth, nameTextHeight);
        ApplyTextStyle(txtPrice, GetFont(priceFontAsset), priceFontSize, priceColor, priceTextWidth, priceTextHeight);
    }

    private TMP_FontAsset GetFont(TMP_FontAsset specificFont)
    {
        if (specificFont != null)
            return specificFont;

        return defaultFontAsset;
    }

    private void ApplyTextStyle(
        TMP_Text target,
        TMP_FontAsset font,
        float fontSize,
        Color color,
        float width,
        float height)
    {
        if (target == null)
            return;

        if (font != null)
            target.font = font;

        target.fontSize = fontSize;
        target.color = color;
        target.alignment = TextAlignmentOptions.Center;
        target.enableWordWrapping = false;
        target.overflowMode = TextOverflowModes.Overflow;
        target.richText = false;

        RectTransform rect = target.rectTransform;
        if (rect != null)
            rect.sizeDelta = new Vector2(width, height);

        target.transform.localScale = Vector3.one * textScale;
    }

    /// <summary>
    /// 데이터에 따라 표시 갱신.
    /// 언어가 바뀌었을 때도 다시 호출하면 된다.
    /// </summary>
    public void RefreshView()
    {
        ApplyAllTextStyle();
        RefreshTextLayout();

        if (cellData == null)
        {
            SetText(txtName, "");
            SetText(txtPrice, "");
            return;
        }

        string displayName = string.IsNullOrWhiteSpace(cellData.cellName)
            ? GetDefaultName(cellData.cellType)
            : cellData.cellName;

        SetText(txtName, displayName);
        SetText(txtPrice, GetPriceText(cellData));
    }

    private void SetText(TMP_Text target, string value)
    {
        if (target == null)
            return;

        target.text = value;

        bool hasText = !string.IsNullOrWhiteSpace(value);

        if (target.gameObject.activeSelf != hasText)
            target.gameObject.SetActive(hasText);
    }

    private string GetDefaultName(BulmabulCellType type)
    {
        bool isEng = IsEnglish();

        switch (type)
        {
            case BulmabulCellType.Start:
                return isEng ? "Start" : "시작";

            case BulmabulCellType.Land:
                return isEng ? "Land" : "땅";

            case BulmabulCellType.Tax:
                return isEng ? "Tax" : "세금";

            case BulmabulCellType.Bonus:
                return isEng ? "Bonus" : "보너스";

            case BulmabulCellType.Chance:
                return isEng ? "Chance" : "찬스";

            case BulmabulCellType.Jail:
                return isEng ? "Jail" : "감옥";

            case BulmabulCellType.Travel:
                return isEng ? "Travel" : "여행";
        }

        return isEng ? "Cell" : "칸";
    }

    private bool IsEnglish()
    {
        var lang = (LaguageManager.Instance != null)
            ? LaguageManager.Instance.currentLang
            : Lauaguage.Kor;

        return lang == Lauaguage.Eng;
    }

    private string GetPriceText(BulmabulCellData data)
    {
        switch (data.cellType)
        {
            case BulmabulCellType.Land:
                return data.buyCost > 0 ? $"{data.buyCost:N0}" : "";

            case BulmabulCellType.Tax:
                return data.taxCost > 0 ? $"-{data.taxCost:N0}" : "";

            case BulmabulCellType.Bonus:
                if (data.bonusMaxAmount <= 0)
                    return "";

                if (data.bonusMinAmount == data.bonusMaxAmount)
                    return $"+{data.bonusMinAmount:N0}";

                return $"+{data.bonusMinAmount:N0}~{data.bonusMaxAmount:N0}";

            case BulmabulCellType.Travel:
                return data.travelCost > 0 ? $"{data.travelCost:N0}" : "";

            default:
                return "";
        }
    }
}