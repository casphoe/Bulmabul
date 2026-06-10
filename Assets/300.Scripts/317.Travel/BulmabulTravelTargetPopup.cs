using Gpm.Ui;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 여행 비용을 지불한 뒤, 다음 자기 턴에 열리는 목적지 선택 팝업.
/// GPM InfiniteScroll을 사용해서 보드 전체 지역을 자동으로 넣는다.
/// 검색창에 입력한 글자로 시작하는 지역만 표시할 수 있다.
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

    [Header("Search Result Panels")]
    [Tooltip("검색 결과가 있을 때 켜지는 패널. 예: TravelFound")]
    [SerializeField] private GameObject travelFoundPanel;

    [Tooltip("검색 결과가 없을 때 켜지는 패널. 예: NoTravelFound")]
    [SerializeField] private GameObject noTravelFoundPanel;

    [Header("Texts")]
    [SerializeField] private TMP_Text txtTitle;
    [SerializeField] private TMP_Text txtInfo;
    [SerializeField] private TMP_Text txtCloseButton;

    [Header("Search")]
    [Tooltip("여행 목적지 이름 검색 입력창")]
    [SerializeField] private TMP_InputField inputSearch;

    [Tooltip("검색 결과가 없을 때 표시할 텍스트")]
    [SerializeField] private TMP_Text txtEmpty;

    [Header("Buttons")]
    [SerializeField] private Button btnClose;

    [Header("Target Settings")]
    [Tooltip("true면 시작/세금/보너스/찬스/감옥/여행 포함 전체 칸을 여행 목적지로 표시")]
    [SerializeField] private bool includeSpecialCells = true;

    [Tooltip("false면 현재 서 있는 여행 칸은 목록에서 제외")]
    [SerializeField] private bool includeCurrentCell = true;

    [Header("Sort Buttons")]
    [SerializeField] private Button btnSortForward;
    [SerializeField] private Button btnSortReverse;

    [SerializeField] private TMP_Text txtSortForward;
    [SerializeField] private TMP_Text txtSortReverse;

    [Header("Sort Settings")]
    [Tooltip("true면 160,159,158 역순 / false면 0,1,2 정순")]
    [SerializeField] private bool isReverseSort = false;

    public bool IsOpen => root != null ? root.activeSelf : gameObject.activeSelf;

    private void Awake()
    {
        if (btnClose != null)
        {
            btnClose.onClick.RemoveListener(Close);
            btnClose.onClick.AddListener(Close);
        }

        if (inputSearch != null)
        {
            inputSearch.onValueChanged.RemoveListener(OnSearchValueChanged);
            inputSearch.onValueChanged.AddListener(OnSearchValueChanged);
        }

        if (btnSortForward != null)
        {
            btnSortForward.onClick.RemoveListener(SetSortForward);
            btnSortForward.onClick.AddListener(SetSortForward);
        }

        if (btnSortReverse != null)
        {
            btnSortReverse.onClick.RemoveListener(SetSortReverse);
            btnSortReverse.onClick.AddListener(SetSortReverse);
        }

        Close();
    }

    private void OnDestroy()
    {
        if (inputSearch != null)
            inputSearch.onValueChanged.RemoveListener(OnSearchValueChanged);

        if (btnSortForward != null)
            btnSortForward.onClick.RemoveListener(SetSortForward);

        if (btnSortReverse != null)
            btnSortReverse.onClick.RemoveListener(SetSortReverse);
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

        if (inputSearch != null)
        {
            inputSearch.SetTextWithoutNotify("");
            inputSearch.ActivateInputField();
        }

        RefreshTexts();
        RebuildList();
    }

    private void RefreshSearchResultPanels(bool hasResult)
    {
        if (travelFoundPanel != null)
            travelFoundPanel.SetActive(hasResult);

        if (noTravelFoundPanel != null)
            noTravelFoundPanel.SetActive(!hasResult);

        if (txtEmpty != null)
            txtEmpty.gameObject.SetActive(!hasResult);
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
                ? "Enter the first letter or select the area you want to travel to."
                : "첫 글자를 입력하거나 이동할 지역을 선택하세요.";
        }

        if (txtCloseButton != null)
            txtCloseButton.text = eng ? "Close" : "닫기";

        if (inputSearch != null && inputSearch.placeholder is TMP_Text placeholder)
        {
            placeholder.text = eng
                ? "Search by first letter"
                : "첫 글자로 검색";
        }

        if (txtEmpty != null)
        {
            txtEmpty.text = eng
                ? "No matching destination."
                : "검색된 목적지가 없습니다.";

            txtEmpty.gameObject.SetActive(false);
        }

        RefreshSortButtonTexts();
    }

    private void OnSearchValueChanged(string value)
    {
        RebuildList();
    }

    private void RebuildList()
    {
        if (board == null || board.CellCount <= 0)
            return;

        string searchText = inputSearch != null ? inputSearch.text : "";
        searchText = NormalizeSearchText(searchText);

        BulmabulGameState state = BulmabulGameState.Instance;
        int currentCellIndex = state != null ? state.GetLocalPlayerCellIndex() : -1;

        List<BulmabulTravelTargetData> resultList = new List<BulmabulTravelTargetData>();

        int cellCount = board.CellCount;

        if (!isReverseSort)
        {
            // 정순: 0, 1, 2, 3 ... 마지막 칸
            for (int i = 0; i < cellCount; i++)
            {
                TryMakeTravelTargetData(i, currentCellIndex, searchText, resultList);
            }
        }
        else
        {
            // 역순: 마지막 칸, 마지막 칸 - 1 ... 0
            for (int i = cellCount - 1; i >= 0; i--)
            {
                TryMakeTravelTargetData(i, currentCellIndex, searchText, resultList);
            }
        }

        bool hasResult = resultList.Count > 0;

        // 중요:
        // InfiniteScroll 데이터를 넣기 전에 먼저 결과 패널을 켜야 함.
        RefreshSearchResultPanels(hasResult);

        if (infiniteScroll == null)
            return;

        infiniteScroll.Clear();

        if (!hasResult)
        {
            infiniteScroll.UpdateAllData(true);
            Canvas.ForceUpdateCanvases();
            return;
        }

        Canvas.ForceUpdateCanvases();

        for (int i = 0; i < resultList.Count; i++)
        {
            infiniteScroll.InsertData(resultList[i], i);
        }

        infiniteScroll.UpdateAllData(true);
        infiniteScroll.MoveToFirstData();

        Canvas.ForceUpdateCanvases();
    }

    private void TryMakeTravelTargetData(
    int cellIndex,
    int currentCellIndex,
    string searchText,
    List<BulmabulTravelTargetData> resultList)
    {
        if (board == null)
            return;

        BulmabulCellData cell = board.GetCell(cellIndex);

        if (cell == null)
            return;

        if (!includeCurrentCell && cellIndex == currentCellIndex)
            return;

        if (!includeSpecialCells && cell.cellType != BulmabulCellType.Land)
            return;

        string cellName = string.IsNullOrWhiteSpace(cell.cellName)
            ? GetByLanguage($"지역 {cellIndex}", $"Area {cellIndex}")
            : cell.cellName;

        if (!IsMatchByFirstLetter(cellName, searchText))
            return;

        BulmabulGameState state = BulmabulGameState.Instance;

        int ownerIndex = -1;
        string ownerName = "";
        int ownerTeamSideInt = 0;

        bool isTeamMode =
            BulmabulGameStartCache.HasCache &&
            BulmabulGameStartCache.ModeInt == (int)MatchMode.Team;

        if (state != null && cell.cellType == BulmabulCellType.Land)
        {
            ownerIndex = state.LandOwnerByCell.Get(cellIndex);

            if (ownerIndex >= 0 && ownerIndex < BulmabulGameState.MaxPlayers)
            {
                var ownerSlot = state.Players.Get(ownerIndex);

                string nickname = ownerSlot.nickname.ToString();

                ownerName = string.IsNullOrWhiteSpace(nickname)
                    ? $"P{ownerIndex + 1}"
                    : nickname;

                ownerTeamSideInt = ownerSlot.teamSideInt;
            }
        }

        BulmabulTravelTargetData data = new BulmabulTravelTargetData
        {
            cellIndex = cellIndex,
            cellName = cellName,
            cellType = cell.cellType,
            buyCost = cell.buyCost,
            tollCost = cell.tollCost,

            ownerIndex = ownerIndex,
            ownerName = ownerName,
            ownerTeamSideInt = ownerTeamSideInt,
            isTeamMode = isTeamMode,

            onClick = OnClickTravelTarget
        };

        resultList.Add(data);
    }

    /// <summary>
    /// 검색어 전처리.
    /// 앞뒤 공백 제거 + 소문자 변환.
    /// 영어는 대소문자 구분 없이 검색된다.
    /// </summary>
    private string NormalizeSearchText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        return value.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// 지역 이름이 검색어로 시작하는지 확인한다.
    /// 
    /// 예:
    /// 검색어 "ㄱ" → "강릉", "광주" 표시
    /// 검색어 "a"  → "Area", "Argentina" 표시
    /// 검색어 "속" → "속초" 표시
    /// </summary>
    private bool IsMatchByFirstLetter(string cellName, string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
            return true;

        if (string.IsNullOrWhiteSpace(cellName))
            return false;

        string targetName = cellName.Trim().ToLowerInvariant();

        if (targetName.StartsWith(searchText))
            return true;

        // 검색어가 한글 초성 1글자인 경우
        if (searchText.Length == 1 && IsKoreanInitialConsonant(searchText[0]))
        {
            char firstChar = targetName[0];
            char firstInitial = GetKoreanInitialConsonant(firstChar);

            return firstInitial == searchText[0];
        }

        return false;
    }

    private bool IsKoreanInitialConsonant(char c)
    {
        return c == 'ㄱ' || c == 'ㄲ' || c == 'ㄴ' || c == 'ㄷ' || c == 'ㄸ' ||
               c == 'ㄹ' || c == 'ㅁ' || c == 'ㅂ' || c == 'ㅃ' || c == 'ㅅ' ||
               c == 'ㅆ' || c == 'ㅇ' || c == 'ㅈ' || c == 'ㅉ' || c == 'ㅊ' ||
               c == 'ㅋ' || c == 'ㅌ' || c == 'ㅍ' || c == 'ㅎ';
    }

    private char GetKoreanInitialConsonant(char c)
    {
        char[] initials =
        {
        'ㄱ', 'ㄲ', 'ㄴ', 'ㄷ', 'ㄸ',
        'ㄹ', 'ㅁ', 'ㅂ', 'ㅃ', 'ㅅ',
        'ㅆ', 'ㅇ', 'ㅈ', 'ㅉ', 'ㅊ',
        'ㅋ', 'ㅌ', 'ㅍ', 'ㅎ'
    };

        if (c < '가' || c > '힣')
            return c;

        int unicode = c - '가';
        int initialIndex = unicode / (21 * 28);

        return initials[initialIndex];
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

    private string GetByLanguage(string kor, string eng)
    {
        return IsEnglish() ? eng : kor;
    }

    private bool IsEnglish()
    {
        var lang = LaguageManager.Instance != null
            ? LaguageManager.Instance.currentLang
            : Lauaguage.Kor;

        return lang == Lauaguage.Eng;
    }

    #region 정렬
    private void SetSortForward()
    {
        isReverseSort = false;
        RefreshSortButtonTexts();
        RebuildList();
    }

    private void SetSortReverse()
    {
        isReverseSort = true;
        RefreshSortButtonTexts();
        RebuildList();
    }

    private void RefreshSortButtonTexts()
    {
        bool eng = IsEnglish();

        if (txtSortForward != null)
            txtSortForward.text = eng ? "0 → Last" : "0번부터";

        if (txtSortReverse != null)
            txtSortReverse.text = eng ? "Last → 0" : "역순";

        if (btnSortForward != null)
            btnSortForward.interactable = isReverseSort;

        if (btnSortReverse != null)
            btnSortReverse.interactable = !isReverseSort;
    }
    #endregion
}