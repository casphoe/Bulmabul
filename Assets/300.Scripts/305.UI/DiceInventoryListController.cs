using System;
using System.Collections.Generic;
using Gpm.Ui;
using UnityEngine;

public class DiceInventoryListController : MonoBehaviour
{
    [SerializeField] private InfiniteScroll scroll;

    private Account _acc;
    private DiceInventorySlotUI.SlotMode _mode;

    // 현재 선택(선택 하이라이트 갱신용)
    private OwnedDice _selected;

    //주사위 선택 이벤트
    public event Action<OwnedDice, DiceInventorySlotUI.SlotMode> OnSelectedDiceChanged;

    public OwnedDice Selected => _selected;
    public DiceInventorySlotUI.SlotMode Mode => _mode;

    public void ClearSelection()
    {
        _selected = null;
        OnSelectedDiceChanged?.Invoke(null, _mode);
    }

    public void BindAccount(Account acc)
    {
        _acc = acc;
    }

    public void ShowEquipList()
    {
        _mode = DiceInventorySlotUI.SlotMode.Equip;
        ClearSelection();
        Reload();
    }

    public void ShowPromoteList()
    {
        _mode = DiceInventorySlotUI.SlotMode.Promote;
        ClearSelection();
        Reload();
    }

    public void Reload()
    {
        if (scroll == null) return;
        if (_acc == null || _acc.DiceInventory == null) return;

        // 1) 정렬
        DiceInventorySort.SortDiceInventory(_acc);

        // 2) 스크롤 데이터 초기화
        scroll.ClearData(); // GPM 샘플/사용 패턴상 Clear 후 Insert로 다시 구성

        // 3) 목록 구성
        for (int i = 0; i < _acc.DiceInventory.Count; i++)
        {
            var dice = _acc.DiceInventory[i];
            if (dice == null) continue;

            if (dice.Count <= 0) continue;

            bool isEquipped = DiceEquipService.MakeEquipKey(dice) == _acc.EquippedDiceKey;

            // 요구사항: 장착중인 주사위는 인벤토리 칸에서 안 보이게
            if (_mode == DiceInventorySlotUI.SlotMode.Equip && isEquipped)
                continue;

            // 승급 탭: "승급 가능한 것만"
            if (_mode == DiceInventorySlotUI.SlotMode.Promote)
            {
                // 별 자동 승급(Shard) or 등급 승급(PromoteExp) 둘 중 하나라도 가능하면 노출
                bool canStarUp =
                      (dice.Level >= DiceProgression.MAX_LEVEL) &&
                      (dice.Star < DiceProgression.MAX_STAR) &&
                      (dice.Shard >= DiceProgression.STAR_UP_COST);

                bool canGradeUp = DiceProgression.CanGradePromote(dice);

                if (!canStarUp && !canGradeUp)
                    continue;
            }

            var data = new DiceInventorySlotData
            {
                dice = dice,
                mode = _mode,
                isEquipped = isEquipped,
                isSelected = (dice == _selected),
                onClick = () => OnClickDice(dice)
            };

            scroll.InsertData(data); // 문서의 InsertData 사용 :contentReference[oaicite:3]{index=3}
        }
    }

    private void OnClickDice(OwnedDice dice)
    {
        _selected = dice;

        // 선택 하이라이트 갱신하려면 "데이터 다시 갱신"이 가장 간단
        // (성능 더 신경쓰면 선택된 2개만 업데이트하는 방식도 가능)
        Reload();

        OnSelectedDiceChanged?.Invoke(_selected, _mode);
    }
}
