using Gpm.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 여행 비용을 지불한 뒤, 다음 자기 턴에 열리는 목적지 선택 팝업.
/// GPM InfiniteScroll을 사용해서 보드 전체 지역을 자동으로 넣는다.
/// </summary>
public class BulmabulTravelTargetPopup : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject root;

    [Header("Board")]
    [Tooltip("게임 보드 데이터. GameScene의 BulmabulBoard를 연결")]
    [SerializeField] private BulmabulBoard board;

    [Header("GPM InfiniteScroll")]
    [SerializeField] private InfiniteScroll infiniteScroll;

    [Header("Texts")]
    [SerializeField] private TMP_Text txtTitle;
    [SerializeField] private TMP_Text txtInfo;
    [SerializeField] private TMP_Text txtCloseButton;

    [Header("Buttons")]
    [SerializeField] private Button btnClose;

    [Header("Target Settings")]
    [Tooltip("true면 시작/세금/보너스/찬스/감옥/여행 포함 전체 칸을 여행 목적지로 표시")]
    [SerializeField] private bool includeSpecialCells = true;

    [Tooltip("false면 현재 서 있는 여행 칸은 목록에서 제외")]
    [SerializeField] private bool includeCurrentCell = true;

    public bool IsOpen => root != null ? root.activeSelf : gameObject.activeSelf;

    private void Awake()
    {
        if (btnClose != null)
        {
            btnClose.onClick.RemoveListener(Close);
            btnClose.onClick.AddListener(Close);
        }

        Close();
    }

    public void Open()
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null)
            return;

        if (!state.CanLocalUseTravel())
            return;

        if (board == null)
            board = FindObjectOfType<BulmabulBoard>();

        if (root != null)
            root.SetActive(true);
        else
            gameObject.SetActive(true);

        RefreshTexts();
        RebuildList();
    }

    public void Close()
    {
        if (root != null)
            root.SetActive(false);
        else
            gameObject.SetActive(false);
    }

    private void RefreshTexts()
    {
        bool eng = IsEnglish();

        if (txtTitle != null)
            txtTitle.text = eng ? "Choose Travel Destination" : "여행 목적지 선택";

        if (txtInfo != null)
        {
            txtInfo.text = eng
                ? "Select the area you want to travel to."
                : "이동할 지역을 선택하세요.";
        }

        if (txtCloseButton != null)
            txtCloseButton.text = eng ? "Close" : "닫기";
    }

    private void RebuildList()
    {
        if (infiniteScroll == null)
            return;

        if (board == null || board.CellCount <= 0)
            return;

        BulmabulGameState state = BulmabulGameState.Instance;
        int currentCellIndex = state != null ? state.GetLocalPlayerCellIndex() : -1;

        infiniteScroll.Clear();

        int insertIndex = 0;

        for (int i = 0; i < board.CellCount; i++)
        {
            BulmabulCellData cell = board.GetCell(i);

            if (cell == null)
                continue;

            if (!includeCurrentCell && i == currentCellIndex)
                continue;

            if (!includeSpecialCells && cell.cellType != BulmabulCellType.Land)
                continue;

            BulmabulTravelTargetData data = new BulmabulTravelTargetData
            {
                cellIndex = i,
                cellName = cell.cellName,
                cellType = cell.cellType,
                buyCost = cell.buyCost,
                tollCost = cell.tollCost,
                onClick = OnClickTravelTarget
            };

            infiniteScroll.InsertData(data, insertIndex);
            insertIndex++;
        }

        infiniteScroll.UpdateAllData(true);

        if (insertIndex > 0)
            infiniteScroll.MoveToFirstData();
    }

    private void OnClickTravelTarget(int targetCellIndex)
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null)
            return;

        if (!state.CanLocalUseTravel())
            return;

        Close();

        state.RequestTravelMoveLocal(targetCellIndex);
    }

    private bool IsEnglish()
    {
        var lang = LaguageManager.Instance != null
            ? LaguageManager.Instance.currentLang
            : Lauaguage.Kor;

        return lang == Lauaguage.Eng;
    }
}