using System;
using Gpm.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class BulmabulTravelTargetData : InfiniteScrollData
{
    public int cellIndex;
    public string cellName;
    public BulmabulCellType cellType;
    public int buyCost;
    public int tollCost;

    public int ownerIndex = -1;
    public string ownerName;
    public int ownerTeamSideInt = 0;
    public bool isTeamMode;

    public Action<int> onClick;
}

public class BulmabulTravelTargetItem : InfiniteScrollItem
{
    [Header("Texts")]
    [SerializeField] private TMP_Text txtName;
    [SerializeField] private TMP_Text txtType;
    [SerializeField] private TMP_Text txtInfo;
    [SerializeField] private TMP_Text txtOwner;
    [SerializeField] private TMP_Text txtButton;

    [Header("Team Mode UI")]
    [Tooltip("팀전일 때만 켜지는 오브젝트")]
    [SerializeField] private GameObject teamModeObject;

    [Tooltip("팀전 소유 정보 텍스트")]
    [SerializeField] private TMP_Text txtTeamOwner;

    [Header("Button")]
    [SerializeField] private Button btnSelect;

    private BulmabulTravelTargetData _data;

    public override void UpdateData(InfiniteScrollData scrollData)
    {
        base.UpdateData(scrollData);

        _data = scrollData as BulmabulTravelTargetData;

        if (_data == null)
        {
            Clear();
            return;
        }

        RefreshText();

        if (btnSelect != null)
        {
            btnSelect.onClick.RemoveAllListeners();
            btnSelect.interactable = true;
            btnSelect.onClick.AddListener(() =>
            {
                _data.onClick?.Invoke(_data.cellIndex);
            });
        }
    }

    private void RefreshText()
    {
        bool eng = IsEnglish();

        if (txtName != null)
        {
            txtName.text = string.IsNullOrWhiteSpace(_data.cellName)
                ? GetByLanguage($"지역 {_data.cellIndex}", $"Area {_data.cellIndex}")
                : _data.cellName;
        }

        if (txtType != null)
            txtType.text = GetCellTypeText(_data.cellType);

        if (txtInfo != null)
        {
            if (_data.cellType == BulmabulCellType.Land)
            {
                txtInfo.text = eng
                    ? $"Buy: {_data.buyCost:N0} / Toll: {_data.tollCost:N0}"
                    : $"구매: {_data.buyCost:N0} / 통행료: {_data.tollCost:N0}";
            }
            else
            {
                txtInfo.text = eng
                    ? $"Cell Index: {_data.cellIndex}"
                    : $"칸 번호: {_data.cellIndex}";
            }
        }

        RefreshOwnerText(eng);

        if (txtButton != null)
            txtButton.text = eng ? "Travel" : "여행하기";
    }

    private void RefreshOwnerText(bool eng)
    {
        bool isLand = _data.cellType == BulmabulCellType.Land;
        bool hasOwner = isLand && _data.ownerIndex >= 0;

        if (txtOwner != null)
        {
            if (!isLand)
            {
                txtOwner.text = eng ? "Not purchasable" : "구매 불가 칸";
            }
            else if (!hasOwner)
            {
                txtOwner.text = eng ? "Owner: None" : "소유자: 없음";
            }
            else
            {
                string ownerName = string.IsNullOrWhiteSpace(_data.ownerName)
                    ? $"P{_data.ownerIndex + 1}"
                    : _data.ownerName;

                txtOwner.text = eng
                    ? $"Owner: {ownerName}"
                    : $"소유자: {ownerName}";
            }
        }

        bool showTeamObject = _data.isTeamMode && hasOwner;

        if (teamModeObject != null)
            teamModeObject.SetActive(showTeamObject);

        if (txtTeamOwner != null)
        {
            if (!showTeamObject)
            {
                txtTeamOwner.text = "";
                return;
            }

            string teamText = GetTeamText(_data.ownerTeamSideInt, eng);

            txtTeamOwner.text = eng
                ? $"Team: {teamText}"
                : $"팀: {teamText}";
        }
    }

    private string GetTeamText(int teamSideInt, bool eng)
    {
        if (teamSideInt == (int)TeamSide.Red)
            return eng ? "Red" : "레드팀";

        if (teamSideInt == (int)TeamSide.Blue)
            return eng ? "Blue" : "블루팀";

        return eng ? "None" : "팀 없음";
    }

    private void Clear()
    {
        if (txtName != null)
            txtName.text = "";

        if (txtType != null)
            txtType.text = "";

        if (txtInfo != null)
            txtInfo.text = "";

        if (txtOwner != null)
            txtOwner.text = "";

        if (txtTeamOwner != null)
            txtTeamOwner.text = "";

        if (teamModeObject != null)
            teamModeObject.SetActive(false);

        if (txtButton != null)
            txtButton.text = "";

        if (btnSelect != null)
        {
            btnSelect.onClick.RemoveAllListeners();
            btnSelect.interactable = false;
        }
    }

    private string GetCellTypeText(BulmabulCellType type)
    {
        bool eng = IsEnglish();

        switch (type)
        {
            case BulmabulCellType.Start:
                return eng ? "Start" : "시작";
            case BulmabulCellType.Land:
                return eng ? "Land" : "땅";
            case BulmabulCellType.Tax:
                return eng ? "Tax" : "세금";
            case BulmabulCellType.Bonus:
                return eng ? "Bonus" : "보너스";
            case BulmabulCellType.Chance:
                return eng ? "Chance" : "찬스";
            case BulmabulCellType.Jail:
                return eng ? "Jail" : "감옥";
            case BulmabulCellType.Travel:
                return eng ? "Travel" : "여행";
            default:
                return eng ? "Unknown" : "알 수 없음";
        }
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
}