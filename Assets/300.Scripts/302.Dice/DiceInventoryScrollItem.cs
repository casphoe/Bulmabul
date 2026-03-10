using System;
using Gpm.Ui;
using UnityEngine;

public class DiceInventorySlotData : InfiniteScrollData
{
    public OwnedDice dice;
    public DiceInventorySlotUI.SlotMode mode;
    public bool isEquipped;
    public bool isSelected;
    public Action onClick;
}

public class DiceInventoryScrollItem : InfiniteScrollItem
{
    [SerializeField] private DiceInventorySlotUI slotUI;

    public override void UpdateData(InfiniteScrollData scrollData)
    {
        base.UpdateData(scrollData);

        var d = scrollData as DiceInventorySlotData;
        if (d == null)
        {
            slotUI?.Bind(null, DiceInventorySlotUI.SlotMode.Equip, false, null);
            return;
        }

        slotUI?.Bind(d.dice, d.mode, d.isEquipped, d.onClick);
        slotUI?.SetSelected(d.isSelected);
    }
}
