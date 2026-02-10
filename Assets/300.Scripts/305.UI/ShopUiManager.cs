using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public enum ShopUi
{
    Dice,Coin
}

public class ShopUiManager : MonoBehaviour
{
    [Header("주사위 뽑기, 재화 구매 버튼")]
    [SerializeField] Button[] btnShops;

    [Header("주사위 뽑기, 재화 구매 오브젝트 들")]
    [SerializeField] GameObject[] shopObjects;

    [Header("주사위 1회,10회 뽑기")]
    [SerializeField]
    Button[] btnDices;

    [Header("뽑기 횟수 50회에서 SR 보장 텍스트(남은 횟수)")]
    [SerializeField] Text txtPick;

    [Header("현재 샵 정보,주사위 뽑기인지 아니면 재화 뽑기(코인 뽑기)")]
    [SerializeField] ShopUi shop;

    const int ONE_COST = 10;

    static int TEN_COST => (ONE_COST * 10) - ONE_COST;

    bool _busy;

    private void Awake()
    {
        shop = ShopUi.Dice;
        for (int i = 0; i < btnShops.Length; i++)
        {
            int idx = i; // 클로저 방지
            btnShops[i].onClick.AddListener(() => ShopUiClick(idx));
        }

        // 뽑기 버튼 연결 + 가격 텍스트 세팅
        SetupDiceButtons();

        ApplyShopUI(shop);
        RefreshPickText();
    }

    private void OnEnable()
    {
        // 언어가 바뀌고 다시 켜질 때 텍스트 갱신
        RefreshDiceButtonTexts();
        RefreshPickText();
    }

    #region 주사위 뽑기 함수
    void SetupDiceButtons()
    {
        if (btnDices == null || btnDices.Length < 2) return;

        // 버튼 자식 Text에 가격 표시
        RefreshDiceButtonTexts();

        // 클릭 연결
        btnDices[0].onClick.AddListener(() => _ = TryPullAsync(1));
        btnDices[1].onClick.AddListener(() => _ = TryPullAsync(10));
    }

    void RefreshDiceButtonTexts()
    {
        if (btnDices == null || btnDices.Length < 2) return;

        var lang = (LaguageManager.Instance != null)
            ? LaguageManager.Instance.currentLang
            : Lauaguage.Kor;

        // 0: 1회
        SetButtonChildText(btnDices[0],
            (lang == Lauaguage.Eng)
                ? $"Pull x1 ({ONE_COST})"
                : $"1회 뽑기 {ONE_COST}원"
        );

        // 1: 10회
        SetButtonChildText(btnDices[1],
            (lang == Lauaguage.Eng)
                ? $"Pull x10 ({TEN_COST})"
                : $"10회 뽑기 {TEN_COST}원"
        );
    }

    void SetButtonChildText(Button btn, string text)
    {
        if (btn == null) return;
        var t = btn.GetComponentInChildren<Text>(true);
        if (t != null) t.text = text;
    }

    void ShopUiClick(int num)
    {
        shop = (ShopUi)num;
        ApplyShopUI(shop);
    }

    void ApplyShopUI(ShopUi target)
    {
        for (int i = 0; i < shopObjects.Length; i++)
            shopObjects[i].SetActive(i == (int)target);

        if (target == ShopUi.Dice)
        {
            RefreshDiceButtonTexts();
            RefreshPickText();
        }
    }

    void RefreshPickText()
    {
        var fb = FireBaseAuthManager.Instance;
        var acc = (fb != null) ? fb.CurrentAccount : null;
        if (acc == null) return;

        int remain = 50 - acc.DicePityCount; // 0이면 50회 남음
        remain = Mathf.Clamp(remain, 1, 50);

        var lang = (LaguageManager.Instance != null)
            ? LaguageManager.Instance.currentLang
            : Lauaguage.Kor;

        if (txtPick != null)
        {
            txtPick.text = (lang == Lauaguage.Eng)
                ? $"Epic guaranteed in {remain} pulls"
                : $"SR(Epic) 보장까지 {remain}회";
        }
    }


    async Task TryPullAsync(int count)
    {
        if (_busy) return;
        _busy = true;

        try
        {
            var fb = FireBaseAuthManager.Instance;
            if (fb == null || fb.CurrentAccount == null)
            {
                ToastMessageManager.instance.ShowToast("계정 정보가 없습니다.", "Account data missing.");
                return;
            }

            var acc = fb.CurrentAccount;

            int cost = (count == 10) ? TEN_COST : ONE_COST;

            // 재화 부족 → 얼마 부족한지 토스트
            if (acc.Cash < cost)
            {
                int lack = Mathf.CeilToInt(cost - acc.Cash);
                ToastMessageManager.instance.ShowToast(
                    $"재화가 부족합니다! {lack}원 부족해요.",
                    $"Not enough cash! You need {lack} more."
                );
                return;
            }

            // CSV 테이블 로드(이미 로드되어있으면 내부에서 return)
            DiceTables.LoadOrThrow();

            // 롤백 스냅샷 (저장 실패 대비)
            float prevCash = acc.Cash;
            int prevPity = acc.DicePityCount;
            int prevTotal = acc.DiceTotalPullCount;
            var prevInv = CloneInventory(acc.DiceInventory);
            var prevEquipped = CloneOwnedDice(acc.EquippedDice);

            // 1) 비용 차감
            acc.Cash -= cost;

            // 2) 뽑기
            if (count == 1)
            {
                Dice d = DiceTables.RollDiceSingle();
                d = ApplyEpicPityIfNeeded(acc, d);              // ✅ Epic(SR) pity 적용
                DiceTables.AddToAccountInventory(acc, d);       // ✅ 중복이면 exp → 레벨업 처리
                UpdateCounters(acc, d);
            }
            else
            {
                // 10회: DiceTables.RollDiceTen() 사용해야 "Rare 1개 보장 슬롯"이 그대로 유지됨
                List<Dice> list = DiceTables.RollDiceTen();

                for (int i = 0; i < list.Count; i++)
                {
                    Dice d = list[i];
                    d = ApplyEpicPityIfNeeded(acc, d);          // ✅ Epic(SR) pity 적용
                    DiceTables.AddToAccountInventory(acc, d);   // ✅ 중복 레벨업
                    UpdateCounters(acc, d);
                }
            }

            RefreshPickText();

            // 3) 다 뽑고 나서 한 번만 저장
            try
            {
                await AccountCloudStore.SaveFullAsync(acc);
                ToastMessageManager.instance.ShowToast("저장 완료!", "Saved!");
            }
            catch (Exception e)
            {
                // 저장 실패 → 롤백
                acc.Cash = prevCash;
                acc.DicePityCount = prevPity;
                acc.DiceTotalPullCount = prevTotal;
                acc.DiceInventory = prevInv;
                acc.EquippedDice = prevEquipped;

                RefreshPickText();

                Debug.LogWarning($"[DiceGacha SaveFail]\n{e}");
                ToastMessageManager.instance.ShowToast(
                    "저장에 실패했습니다. 잠시 후 다시 시도해 주세요.",
                    "Save failed. Please try again."
                );
            }
        }
        finally
        {
            _busy = false;
        }
    }

    bool IsSR_Epic(Dice d) => (d.grade == DiceGrade.Epic || d.grade == DiceGrade.Legendary);

    Dice ApplyEpicPityIfNeeded(Account acc, Dice rolled)
    {
        // dicePityCount가 49면 다음 1번은 무조건 Epic(SR)
        bool forceSR = acc.DicePityCount >= 49;
        if (!forceSR) return rolled;
        if (IsSR_Epic(rolled)) return rolled;

        // SR이 나올 때까지 재시도 (무한루프 방지)
        for (int i = 0; i < 200; i++)
        {
            var retry = DiceTables.RollDiceSingle(); // pity 상황에서만 분포가 달라도 OK(희소 이벤트)
            if (IsSR_Epic(retry)) return retry;
        }
        return rolled;
    }

    void UpdateCounters(Account acc, Dice rolled)
    {
        acc.DiceTotalPullCount += 1;

        if (IsSR_Epic(rolled))
            acc.DicePityCount = 0;
        else
            acc.DicePityCount = Mathf.Clamp(acc.DicePityCount + 1, 0, 49);
    }

    List<OwnedDice> CloneInventory(List<OwnedDice> src)
    {
        if (src == null) return new List<OwnedDice>();
        var list = new List<OwnedDice>(src.Count);
        for (int i = 0; i < src.Count; i++)
            list.Add(CloneOwnedDice(src[i]));
        return list;
    }

    OwnedDice CloneOwnedDice(OwnedDice d)
    {
        if (d == null) return null;
        return new OwnedDice
        {
            Grade = d.Grade,
            Star = d.Star,
            Level = d.Level,
            Count = d.Count,
            Exp = d.Exp
        };
    }
    #endregion
}
