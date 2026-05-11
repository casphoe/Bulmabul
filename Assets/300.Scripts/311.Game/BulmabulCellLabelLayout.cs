using TMPro;
using UnityEngine;

/// <summary>
/// 보드 셀의 이름/가격 텍스트 위치를 자동 배치.
/// 
/// 구조:
/// Cell
///  └── LabelRoot (localPosition = 0,0,0 고정)
///       ├── TxtName3D
///       └── TxtPrice3D
/// 
/// 규칙:
/// - 윗줄 셀    : 텍스트를 아래쪽(-Z)으로 배치
/// - 아랫줄 셀  : 텍스트를 위쪽(+Z)으로 배치
/// - 왼쪽 줄 셀 : 텍스트를 오른쪽(+X)으로 배치
/// - 오른쪽 줄 셀: 텍스트를 왼쪽(-X)으로 배치
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

    private void Awake()
    {
        EnsureReferences();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        EnsureReferences();
    }
#endif

    /// <summary>
    /// LabelRoot / 텍스트 자동 연결.
    /// </summary>
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
            if (tr == null) tr = transform.Find("TxtName3D");
            if (tr != null) txtLandName = tr.GetComponent<TMP_Text>();
        }

        if (txtPrice == null)
        {
            Transform tr = labelRoot.Find("TxtPrice3D");
            if (tr == null) tr = transform.Find("TxtPrice3D");
            if (tr != null) txtPrice = tr.GetComponent<TMP_Text>();
        }

        if (txtLandName != null)
            txtLandName.transform.SetParent(labelRoot, true);

        if (txtPrice != null)
            txtPrice.transform.SetParent(labelRoot, true);
    }

    /// <summary>
    /// 보드 중앙 기준으로 텍스트 위치 갱신.
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

        if (txtLandName != null)
            txtLandName.transform.localPosition = namePos;

        if (txtPrice != null)
            txtPrice.transform.localPosition = pricePos;

        ApplyTextRotation();
        ApplyTextAlignment(side);
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

    private void ApplyTextAlignment(LabelSide side)
    {
        TextAlignmentOptions align = TextAlignmentOptions.Center;

        switch (side)
        {
            case LabelSide.Left:
                align = TextAlignmentOptions.Left;
                break;

            case LabelSide.Right:
                align = TextAlignmentOptions.Right;
                break;
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
            txtLandName.transform.localRotation = rot;

        if (txtPrice != null)
            txtPrice.transform.localRotation = rot;
    }
}