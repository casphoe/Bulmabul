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

    [SerializeField] private GameObject equipShowPanel;
    [SerializeField] private GameObject promoteShowPanel;

    [Header("Controllers (각각 다른 InfiniteScroll)")]
    [SerializeField] private DiceInventoryListController equipList;
    [SerializeField] private DiceInventoryListController promoteList;

    [Header("Title Txt")]
    [SerializeField] private Text txtTitle;

    private Account _acc;
    private int selectNum = 0;

    private void Awake()
    {
        btnEquip.onClick.AddListener(() => OnBtnSelectClick(0));
        btnPromote.onClick.AddListener(() => OnBtnSelectClick(1));
    }

    private void OnEnable()
    {
        // 로그인 계정 바인딩 (필요시 여기서 갱신)
        var auth = FireBaseAuthManager.Instance;
        _acc = auth != null ? auth.CurrentAccount : null;

        if (equipList != null) equipList.BindAccount(_acc);
        if (promoteList != null) promoteList.BindAccount(_acc);

        OnBtnSelectClick(selectNum); // 현재 탭 유지
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
                if (equipTargetPanel != null) equipTargetPanel.SetActive(true);
                if (equipShowPanel != null) equipShowPanel.SetActive(true);
                if (equipList != null) equipList.ShowEquipList();
                break;
            case 1:
                if (promoteListPanel != null) promoteListPanel.SetActive(true);
                if (promoteShowPanel != null) promoteShowPanel.SetActive(true);
                if (promoteList != null) promoteList.ShowPromoteList();
                break;
        }
    }

    void AllDisable()
    {
        if (equipTargetPanel != null) equipTargetPanel.SetActive(false);
        if (promoteListPanel != null) promoteListPanel.SetActive(false);

        if (equipShowPanel != null) equipShowPanel.SetActive(false);
        if (promoteShowPanel != null) promoteShowPanel.SetActive(false);
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
