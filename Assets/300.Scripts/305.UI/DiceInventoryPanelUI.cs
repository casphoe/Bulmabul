using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class DiceInventoryPanelUI : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] private Button btnEquip;
    [SerializeField] private Button btnPromote;

    [Header("Sub Panels")]
    [SerializeField] private GameObject equipTargetPanel;
    [SerializeField] private GameObject promoteListPanel;

    [Header("List Contents (Grid/Vertical Layout)")]
    [SerializeField] private Transform equipContentRoot;    // 장착 목록 content
    [SerializeField] private Transform promoteContentRoot;  // 승급 목록 content

    [Header("Slot Prefab")]
    [SerializeField] private DiceInventorySlotUI slotPrefab;

    [Header("Title Txt")]
    [SerializeField] private Text txtTitle;

    private Account _acc;
    private OwnedDice _selectedDice;
    private int selectNum = 0;

    // 생성된 슬롯 캐시(패널별)
    private readonly List<DiceInventorySlotUI> _equipSlots = new();
    private readonly List<DiceInventorySlotUI> _promoteSlots = new();

    private void Awake()
    {
        btnEquip.onClick.AddListener(() => OnBtnSelectClick(0));
        btnPromote.onClick.AddListener(() => OnBtnSelectClick(1));
    }

    private void Start()
    {
        OnBtnSelectClick(0);
    }

    void OnBtnSelectClick(int num)
    {
        selectNum = num;

        AllDisable();

        UpdateTitleByTab(selectNum);

        switch (selectNum)
        {
            case 0:
                equipTargetPanel.SetActive(true);
                break;
            case 1:
                promoteListPanel.SetActive(true);
                break;
        }
    }

    void AllDisable()
    {
        equipTargetPanel.SetActive(false);
        promoteListPanel.SetActive(false);
    }

    void UpdateTitleByTab(int tabIndex)
    {
        if (txtTitle == null) return;

        bool isEng = false;

        if (LaguageManager.Instance != null)
            isEng = (LaguageManager.Instance.currentLang == Lauaguage.Eng);

        switch (tabIndex)
        {
            case 0:
                txtTitle.text = isEng ? "Dice Equip" : "주사위 장착";
                break;

            case 1:
                txtTitle.text = isEng ? "Dice Promotion" : "주사위 승급";
                break;

            default:
                txtTitle.text = isEng ? "Dice" : "주사위";
                break;
        }
    }
}
