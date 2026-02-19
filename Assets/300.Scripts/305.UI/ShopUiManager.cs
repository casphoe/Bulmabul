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

    [Header("주사위 1회/10회 버튼 (0=1회, 1=10회)")]
    [SerializeField] Button[] btnDices;


    [Header("뽑기 횟수 50회에서 SR 보장 텍스트(남은 횟수)")]
    [SerializeField] Text txtPick;

    [Header("확인 팝업")]
    [SerializeField] GameObject pullConfirmPopup;
    [SerializeField] Text txtConfirmDesc;
    [SerializeField] Button btnConfirmClose;
    [SerializeField] GameObject doubleClickArea;

    [Header("더블클릭 연출 컨트롤러(선택)")]
    [SerializeField] DiceClickRevealFX diceRevealFx; // 아래 스크립트

    [Header("결과 팝업")]
    [SerializeField] DicePullResultPopup resultPopup;

    [Header("현재 샵 정보,주사위 뽑기인지 아니면 재화 뽑기(코인 뽑기)")]
    [SerializeField] ShopUi shop;

    [SerializeField] Toggle toggleSkip; // 인스펙터 연결

    const int ONE_COST = 10;

    static int TEN_COST => (ONE_COST * 10) - ONE_COST;

    int _pendingPullCount = 0;
    bool _isRolling = false;

    private void Awake()
    {
        shop = ShopUi.Dice;
        for (int i = 0; i < btnShops.Length; i++)
        {
            int idx = i; // 클로저 방지
            btnShops[i].onClick.AddListener(() => ShopUiClick(idx));
        }

        // 뽑기 버튼 연결 + 가격 텍스트 세팅
        ApplyShopUI(shop);

        SetupDiceButtons();
        SetupConfirmPopup();
        RefreshPityText();
    }

    #region 주사위 뽑기 함수

    bool IsSkipOn
    {
        get => (toggleSkip != null && toggleSkip.isOn);
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
    }

    void SetupDiceButtons()
    {
        if (btnDices == null || btnDices.Length < 2) return;

        var lang = (LaguageManager.Instance != null)
            ? LaguageManager.Instance.currentLang
            : Lauaguage.Kor;

        string oneText = (lang == Lauaguage.Eng) ? $"Single {ONE_COST}" : $"1회 뽑기 {ONE_COST}원";
        string tenText = (lang == Lauaguage.Eng) ? $"10 Pull {TEN_COST}" : $"10회 뽑기 {TEN_COST}원";

        SetButtonChildText(btnDices[0], oneText);
        SetButtonChildText(btnDices[1], tenText);

        btnDices[0].onClick.AddListener(() => OnPressPullButton(1));
        btnDices[1].onClick.AddListener(() => OnPressPullButton(10));
    }

    void SetupConfirmPopup()
    {
        if (pullConfirmPopup != null) pullConfirmPopup.SetActive(false);

        if (btnConfirmClose != null)
            btnConfirmClose.onClick.AddListener(CloseConfirmPopup);

        var dc = pullConfirmPopup != null
       ? pullConfirmPopup.GetComponentInChildren<DoubleClickTrigger>(true)
       : null;

        if (dc != null)
        {
            dc.Open(); // 아래에 ResetClick 추가할 거임
            dc.onDoubleClick = () => _ = OnConfirmDoubleClickAsync();
        }
        else
        {
            Debug.LogWarning("[ShopUiManager] DoubleClickTrigger를 찾지 못했습니다. (팝업 내부에 붙어있는지 확인)");
        }
    }

    void OnPressPullButton(int pullCount)
    {
        if (_isRolling) return;

        // 스킵 ON이면 "바로 뽑기"
        if (IsSkipOn)
        {
            _ = TryPullAsync(pullCount, skipReveal: true);
            return;
        }

        // 스킵 OFF면 확인창(0번 자식) 띄우고 더블클릭 대기
        OpenConfirmPopup_NoSkip(pullCount);
    }

    void OpenConfirmPopup_NoSkip(int pullCount)
    {
        _pendingPullCount = pullCount;

        if (pullConfirmPopup != null)
        {
            pullConfirmPopup.SetActive(true);
            SetConfirmPopupMode(skipMode: false); // 0번 자식 ON
        }

        //매번 새 뽑기 시작 시 상태 초기화
        if (diceRevealFx != null)
            diceRevealFx.ResetForNewPull();

        // 더블클릭 전 회전 연출 시작
        if (diceRevealFx != null)
            diceRevealFx.BeginIdle();


        var lang = (LaguageManager.Instance != null)
            ? LaguageManager.Instance.currentLang
            : Lauaguage.Kor;

        if (txtConfirmDesc != null)
        {
            txtConfirmDesc.text = (lang == Lauaguage.Eng)
                ? "Double-click to start."
                : "더블클릭하면 뽑기를 시작합니다.";
        }
    }

    void SetConfirmPopupMode(bool skipMode)
    {
        if (pullConfirmPopup == null) return;

        Transform root = pullConfirmPopup.transform;

        // 컨테이너(0번)
        if (root.childCount <= 0) return;
        Transform container = root.GetChild(0);

        // container의 0=NoSkip, 1=Skip
        if (container.childCount > 0) container.GetChild(0).gameObject.SetActive(!skipMode);
        if (container.childCount > 1) container.GetChild(1).gameObject.SetActive(skipMode);
    }


    void CloseConfirmPopup()
    {
        _pendingPullCount = 0;

        // EndIdle + 크기원복까지 포함
        if (diceRevealFx != null)
            diceRevealFx.ResetForNewPull();

        if (pullConfirmPopup != null)
            pullConfirmPopup.SetActive(false);
    }

    async Task OnConfirmDoubleClickAsync()
    {
        if (_isRolling) return;
        if (_pendingPullCount != 1 && _pendingPullCount != 10) return;

        // 더블클릭 시: 800x800 확대 + crack 연출 먼저
        if (diceRevealFx != null)
        {
            await diceRevealFx.PlayCrackAndBurstAsync();
            // 연출 도중/끝나고 "스킵 UI(1번 자식)"처럼 바꾸고 싶다면:
            if (pullConfirmPopup != null)
                SetConfirmPopupMode(skipMode: true);
        }

        await TryPullAsync(_pendingPullCount, skipReveal: false);
    }

    async Task TryPullAsync(int pullCount, bool skipReveal)
    {
        var auth = FireBaseAuthManager.Instance;
        if (auth == null || auth.CurrentAccount == null)
        {
            ToastMessageManager.instance.ShowToast("계정 정보가 없습니다.", "No account data.");
            return;
        }

        // 0) 비용 체크
        int cost = (pullCount == 10) ? TEN_COST : ONE_COST;
        float cash = auth.CurrentAccount.Cash;

        if (cash < cost)
        {
            float lack = cost - cash;

            // “얼마 부족” 토스트
            ToastMessageManager.instance.ShowToast(
                $"재화가 부족합니다. {lack:0}원 부족해요.",
                $"Not enough currency. You need {lack:0} more."
            );
            return;
        }

        // 1) CSV 테이블 로드(최초 1회)
        try
        {
            DiceTables.LoadOrThrow();
        }
        catch (Exception e)
        {
            Debug.LogError(e);
            ToastMessageManager.instance.ShowToast("주사위 테이블 로드 실패", "Failed to load dice tables.");
            return;
        }

        _isRolling = true;
        CloseConfirmPopup();

        // 2) 결과 팝업 열고 이펙트 재생
        if (resultPopup != null)
            resultPopup.Open(pullCount);

        // 3) 실제 뽑기 결과 생성 (+ pity 적용 예시)
        // Epic = SR, "50회에서 SR 보장": Epic 안 나왔으면 pityCount 증가, Epic 이상 나오면 pityCount 0
        var rolledList = new List<Dice>();

        if (pullCount == 1)
        {
            var d = RollWithPity(auth.CurrentAccount, PullType.Single);
            rolledList.Add(d);
        }
        else
        {
            // 10뽑은 1~9 + 10번째 Rare+ 보장 (네 DiceTables.RollDiceTen이 이미 그 구조)
            for (int i = 0; i < 9; i++)
                rolledList.Add(RollWithPity(auth.CurrentAccount, PullType.Ten_1to9));

            rolledList.Add(RollWithPity(auth.CurrentAccount, PullType.Ten_Slot10_Guarantee));
        }

        // 4) 이펙트 → 결과 표시
        // 스킵이면 연출 없이 결과만
        if (resultPopup != null)
        {
            if (skipReveal)
                resultPopup.ShowResultsInstant(rolledList);   // 연출 없이 바로 결과
            else
                await resultPopup.PlayEffectThenShowAsync(rolledList); // 연출 후 결과
        }

        // 5) 재화 차감
        auth.CurrentAccount.Cash -= cost;
        if (auth.CurrentAccount.Cash < 0) auth.CurrentAccount.Cash = 0;

        // 6) 인벤 반영(중복이면 exp/레벨업 처리)
        foreach (var d in rolledList)
            DiceTables.AddToAccountInventory(auth.CurrentAccount, d);

        // 7) Firebase 저장 (뽑기 끝난 후 한 번에)
        try
        {
            // 네 프로젝트에 이미 있는 저장 함수로 호출
            // (원자 저장을 원하면 UpdateChildrenAsync로 cash/diceInventory/pity 같이 올리면 됨)
            await AccountCloudStore.SaveFullAsync(auth.CurrentAccount);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Pull] Save failed: {e.Message}");
            ToastMessageManager.instance.ShowToast("저장 실패(네트워크 확인)", "Save failed (check network).");
        }

        RefreshPityText();
        _isRolling = false;
    }

    Dice RollWithPity(Account acc, PullType pullType)
    {
        // 너 rules에 dicePityCount 저장하도록 뚫어놨으니 Account에도 int로 들고있거나
        // accountEnc에 포함시키거나(권장) 해야 함.
        // 여기서는 acc에 "int DicePityCount" 필드가 있다고 가정하기 어려워서
        // 예시: accountEnc 내부에 있다면 그걸 연결해줘.
        // ----
        // 그래서 아래는 "필드가 있다고 가정한 샘플"이야:
        // acc.DicePityCount (0~49)

        // ⭐ 너 Account에 아래 필드 추가 추천:
        // [SerializeField] private int dicePityCount;
        // public int DicePityCount { get => dicePityCount; set => dicePityCount = Mathf.Clamp(value, 0, 49); }

        int pity = GetPityCount(acc); // <- 아래 헬퍼
        bool pityNow = (pity >= 49);

        Dice d = DiceTables.RollByPullType(pullType);

        // DiceTables.RollFrom이 private라면:
        // - 너가 RollDiceTen을 쓰고 있으니, Ten은 그냥 RollDiceTen으로 뽑고
        // - 1~9/10번째 개별 처리하고 싶으면 DiceTables에 public wrapper 하나 추가하는 게 제일 깔끔해.
        // 여기서는 "설명용"으로 썼고, 실제로는:
        // 1) single -> RollDiceSingle()
        // 2) ten -> RollDiceTen()으로 한 번에 받고 pity는 결과로 판단해서 pityCount만 업데이트
        // 로 단순화해도 돼.

        // pity 발동이면 Epic 이상 나올 때까지 재추첨 (안전장치 30회)
        if (pityNow)
        {
            int guard = 30;
            while (d.grade < DiceGrade.Epic && guard-- > 0)
            {
                d = (pullType == PullType.Single)
                    ? DiceTables.RollDiceSingle()
                    : d; // ten 개별 롤을 못 쓰면 pity 강제는 ten에서 "한 칸만 Epic으로 교체" 방식으로 처리 권장
            }
        }

        // pityCount 갱신
        if (d.grade >= DiceGrade.Epic) pity = 0;
        else pity = Mathf.Clamp(pity + 1, 0, 49);

        SetPityCount(acc, pity);
        return d;
    }

    // ===== pityCount 저장을 Account에 연결하기 위한 헬퍼 =====
    // 네가 Account에 DicePityCount를 추가하면 아래 두 함수는 한 줄로 끝남.
    int GetPityCount(Account acc)
    {
        // TODO: acc.DicePityCount 로 교체
        // 임시로 0 처리
        return 0;
    }

    void SetPityCount(Account acc, int value)
    {
        // TODO: acc.DicePityCount = value;
    }

    void RefreshPityText()
    {
        if (txtPick == null) return;

        var lang = (LaguageManager.Instance != null)
            ? LaguageManager.Instance.currentLang
            : Lauaguage.Kor;

        var auth = FireBaseAuthManager.Instance;
        int pity = (auth != null && auth.CurrentAccount != null) ? GetPityCount(auth.CurrentAccount) : 0;
        int remain = 50 - pity; // pity=0이면 50 남음, pity=49이면 1 남음

        txtPick.text = (lang == Lauaguage.Eng)
            ? $"SR(Epic) guaranteed in {remain}"
            : $"SR(Epic) 보장까지 {remain}회";
    }

    void SetButtonChildText(Button btn, string value)
    {
        if (btn == null) return;
        var t = btn.GetComponentInChildren<Text>(true);
        if (t != null) t.text = value;
    }
    #endregion
}
