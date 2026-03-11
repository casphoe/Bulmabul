using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class DiceInventoryPanelUI : MonoBehaviour
{
    #region 변수
    [Header("Tab Buttons")]
    [SerializeField] private Button btnEquip;      // 탭: 장착
    [SerializeField] private Button btnPromote;    // 탭: 승급

    [Header("Action Buttons")]
    [SerializeField] private Button btnDoEquip;    // 실제 장착 실행
    [SerializeField] private Button btnDoPromote;  // 실제 승급 실행

    [Header("Sub Panels")]
    [SerializeField] private GameObject equipTargetPanel;
    [SerializeField] private GameObject promoteListPanel;
    [SerializeField] private GameObject equipShowPanel;
    [SerializeField] private GameObject promoteShowPanel;

    [Header("Controllers (각각 다른 InfiniteScroll)")]
    [SerializeField] private DiceInventoryListController equipList;
    [SerializeField] private DiceInventoryListController promoteList;

    [Header("Title Txt")]
    [SerializeField] private Text txtTitle;
    [SerializeField] private Text[] txtSubTitle;   // 0=Equip, 1=Promote

    [Header("장착된 주사위 정보(UI)")]
    [SerializeField] private Image equipDice;      // 장착된 주사위 이미지
    [SerializeField] private Text txtEquipInfo;    // 장착된 주사위 정보(등급/별/레벨 + shard/promote 등)

    [Header("승급 선택된 주사위 정보(UI)")]
    [SerializeField] private Image promoteDice;    // 승급 탭에서 선택한 주사위 이미지
    [SerializeField] private Text txtPromoteInfo;  // 승급 탭에서 선택한 주사위 정보

    private RainbowImageEffect _equipRainbow;
    private RainbowImageEffect _promoteRainbow;

    private Account _acc;
    private int selectNum = 0; // 0=Equip, 1=Promote

    // 탭별 선택 주사위
    private OwnedDice _selectedEquipDice;
    private OwnedDice _selectedPromoteDice;
    #endregion

    private void Awake()
    {
        if (btnEquip != null) btnEquip.onClick.AddListener(() => OnBtnSelectClick(0));
        if (btnPromote != null) btnPromote.onClick.AddListener(() => OnBtnSelectClick(1));

        if (btnDoEquip != null) btnDoEquip.onClick.AddListener(OnClickEquipSelectedDice);
        if (btnDoPromote != null) btnDoPromote.onClick.AddListener(OnClickPromoteSelectedDice);
    }

    private void OnEnable()
    {
        // 로그인 계정 바인딩
        var auth = FireBaseAuthManager.Instance;
        _acc = auth != null ? auth.CurrentAccount : null;

        if (equipList != null)
        {
            equipList.BindAccount(_acc);
            equipList.OnSelectedDiceChanged -= OnDiceSelected;
            equipList.OnSelectedDiceChanged += OnDiceSelected;
        }

        if (promoteList != null)
        {
            promoteList.BindAccount(_acc);
            promoteList.OnSelectedDiceChanged -= OnDiceSelected;
            promoteList.OnSelectedDiceChanged += OnDiceSelected;
        }

        // 장착 UI는 항상 최신으로
        RefreshEquippedUI();

        // 현재 탭 유지
        OnBtnSelectClick(selectNum);
    }

    private void Start()
    {
        OnBtnSelectClick(0);
    }

    private void OnBtnSelectClick(int num)
    {
        selectNum = num;

        AllDisable();
        UpdateTitleByTab(selectNum);

        // 탭 전환 시 버튼 상태/선택 표시 갱신
        RefreshActionButtons();
        RefreshPromoteSelectedUI(); // 승급 탭일 때만 표시됨
        RefreshEquippedUI();

        switch (selectNum)
        {
            case 0:
                equipTargetPanel?.SetActive(true);
                equipShowPanel?.SetActive(true);
                equipList?.ShowEquipList();
                break;

            case 1:
                promoteListPanel?.SetActive(true);
                promoteShowPanel?.SetActive(true);
                promoteList?.ShowPromoteList();
                break;
        }
    }

    private void OnDiceSelected(OwnedDice dice, DiceInventorySlotUI.SlotMode mode)
    {
        if (mode == DiceInventorySlotUI.SlotMode.Equip)
            _selectedEquipDice = dice;
        else
            _selectedPromoteDice = dice;

        RefreshActionButtons();

        // 승급 탭 선택 UI는 항상 갱신
        RefreshPromoteSelectedUI();
    }

    private void RefreshActionButtons()
    {
        // 장착 실행 버튼
        if (btnDoEquip != null)
            btnDoEquip.interactable = (selectNum == 0 && _acc != null && _selectedEquipDice != null);

        // 승급 실행 버튼
        if (btnDoPromote != null)
        {
            if (selectNum != 1 || _acc == null || _selectedPromoteDice == null)
            {
                btnDoPromote.interactable = false;
            }
            else
            {
                bool canStarUp =
                    (_selectedPromoteDice.Level >= DiceProgression.MAX_LEVEL) &&
                    (_selectedPromoteDice.Star < DiceProgression.MAX_STAR) &&
                    (_selectedPromoteDice.Shard >= DiceProgression.STAR_UP_COST);

                bool canGradeUp = DiceProgression.CanGradePromote(_selectedPromoteDice);

                btnDoPromote.interactable = (canStarUp || canGradeUp);
            }
        }
    }
   
    // 장착
    public async void OnClickEquipSelectedDice()
    {
        if (_acc == null || _selectedEquipDice == null) return;

        bool ok = await DiceEquipService.EquipAndSaveAsync(_acc, _selectedEquipDice);
        if (!ok)
        {
            ToastMessageManager.instance.ShowToast("장착 실패", "Equip failed.");
            return;
        }

        // 선택 해제
        _selectedEquipDice = null;

        // 장착 정합성/정렬
        DiceEquipService.RestoreEquippedDice(_acc);
        DiceInventorySort.SortDiceInventory(_acc);

        // UI 갱신
        RefreshEquippedUI();
        RefreshActionButtons();

        // 리스트 갱신:
        // - Equip 탭: 새로 장착된 건 숨김
        // - 이전 장착은 인벤으로 돌아오므로 다시 보여야 함
        equipList?.ShowEquipList();
        promoteList?.Reload();

        ToastMessageManager.instance.ShowToast("장착 완료", "Equipped.");
    }

    // 승급
    public async void OnClickPromoteSelectedDice()
    {
        if (_acc == null || _selectedPromoteDice == null) return;

        bool canStarUp =
            (_selectedPromoteDice.Level >= DiceProgression.MAX_LEVEL) &&
            (_selectedPromoteDice.Star < DiceProgression.MAX_STAR) &&
            (_selectedPromoteDice.Shard >= DiceProgression.STAR_UP_COST);

        bool canGradeUp = DiceProgression.CanGradePromote(_selectedPromoteDice);

        bool changed = false;

        // 1) 별 승급 우선(Shard 기반)
        if (canStarUp)
        {
            DiceTables.PromoteStarFromShards(_acc, _selectedPromoteDice);
            changed = true;
        }
        // 2) 등급 승급(PromoteExp 기반)
        else if (canGradeUp)
        {
            changed = DiceGradePromotion.TryPromoteGrade(_acc, _selectedPromoteDice);
        }

        if (!changed)
        {
            ToastMessageManager.instance.ShowToast("승급 불가", "Cannot promote.");
            RefreshActionButtons();
            return;
        }

        // 승급으로 인벤 구조가 바뀜 → 장착 정합성 보정
        DiceEquipService.NormalizeEquippedDice(_acc);

        // 정렬 유지
        DiceInventorySort.SortDiceInventory(_acc);

        // 저장
        await AccountCloudStore.SaveFullAsync(_acc);

        // 승급 후 선택은 무효가 될 수 있으니 해제
        _selectedPromoteDice = null;

        // UI 갱신
        RefreshEquippedUI();
        RefreshPromoteSelectedUI();
        RefreshActionButtons();

        // 리스트 갱신
        equipList?.Reload();
        promoteList?.ShowPromoteList();

        ToastMessageManager.instance.ShowToast("승급 완료", "Promoted.");
    }

    // UI 표시
    private void RefreshEquippedUI()
    {
        if (_acc == null)
        {
            SetEquippedUIEmpty();
            return;
        }

        DiceEquipService.RestoreEquippedDice(_acc);
        var eq = _acc.EquippedDice;

        if (eq == null)
        {
            SetEquippedUIEmpty();
            return;
        }

        DiceGradeColorUtil.Apply(equipDice, eq.Grade, ref _equipRainbow);

        if (txtEquipInfo != null)
            txtEquipInfo.text = BuildDiceInfoText(eq, IsEng(), showPromoteInfo: true);
    }

    private void SetEquippedUIEmpty()
    {
        DiceGradeColorUtil.Apply(equipDice, DiceGrade.Common, ref _equipRainbow);

        if (txtEquipInfo != null)
            txtEquipInfo.text = IsEng() ? "No equipped dice" : "장착된 주사위 없음";
    }

    // 승급 탭 선택 주사위 표시 (Equip 선택은 여기에서 표시하지 않음)
    private void RefreshPromoteSelectedUI()
    {
        // 승급 탭이 아니면 비움(원하면 숨김 처리도 가능)
        if (selectNum != 1)
        {
            SetPromoteSelectedUIEmpty();
            return;
        }

        if (_acc == null || _selectedPromoteDice == null)
        {
            SetPromoteSelectedUIEmpty();
            return;
        }

        DiceGradeColorUtil.Apply(promoteDice, _selectedPromoteDice.Grade, ref _promoteRainbow);

        if (txtPromoteInfo != null)
            txtPromoteInfo.text = BuildDiceInfoText(_selectedPromoteDice, IsEng(), showPromoteInfo: true);
    }

    private void SetPromoteSelectedUIEmpty()
    {
        DiceGradeColorUtil.Apply(promoteDice, DiceGrade.Common, ref _promoteRainbow);
        if (txtPromoteInfo != null) txtPromoteInfo.text = "";
    }

    // 등급/별/레벨 + 조각/승급치까지 한 번에 출력
    private string BuildDiceInfoText(OwnedDice d, bool isEng, bool showPromoteInfo)
    {
        if (d == null) return "";

        string gradeText = GetGradeText(d.Grade, isEng);

        // 1줄: 등급/별/레벨
        string line1 = isEng
            ? $"{gradeText}\n  ★{d.Star}\n  Lv.{d.Level}\n"
            : $"{gradeText}\n  ★{d.Star}\n  레벨 {d.Level}\n";

        if (!showPromoteInfo) return line1;

        // 2줄: shard / promoteExp
        // - Star < 5 이면 shard를 보는게 일반적
        // - Star==5, Lv10이면 PromoteExp가 의미 있음
        if (d.Level >= DiceProgression.MAX_LEVEL && d.Star >= DiceProgression.MAX_STAR)
        {
            // 등급 승급 재화
            string line2 = isEng
                ? $"Promote {d.PromoteExp}/{DiceProgression.GRADE_UP_COST}"
                : $"승급치 {d.PromoteExp}/{DiceProgression.GRADE_UP_COST}";
            return $"{line1}\n{line2}";
        }
        else
        {
            // 별 승급 shard
            string line2 = isEng
                ? $"Shard {d.Shard}/{DiceProgression.STAR_UP_COST}"
                : $"조각 {d.Shard}/{DiceProgression.STAR_UP_COST}";
            return $"{line1}\n{line2}";
        }
    }

    private bool IsEng()
    {
        return (LaguageManager.Instance != null && LaguageManager.Instance.currentLang == Lauaguage.Eng);
    }

    private string GetGradeText(DiceGrade grade, bool isEng)
    {
        if (isEng)
        {
            return grade switch
            {
                DiceGrade.Common => "Common",
                DiceGrade.Rare => "Rare",
                DiceGrade.Epic => "Epic",
                DiceGrade.Legendary => "Legendary",
                _ => "Unknown"
            };
        }

        return grade switch
        {
            DiceGrade.Common => "일반",
            DiceGrade.Rare => "희귀",
            DiceGrade.Epic => "영웅",
            DiceGrade.Legendary => "전설",
            _ => "알 수 없음"
        };
    }

    private void AllDisable()
    {
        equipTargetPanel?.SetActive(false);
        promoteListPanel?.SetActive(false);
        equipShowPanel?.SetActive(false);
        promoteShowPanel?.SetActive(false);
    }

    private void UpdateTitleByTab(int tabIndex)
    {
        if (txtTitle == null) return;

        bool isEng = IsEng();

        // 안전 체크
        Text equipSub = (txtSubTitle != null && txtSubTitle.Length > 0) ? txtSubTitle[0] : null;
        Text promoteSub = (txtSubTitle != null && txtSubTitle.Length > 1) ? txtSubTitle[1] : null;

        switch (tabIndex)
        {
            case 0:
                txtTitle.text = isEng ? "Dice Equip" : "주사위 장착";
                if (equipSub != null) equipSub.text = isEng ? "Equipped Dice" : "장착된 주사위";
                break;

            case 1:
                txtTitle.text = isEng ? "Dice Promotion" : "주사위 승급";
                if (promoteSub != null) promoteSub.text = isEng ? "Upgraded Dice" : "승급된 주사위";
                break;

            default:
                txtTitle.text = isEng ? "Dice" : "주사위";
                break;
        }
    }
}