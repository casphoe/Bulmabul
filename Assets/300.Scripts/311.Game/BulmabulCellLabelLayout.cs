using TMPro;
using UnityEngine;

/// <summary>
/// 보드 셀의 이름/가격 텍스트 위치를 자동 배치.
/// 기본 재배치는 유지하고, 사진에서 맞춘 셀만 예외 위치를 적용한다.
/// </summary>
public class BulmabulCellLabelLayout : MonoBehaviour
{
    public enum LabelSide
    {
        Auto,
        Top,
        Bottom,
        Left,
        Right
    }

    [Header("References")]
    [SerializeField] private Transform labelRoot;
    [SerializeField] private TMP_Text txtLandName;
    [SerializeField] private TMP_Text txtPrice;

    [Header("Layout")]
    [Tooltip("Auto면 보드 중앙 기준 자동 판단")]
    [SerializeField] private LabelSide sideOverride = LabelSide.Auto;

    [Tooltip("셀 중심에서 텍스트 묶음을 얼마나 떨어뜨릴지")]
    private float labelDistance = 1.05f;

    [Tooltip("텍스트가 발판 위에 뜨는 높이")]
    private float textHeight = 0.08f;

    [Tooltip("이름/가격 줄 간격")]
    private float lineGap = 0.28f;

    [Header("Fine Tune")]
    [SerializeField] private float extraXOffset = 0f;
    [SerializeField] private float extraZOffset = 0f;

    [Header("Text Rotation")]
    [Tooltip("텍스트를 발판 위에 눕혀서 표시")]
    [SerializeField] private bool useHorizontalTextOnBoard = true;

    [Header("Runtime Cell Info")]
    [SerializeField] private int cellIndex = -1;
    [SerializeField] private BulmabulCellType cellType = BulmabulCellType.Land;
    [SerializeField] private int cellsPerSide = 40;

    [System.Serializable]
    public class CellLabelOverride
    {
        public int cellIndex = -1;

        public bool overrideNamePosition = true;
        public Vector3 nameLocalPosition;

        public bool overridePricePosition = true;
        public Vector3 priceLocalPosition;
    }

    [System.Serializable]
    public class CellLabelRangeOverride
    {
        public int startIndex = -1;
        public int endIndex = -1;

        public bool overrideNamePosition = true;
        public Vector3 nameLocalPosition;

        public bool overridePricePosition = true;
        public Vector3 priceLocalPosition;
    }

    [Header("Only Selected Cell Position Overrides")]
    [SerializeField]
    private CellLabelOverride[] cellOverrides =
    {
         new CellLabelOverride
        {
            cellIndex = 0,
            overrideNamePosition = true,
            nameLocalPosition = new Vector3(0f, 0.08f, 0.863f),
            overridePricePosition = false
        },

        // Cell 039 - 사진에서 맞춘 값 유지
        new CellLabelOverride
        {
            cellIndex = 39,
            overrideNamePosition = true,
            nameLocalPosition = new Vector3(0.049f, 0.08f, -0.717f),
            overridePricePosition = true,
            priceLocalPosition = new Vector3(0f, 0.08f, -0.99f)
        },

    // Cell 040 Jail - 사진 오른쪽 값 기준
    new CellLabelOverride
    {
        cellIndex = 40,
        overrideNamePosition = true,
        nameLocalPosition = new Vector3(-1.429f, 0.08f, -0.702f),
        overridePricePosition = false
    },

    // Cell 080 Travel - 이전에 맞춘 값 유지
    new CellLabelOverride
    {
        cellIndex = 80,
        overrideNamePosition = true,
        nameLocalPosition = new Vector3(0f, 0.08f, -0.749f),
        overridePricePosition = true,
        priceLocalPosition = new Vector3(0f, 0.08f, -0.939f)
    },

    // Cell 119 - 이전에 맞춘 값 유지
    new CellLabelOverride
    {
        cellIndex = 119,
        overrideNamePosition = true,
        nameLocalPosition = new Vector3(-0.894f, 0.08f, -0.164f),
        overridePricePosition = true,
        priceLocalPosition = new Vector3(-0.91f, 0.08f, 0.16f)
    },

    // Cell 159 - 사진 오른쪽 값 기준
    new CellLabelOverride
    {
        cellIndex = 159,
        overrideNamePosition = true,
        nameLocalPosition = new Vector3(-0.781f, 0.08f, 0.083f),
        overridePricePosition = true,
        priceLocalPosition = new Vector3(-0.772f, 0.08f, -0.177f)
    }
    };

    [Header("Only Selected Range Position Overrides")]
    [SerializeField]
    private CellLabelRangeOverride[] rangeOverrides =
    {
        // 속초해수욕장 다음부터 오른쪽 라인 끝까지 안 보이던 구간만 보정.
        // 나머지 셀은 기존 재배치 그대로 둔다.
        new CellLabelRangeOverride
        {
            startIndex = 48,
            endIndex = 79,
            nameLocalPosition = new Vector3(0.988f, 0.08f, 0.14f),
            priceLocalPosition = new Vector3(0.988f, 0.08f, -0.14f)
        }
    };

    private void Awake()
    {
        EnsureReferences();
    }

    public void Setup(int index, BulmabulCellType type, int perSide)
    {
        cellIndex = index;
        cellType = type;
        cellsPerSide = Mathf.Max(1, perSide);

        EnsureReferences();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        EnsureReferences();
    }
#endif

    private void EnsureReferences()
    {
        if (labelRoot == null)
        {
            Transform found = transform.Find("LabelRoot");
            if (found != null)
            {
                labelRoot = found;
            }
            else
            {
                GameObject go = new GameObject("LabelRoot");
                labelRoot = go.transform;
                labelRoot.SetParent(transform, false);
            }
        }

        labelRoot.SetParent(transform, false);
        labelRoot.localPosition = Vector3.zero;
        labelRoot.localRotation = Quaternion.identity;
        labelRoot.localScale = Vector3.one;

        if (txtLandName == null)
        {
            Transform tr = labelRoot.Find("TxtName3D");
            if (tr == null)
                tr = transform.Find("TxtName3D");

            if (tr != null)
                txtLandName = tr.GetComponent<TMP_Text>();
        }

        if (txtPrice == null)
        {
            Transform tr = labelRoot.Find("TxtPrice3D");
            if (tr == null)
                tr = transform.Find("TxtPrice3D");

            if (tr != null)
                txtPrice = tr.GetComponent<TMP_Text>();
        }

        if (txtLandName != null)
            txtLandName.transform.SetParent(labelRoot, true);

        if (txtPrice != null)
            txtPrice.transform.SetParent(labelRoot, true);
    }

    /// <summary>
    /// 보드 중앙 기준으로 텍스트 위치 갱신.
    /// 단, cellOverrides / rangeOverrides에 등록된 셀은 사진에서 맞춘 위치를 우선 적용한다.
    /// </summary>
    public void RefreshLayout(Vector3 boardCenter)
    {
        EnsureReferences();

        if (labelRoot == null)
            return;

        labelRoot.localPosition = Vector3.zero;
        labelRoot.localRotation = Quaternion.identity;
        labelRoot.localScale = Vector3.one;

        LabelSide side = ResolveSide(boardCenter);

        Vector3 anchorPos;
        Vector3 namePos;
        Vector3 pricePos;

        switch (side)
        {
            case LabelSide.Top:
                // 위쪽 줄 -> 아래쪽(-Z)
                anchorPos = new Vector3(extraXOffset, textHeight, -labelDistance + extraZOffset);
                namePos = anchorPos + new Vector3(0f, 0f, lineGap * 0.5f);
                pricePos = anchorPos + new Vector3(0f, 0f, -lineGap * 0.5f);
                break;

            case LabelSide.Bottom:
                // 아래쪽 줄 -> 위쪽(+Z)
                anchorPos = new Vector3(extraXOffset, textHeight, labelDistance + extraZOffset);
                namePos = anchorPos + new Vector3(0f, 0f, lineGap * 0.5f);
                pricePos = anchorPos + new Vector3(0f, 0f, -lineGap * 0.5f);
                break;

            case LabelSide.Left:
                // 왼쪽 줄 -> 오른쪽(+X)
                anchorPos = new Vector3(labelDistance + extraXOffset, textHeight, extraZOffset);
                namePos = anchorPos + new Vector3(0f, 0f, lineGap * 0.5f);
                pricePos = anchorPos + new Vector3(0f, 0f, -lineGap * 0.5f);
                break;

            case LabelSide.Right:
                // 오른쪽 줄 -> 왼쪽(-X)
                anchorPos = new Vector3(-labelDistance + extraXOffset, textHeight, extraZOffset);
                namePos = anchorPos + new Vector3(0f, 0f, lineGap * 0.5f);
                pricePos = anchorPos + new Vector3(0f, 0f, -lineGap * 0.5f);
                break;

            default:
                anchorPos = new Vector3(extraXOffset, textHeight, labelDistance + extraZOffset);
                namePos = anchorPos + new Vector3(0f, 0f, lineGap * 0.5f);
                pricePos = anchorPos + new Vector3(0f, 0f, -lineGap * 0.5f);
                break;
        }

        bool usedManualOverride = ApplyOverridePosition(ref namePos, ref pricePos);

        if (txtLandName != null)
            txtLandName.transform.localPosition = namePos;

        if (txtPrice != null)
            txtPrice.transform.localPosition = pricePos;

        ApplyTextRenderSettings(txtLandName);
        ApplyTextRenderSettings(txtPrice);

        ApplyTextRotation();
        ApplyTextAlignment(side, usedManualOverride);
    }

    private LabelSide ResolveSide(Vector3 boardCenter)
    {
        if (sideOverride != LabelSide.Auto)
            return sideOverride;

        Vector3 delta = transform.position - boardCenter;

        if (Mathf.Abs(delta.x) > Mathf.Abs(delta.z))
            return delta.x < 0f ? LabelSide.Left : LabelSide.Right;

        return delta.z < 0f ? LabelSide.Bottom : LabelSide.Top;
    }

    private bool ApplyOverridePosition(ref Vector3 namePos, ref Vector3 pricePos)
    {
        if (TryApplyCellOverride(ref namePos, ref pricePos))
            return true;

        return TryApplyRangeOverride(ref namePos, ref pricePos);
    }

    private bool TryApplyCellOverride(ref Vector3 namePos, ref Vector3 pricePos)
    {
        if (cellOverrides == null)
            return false;

        for (int i = 0; i < cellOverrides.Length; i++)
        {
            CellLabelOverride item = cellOverrides[i];

            if (item == null)
                continue;

            if (item.cellIndex != cellIndex)
                continue;

            if (item.overrideNamePosition)
                namePos = item.nameLocalPosition;

            if (item.overridePricePosition)
                pricePos = item.priceLocalPosition;

            return true;
        }

        return false;
    }

    private bool TryApplyRangeOverride(ref Vector3 namePos, ref Vector3 pricePos)
    {
        if (rangeOverrides == null)
            return false;

        for (int i = 0; i < rangeOverrides.Length; i++)
        {
            CellLabelRangeOverride item = rangeOverrides[i];

            if (item == null)
                continue;

            if (cellIndex < item.startIndex || cellIndex > item.endIndex)
                continue;

            if (item.overrideNamePosition)
                namePos = item.nameLocalPosition;

            if (item.overridePricePosition)
                pricePos = item.priceLocalPosition;

            return true;
        }

        return false;
    }

    private void ApplyTextAlignment(LabelSide side, bool usedManualOverride)
    {
        TextAlignmentOptions align = TextAlignmentOptions.Center;

        // 사진에서 직접 맞춘 셀은 정렬까지 다시 흔들지 말고 Center 유지
        if (!usedManualOverride)
        {
            switch (side)
            {
                case LabelSide.Left:
                    align = TextAlignmentOptions.Left;
                    break;

                case LabelSide.Right:
                    align = TextAlignmentOptions.Right;
                    break;
            }
        }

        if (txtLandName != null)
            txtLandName.alignment = align;

        if (txtPrice != null)
            txtPrice.alignment = align;
    }

    private void ApplyTextRotation()
    {
        Quaternion rot = useHorizontalTextOnBoard
            ? Quaternion.Euler(90f, 0f, 0f)
            : Quaternion.identity;

        if (txtLandName != null)
        {
            txtLandName.transform.localRotation = rot;
            ApplyTextRenderSettings(txtLandName);
        }

        if (txtPrice != null)
        {
            txtPrice.transform.localRotation = rot;
            ApplyTextRenderSettings(txtPrice);
        }
    }

    private void ApplyTextRenderSettings(TMP_Text text)
    {
        if (text == null)
            return;

        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;

        // TextMeshPro 3D는 각도에 따라 뒷면이 안 보일 수 있음.
        // 그래서 머티리얼을 복사한 뒤 Cull Off로 강제한다.
        Material mat = text.fontMaterial;

        if (mat != null)
        {
            Material instanceMat = new Material(mat);

            // TMP Distance Field 계열 셰이더에서 주로 사용하는 Cull 속성
            if (instanceMat.HasProperty("_CullMode"))
                instanceMat.SetFloat("_CullMode", 0f);

            // 일부 셰이더는 _Cull을 사용함
            if (instanceMat.HasProperty("_Cull"))
                instanceMat.SetFloat("_Cull", 0f);

            text.fontMaterial = instanceMat;
        }

        TextMeshPro tmp3D = text as TextMeshPro;
        if (tmp3D != null)
        {
            MeshRenderer renderer = tmp3D.GetComponent<MeshRenderer>();

            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            tmp3D.sortingOrder = 100;
        }

        text.ForceMeshUpdate();
    }
}