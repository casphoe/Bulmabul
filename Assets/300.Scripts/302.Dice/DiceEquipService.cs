using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public static class DiceEquipService
{
    /// <summary>
    /// 장착 가능한 키 (레벨 제외)
    /// Grade|Star
    /// </summary>
    public static string MakeEquipKey(OwnedDice d)
    {
        if (d == null) return "";
        return $"{d.Grade}|{d.Star}";
    }

    public static string MakeEquipKey(DiceGrade grade, int star)
    {
        return $"{grade}|{star}";
    }

    /// <summary>
    /// 교체 장착 (해제 없음)
    /// - target이 인벤토리에 있어야 함
    /// - 성공 시 Account에 반영 + 저장
    /// </summary>
    public static async Task<bool> EquipAndSaveAsync(Account acc, OwnedDice target)
    {
        if (acc == null || target == null) return false;
        if (acc.DiceInventory == null || acc.DiceInventory.Count == 0) return false;

        // 인벤에 존재하는지 확인
        var found = acc.DiceInventory.Find(d =>
            d == target || (d != null && d.Grade == target.Grade && d.Star == target.Star));

        if (found == null) return false;

        // 장착 갱신
        acc.EquippedDice = found;
        acc.EquippedDiceKey = MakeEquipKey(found);

        // 장착 상태 정합성 보정
        NormalizeEquippedDice(acc);

        await AccountCloudStore.SaveFullAsync(acc);
        return true;
    }

    /// <summary>
    /// 로그인 후 장착 복원
    /// - 키가 유효하면 복원
    /// - 없거나 유효하지 않으면 기본 장착(Common|1) 우선
    /// - 그것도 없으면 정렬 후 첫 번째
    /// </summary>
    public static void RestoreEquippedDice(Account acc)
    {
        if (acc == null) return;

        if (acc.DiceInventory == null || acc.DiceInventory.Count == 0)
        {
            // 인벤 비어있으면 null 허용 (비정상/예외 상황)
            acc.EquippedDice = null;
            acc.EquippedDiceKey = "";
            return;
        }

        // 1) 저장된 키로 복원 시도
        if (!string.IsNullOrWhiteSpace(acc.EquippedDiceKey))
        {
            var foundByKey = acc.DiceInventory.Find(d => d != null && MakeEquipKey(d) == acc.EquippedDiceKey);
            if (foundByKey != null)
            {
                acc.EquippedDice = foundByKey;
                return;
            }
        }

        // 2) 기본 장착(Common|1) 우선
        var defaultDice = acc.DiceInventory.Find(d => d != null && d.Grade == DiceGrade.Common && d.Star == 1);
        if (defaultDice != null)
        {
            acc.EquippedDice = defaultDice;
            acc.EquippedDiceKey = MakeEquipKey(defaultDice);
            return;
        }

        // 3) fallback: 정렬 후 최상위 1개 장착
        DiceInventorySort.SortDiceInventory(acc);
        acc.EquippedDice = acc.DiceInventory[0];
        acc.EquippedDiceKey = MakeEquipKey(acc.EquippedDice);
    }

    /// <summary>
    /// 인벤토리 변경(승급/합치기) 이후 장착 상태 정합성 유지
    /// - 장착 중이던 주사위가 사라졌으면 복원
    /// </summary>
    public static void NormalizeEquippedDice(Account acc)
    {
        if (acc == null) return;

        if (acc.DiceInventory == null || acc.DiceInventory.Count == 0)
        {
            acc.EquippedDice = null;
            acc.EquippedDiceKey = "";
            return;
        }

        bool existsRef = (acc.EquippedDice != null && acc.DiceInventory.Contains(acc.EquippedDice));
        if (existsRef)
        {
            acc.EquippedDiceKey = MakeEquipKey(acc.EquippedDice);
            return;
        }

        // 참조가 끊겼으면 키 기준 복원
        if (!string.IsNullOrWhiteSpace(acc.EquippedDiceKey))
        {
            var foundByKey = acc.DiceInventory.Find(d => d != null && MakeEquipKey(d) == acc.EquippedDiceKey);
            if (foundByKey != null)
            {
                acc.EquippedDice = foundByKey;
                return;
            }
        }

        // 없으면 기본 규칙으로 복원
        RestoreEquippedDice(acc);
    }

    /// <summary>
    /// 승급 결과 주사위를 장착으로 강제 교체 (자동 승급/등급 승급 시 사용)
    /// 저장은 호출자에서 한 번에 처리하는 걸 권장.
    /// </summary>
    public static void SetEquippedTo(Account acc, OwnedDice target)
    {
        if (acc == null || target == null) return;
        acc.EquippedDice = target;
        acc.EquippedDiceKey = MakeEquipKey(target);
    }
}
